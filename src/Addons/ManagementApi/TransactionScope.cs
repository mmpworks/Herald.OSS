// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MMP.Herald.Configuration;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Transactional commit and rollback for <see cref="HeraldManagementApi"/>:
/// Begin / Commit / Rollback, the <c>CommitFull(json)</c> end-to-end
/// apply path the dashboard's bulk save sends, the downtime-rebuild
/// fallback for pipelines that don't support hot-swap, and the
/// <c>AutoCommitOrStage</c> funnel every per-axis mutation routes
/// through.
///
/// <para>The facade holds the public surface; this class holds the
/// bodies the facade delegates to.</para>
/// </summary>
internal sealed class TransactionScope
{
    private readonly IManagementContext _ctx;
    private readonly SinkManagement _sinks;

    public TransactionScope(IManagementContext ctx, SinkManagement sinks)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));
    }

    // ── Begin / Commit / Rollback ─────────────────────────────────────

    public ManagementResult BeginTransaction()
    {
        if (_ctx.EnsureAuthorized(nameof(BeginTransaction)) is { } denied) return denied;
        if (_ctx.InTransaction)
            return ManagementResult.Fail("Transaction already active. Commit or rollback first.");

        _ctx.Snapshot = _ctx.Builder.ExportConfig();
        _ctx.SetInTransaction(true);
        return ManagementResult.Ok("Transaction started. Make changes, then commit or rollback.");
    }

    public ManagementResult CommitTransaction()
    {
        if (_ctx.EnsureAuthorized(nameof(CommitTransaction)) is { } denied) return denied;
        if (!_ctx.InTransaction)
            return ManagementResult.Fail("No active transaction. Call BeginTransaction first.");

        var validation = _ctx.Builder.Validate();
        if (validation.HasCritical)
        {
            var messages = string.Join("; ", validation.Issues
                .Where(i => i.Severity == ValidationSeverity.Critical)
                .Select(i => i.Message));
            return ManagementResult.Fail($"Validation failed: {messages}");
        }

        _ctx.SetInTransaction(false);
        _ctx.Snapshot = null;

        // Always save config to disk — this is the essential operation.
        // The config represents the desired state regardless of whether
        // the live pipeline can be hot-swapped right now. Fail loudly
        // when the write fails: a commit that reports Ok but never
        // touched disk is the exact lie this fix removes.
        var persistError = _ctx.PersistConfig();
        if (persistError is not null)
            return ManagementResult.Fail(_ctx.FormatPersistFailure(persistError));

        // Attempt to hot-swap the live pipeline. Non-fatal if it fails —
        // the config is saved and will be applied on next restart.
        var swapped = _ctx.Result.RebuildFrom(_ctx.Builder);
        if (swapped)
            return ManagementResult.Ok("Configuration saved. Pipeline rebuilt and swapped atomically.");

        // Hot-swap unavailable — try rebuild with downtime
        if (_ctx.Registration is not null)
            return RebuildWithDowntime();

        return ManagementResult.Ok("Configuration saved. Pipeline unchanged (restart required).");
    }

    public ManagementResult RollbackTransaction()
    {
        if (_ctx.EnsureAuthorized(nameof(RollbackTransaction)) is { } denied) return denied;
        if (!_ctx.InTransaction || _ctx.Snapshot is null)
            return ManagementResult.Fail("No active transaction to rollback.");

        // Restore builder state by resetting and re-applying from snapshot
        // Note: We can't fully deserialize back into the builder (some types aren't
        // JSON-representable like ILogSinkProvider instances), but we can restore
        // the scalar config by rebuilding from the snapshot JSON.
        _ctx.Builder.Reset();
        _ctx.RestoreFromJson(_ctx.Snapshot);

        _ctx.SetInTransaction(false);
        _ctx.Snapshot = null;
        return ManagementResult.Ok("Transaction rolled back. Builder restored to pre-transaction state.");
    }

    // ── Full commit (dashboard bulk save) ─────────────────────────────

    public ManagementResult CommitFull(string json)
    {
        if (_ctx.EnsureAuthorized(nameof(CommitFull)) is { } denied) return denied;
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Apply strategy from steps
            if (root.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
            {
                var stepNames = new List<string>();
                foreach (var stepEl in stepsEl.EnumerateArray())
                {
                    if (stepEl.TryGetProperty("stepName", out var nameEl))
                    {
                        var stepName = nameEl.GetString();
                        if (!string.IsNullOrEmpty(stepName))
                            stepNames.Add(stepName);
                    }

                    // Apply alias if present
                    if (stepEl.TryGetProperty("alias", out var aliasEl) && aliasEl.ValueKind == JsonValueKind.String)
                    {
                        var stepName = stepEl.GetProperty("stepName").GetString();
                        var alias = aliasEl.GetString();
                        if (!string.IsNullOrEmpty(stepName))
                            _ctx.Builder.WithAlias(stepName, alias ?? "");
                    }
                }

                if (stepNames.Count > 0)
                {
                    var strategy = PipelineStrategy.FromNames(stepNames);
                    _ctx.Builder.WithPipelineStrategy(strategy);
                }
            }

            // Apply sink configurations with full CRUD:
            // 1. Snapshot which sinks currently exist on the builder
            // 2. Ensure each sink in the payload exists (create if new)
            // 3. Apply config updates
            // 4. Remove sinks that are no longer in the payload
            if (root.TryGetProperty("sinks", out var sinksEl) && sinksEl.ValueKind == JsonValueKind.Array)
            {
                var priorInspection = _ctx.Builder.Inspect();

                // Collect the sink kinds requested in this commit
                var requestedKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sinkEl in sinksEl.EnumerateArray())
                {
                    var rawId = sinkEl.TryGetProperty("sinkId", out var idEl) ? idEl.GetString() : null;
                    if (string.IsNullOrEmpty(rawId)) continue;
                    var sinkKind = rawId.Contains(':') ? rawId.Split(':')[0] : rawId;
                    requestedKinds.Add(sinkKind);
                }

                // Remove sinks that existed before but are not in the new payload
                if (priorInspection.HasConsoleSink && !requestedKinds.Contains(Services.KnownSinkKinds.Console))
                    _ctx.Builder.WithoutConsoleSink();
                if (priorInspection.HasFileSink)
                {
                    var priorFileKind = priorInspection.FileKind ?? Services.KnownSinkKinds.TextFile;
                    if (!requestedKinds.Contains(Services.KnownSinkKinds.JsonFile) &&
                        !requestedKinds.Contains(Services.KnownSinkKinds.TextFile) &&
                        !requestedKinds.Contains(Services.KnownSinkKinds.ProtobufFile))
                        _ctx.Builder.WithoutFileSink();
                }

                // Ensure sinks exist before applying config (create if new)
                if (requestedKinds.Contains(Services.KnownSinkKinds.Console) && !priorInspection.HasConsoleSink)
                    _ctx.Builder.WithConsoleSink();
                // File sinks are created via WithFileSink() inside ApplySinkConfig, which handles both create and update

                foreach (var sinkEl in sinksEl.EnumerateArray())
                {
                    // Apply alias
                    if (sinkEl.TryGetProperty("sinkId", out var sinkIdEl) &&
                        sinkEl.TryGetProperty("alias", out var sinkAliasEl) &&
                        sinkAliasEl.ValueKind == JsonValueKind.String)
                    {
                        var sinkId = sinkIdEl.GetString();
                        var alias = sinkAliasEl.GetString();
                        if (!string.IsNullOrEmpty(sinkId))
                            _ctx.Builder.WithAlias(sinkId, alias ?? "");
                    }

                    // Apply sink config properties
                    if (sinkEl.TryGetProperty("config", out var configEl) && configEl.ValueKind == JsonValueKind.Object)
                    {
                        _sinks.ApplySinkConfig(sinkEl, configEl);
                    }
                }
            }

            // Apply pipeline-level loopback fields. Each is optional
            // and only writes when present in the body so a partial
            // commit (e.g. "edit only the sinks") leaves these
            // settings untouched.
            if (root.TryGetProperty("testLoopbackUrl", out var lbUrlEl))
            {
                _ctx.Builder.WithTestLoopbackUrl(
                    lbUrlEl.ValueKind == JsonValueKind.String ? lbUrlEl.GetString() : null);
            }
            if (root.TryGetProperty("testLoopbackLogDir", out var lbDirEl))
            {
                _ctx.Builder.WithTestLoopbackLogDir(
                    lbDirEl.ValueKind == JsonValueKind.String ? lbDirEl.GetString() : null);
            }
            if (root.TryGetProperty("loopbackEntriesPerFile", out var lbCapEl)
                && lbCapEl.ValueKind == JsonValueKind.Number)
            {
                _ctx.Builder.WithLoopbackEntriesPerFile(lbCapEl.GetInt32());
            }
            if (root.TryGetProperty("loopbackUseNdjson", out var lbFmtEl)
                && (lbFmtEl.ValueKind == JsonValueKind.True
                 || lbFmtEl.ValueKind == JsonValueKind.False))
            {
                _ctx.Builder.WithLoopbackUseNdjson(lbFmtEl.GetBoolean());
            }

            // Validate
            var validation = _ctx.Builder.Validate();
            if (validation.HasCritical)
            {
                var messages = string.Join("; ", validation.Issues
                    .Where(i => i.Severity == ValidationSeverity.Critical)
                    .Select(i => i.Message));
                return ManagementResult.Fail($"Validation failed: {messages}");
            }

            // Save config to disk (always — essential operation). A
            // failure here is fatal to CommitFull because the whole
            // contract is "configuration saved AND pipeline updated";
            // returning Ok with the save quietly dropped is the lie
            // this fix exists to eliminate.
            var persistError = _ctx.PersistConfig();
            if (persistError is not null)
                return ManagementResult.Fail(_ctx.FormatPersistFailure(persistError));

            // Attempt hot-swap first (zero downtime)
            var swapped = _ctx.Result.RebuildFrom(_ctx.Builder);
            if (swapped)
                return ManagementResult.Ok("Configuration saved and pipeline rebuilt.");

            // Hot-swap unavailable — try rebuild with downtime (messages lost during rebuild)
            if (_ctx.Registration is not null)
                return RebuildWithDowntime();

            return ManagementResult.Ok("Configuration saved. Pipeline unchanged (restart required).");
        }
        catch (Exception ex)
        {
            return ManagementResult.Fail($"Commit failed: {ex.Message}");
        }
    }

    // ── Downtime rebuild ──────────────────────────────────────────────

    public ManagementResult RebuildWithDowntime()
    {
        if (_ctx.EnsureAuthorized(nameof(RebuildWithDowntime)) is { } denied) return denied;
        if (_ctx.Registration is null)
            return ManagementResult.Fail("RebuildWithDowntime requires a registered pipeline.");

        var validation = _ctx.Builder.Validate();
        if (validation.HasCritical)
        {
            var messages = string.Join("; ", validation.Issues
                .Where(i => i.Severity == ValidationSeverity.Critical)
                .Select(i => i.Message));
            return ManagementResult.Fail($"Validation failed: {messages}");
        }

        // Save config first. Fail before tearing down the live
        // pipeline if the disk write didn't succeed — a downtime
        // rebuild that loses both the running pipeline AND the
        // on-disk record is the worst possible outcome.
        var persistError = _ctx.PersistConfig();
        if (persistError is not null)
            return ManagementResult.Fail(_ctx.FormatPersistFailure(persistError));

        // Build a completely new pipeline
        var newResult = _ctx.Builder.BuildAndCommit();

        // Swap the registration's result — new callers get the new pipeline
        var registration = _ctx.Registration;
        registration.Result = newResult;

        // Update our own reference so subsequent API calls use the new result
        _ctx.ReplaceResult(newResult);

        // Invalidate cached API on registration so it rebuilds with new result
        registration.InvalidateApi();

        return ManagementResult.Ok(
            "Pipeline rebuilt with downtime. Configuration saved. " +
            "Events during rebuild were dropped. New pipeline is active.");
    }

    // ── AutoCommitOrStage funnel every per-axis mutation calls ────────

    /// <summary>
    /// If in a transaction, the change is staged (no pipeline swap).
    /// If not in a transaction, auto-commits immediately.
    /// </summary>
    public ManagementResult AutoCommitOrStage(string message)
    {
        if (_ctx.InTransaction)
            return ManagementResult.Ok($"[staged] {message}");

        // Auto-commit: validate, save, then try hot-swap
        var validation = _ctx.Builder.Validate();
        if (validation.HasCritical)
            return ManagementResult.Ok($"{message} (not committed — validation has critical issues)");

        // Always save config — this is the essential operation. Fail
        // loudly when the write fails so the per-PATCH funnel can't
        // report Ok on an edit that never reached disk.
        var persistError = _ctx.PersistConfig();
        if (persistError is not null)
            return ManagementResult.Fail($"{message} {_ctx.FormatPersistFailure(persistError)}");

        // Attempt hot-swap (non-fatal if unavailable)
        var swapped = _ctx.Result.RebuildFrom(_ctx.Builder);
        if (swapped)
            return ManagementResult.Ok($"{message} Pipeline rebuilt.");

        // Hot-swap unavailable — try rebuild with downtime
        if (_ctx.Registration is not null)
            return RebuildWithDowntime();

        return ManagementResult.Ok($"{message} Saved. Pipeline unchanged (restart required).");
    }

    // ── Reset / ApplyConfigJson ───────────────────────────────────────

    public ManagementResult Reset()
    {
        if (_ctx.EnsureAuthorized(nameof(Reset)) is { } denied) return denied;
        _ctx.Builder.Reset();
        return AutoCommitOrStage("Builder reset to initial state.");
    }

    public ManagementResult ApplyConfigJson(string json)
    {
        if (_ctx.EnsureAuthorized(nameof(ApplyConfigJson)) is { } denied) return denied;
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            _ctx.Builder.Reset();
            _ctx.RestoreFromJson(json);
            return AutoCommitOrStage("Configuration applied from JSON.");
        }
        catch (Exception ex)
        {
            return ManagementResult.Fail($"Failed to apply config: {ex.Message}");
        }
    }
}
