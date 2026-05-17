// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using MMP.Herald.Configuration;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Sink-lifecycle management operations on <see cref="HeraldManagementApi"/>:
/// console / file sink enable-disable + level + retention, the per-sink
/// runtime override funnel, the channel-sink registry, plugin sink
/// provider configuration, and the per-kind <c>ApplySinkConfig</c>
/// dispatch the <c>CommitFull</c> path uses.
///
/// <para>The facade holds the public surface; this class holds the
/// bodies the facade delegates to.</para>
/// </summary>
internal sealed class SinkManagement
{
    private readonly IManagementContext _ctx;

    public SinkManagement(IManagementContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    // ── Console sink ──────────────────────────────────────────────────

    public ManagementResult SetConsoleSink(bool enabled, string? minLevel)
    {
        if (_ctx.EnsureAuthorized(nameof(SetConsoleSink)) is { } denied) return denied;
        if (enabled)
            _ctx.Builder.WithConsoleSink(minLevel: minLevel);
        else
            _ctx.Builder.WithoutConsoleSink();
        return _ctx.AutoCommitOrStage(enabled ? "Console sink enabled." : "Console sink removed.");
    }

    public ManagementResult UpdateConsoleMinLevel(string? minLevel)
    {
        if (_ctx.EnsureAuthorized(nameof(UpdateConsoleMinLevel)) is { } denied) return denied;
        try
        {
            _ctx.Builder.UpdateConsoleMinLevel(minLevel);
            return _ctx.AutoCommitOrStage($"Console minimum level updated to '{minLevel ?? "inherit"}'.");
        }
        catch (InvalidOperationException ex)
        {
            return ManagementResult.Fail(ex.Message);
        }
    }

    // ── File sink ─────────────────────────────────────────────────────

    public ManagementResult SetFileSink(bool enabled, string? path, string? minLevel)
    {
        if (_ctx.EnsureAuthorized(nameof(SetFileSink)) is { } denied) return denied;
        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ManagementResult.Fail("File path is required when enabling file sink.");

            // Confine path through LogRootDirectory. The principal review
            // wants the failure to surface as Fail rather than an
            // exception bubbling out of the public API; catch
            // InvalidOperationException here and translate.
            string confined;
            try
            {
                confined = _ctx.ResolveFileSinkPath(path);
            }
            catch (InvalidOperationException ex)
            {
                return ManagementResult.Fail(ex.Message);
            }
            _ctx.Builder.WithFileSink(confined, minLevel: minLevel);
        }
        else
        {
            _ctx.Builder.WithoutFileSink();
        }
        return _ctx.AutoCommitOrStage(enabled ? $"File sink enabled at '{path}'." : "File sink removed.");
    }

    public ManagementResult UpdateFileMinLevel(string? minLevel)
    {
        if (_ctx.EnsureAuthorized(nameof(UpdateFileMinLevel)) is { } denied) return denied;
        try
        {
            _ctx.Builder.UpdateFileMinLevel(minLevel);
            return _ctx.AutoCommitOrStage($"File minimum level updated to '{minLevel ?? "inherit"}'.");
        }
        catch (InvalidOperationException ex)
        {
            return ManagementResult.Fail(ex.Message);
        }
    }

    public ManagementResult UpdateFileRetentionPolicy(int? retentionDays, long? totalSizeCapBytes)
    {
        if (_ctx.EnsureAuthorized(nameof(UpdateFileRetentionPolicy)) is { } denied) return denied;
        try
        {
            var inspection = _ctx.Builder.Inspect();
            if (!inspection.HasFileSink)
                return ManagementResult.Fail("No file sink configured.");

            // Preserve existing rolling settings and merge in retention
            var policy = new FileSinkPolicy(
                Interval: inspection.FileRollingInterval ?? Services.JsonConfigProperties.IntervalNone,
                MaxBytes: inspection.FileMaxBytes,
                MaxRetainedFiles: inspection.FileMaxRetainedFiles,
                FileNameSuffix: inspection.FileNamePattern,
                RetentionDays: retentionDays,
                TotalSizeCapBytes: totalSizeCapBytes);

            _ctx.Builder.UpdateFileRollingPolicy(policy);
            var parts = new List<string>();
            if (retentionDays.HasValue) parts.Add($"retentionDays={retentionDays}");
            if (totalSizeCapBytes.HasValue) parts.Add($"totalSizeCap={totalSizeCapBytes}");
            return _ctx.AutoCommitOrStage($"File retention policy updated: {string.Join(", ", parts)}.");
        }
        catch (InvalidOperationException ex)
        {
            return ManagementResult.Fail(ex.Message);
        }
    }

    // ── Per-sink runtime ──────────────────────────────────────────────

    public HeraldManagementApi.SinkRuntimeApplyResult ApplySinkRuntime(
        string pipelineName,
        string sinkId,
        Configuration.Runtime.SinkRuntimeOverride incoming)
    {
        if (_ctx.EnsureAuthorized(nameof(ApplySinkRuntime)) is { } denied)
            return new HeraldManagementApi.SinkRuntimeApplyResult(denied, null, null, null, null, null, null, null, null);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkId);
        ArgumentNullException.ThrowIfNull(incoming);

        var overridesHolder = Routing.Loopback.SinkOverridesRegistry.Get(pipelineName, sinkId);
        var runStateHolder = Routing.Loopback.SinkRunStateRegistry.Get(pipelineName, sinkId);
        if (overridesHolder is null || runStateHolder is null)
        {
            return new HeraldManagementApi.SinkRuntimeApplyResult(
                ManagementResult.Fail($"Unknown sink '{sinkId}' on pipeline '{pipelineName}'."),
                null, null, null, null, null, null, null, null);
        }

        // RunState — parse the string into the enum; reject anything
        // that isn't one of the three legal values up front so the
        // holder never sees garbage.
        Configuration.Runtime.SinkRunState? previousRunState = null;
        Configuration.Runtime.SinkRunState? nextRunState = null;
        if (incoming.RunState is not null)
        {
            var parsed = ParseRunStateOrNull(incoming.RunState);
            if (parsed is null)
            {
                return new HeraldManagementApi.SinkRuntimeApplyResult(
                    ManagementResult.Fail($"Unknown runState '{incoming.RunState}'. Expected 'disabled', 'live', or 'test'."),
                    null, null, null, null, null, null, null, null);
            }
            nextRunState = parsed.Value;
            previousRunState = runStateHolder.Set(parsed.Value);
        }

        // MinLevel — "none"/""/null clears the gate; any other value
        // must match a registered level.
        Levels.LogLevel? previousMinLevel = null;
        Levels.LogLevel? nextMinLevel = null;
        var minLevelChanged = false;
        if (incoming.MinLevel is not null)
        {
            minLevelChanged = true;
            var key = incoming.MinLevel.Trim();
            if (string.IsNullOrEmpty(key) || key.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                nextMinLevel = null;
            }
            else
            {
                nextMinLevel = _ctx.Result.LevelRegistry.GetByKeyOrNull(key);
                if (nextMinLevel is null)
                {
                    return new HeraldManagementApi.SinkRuntimeApplyResult(
                        ManagementResult.Fail($"Unknown level '{key}'. Use a registered level key or 'none' to clear the gate."),
                        null, null, null, null, null, null, null, null);
                }
            }
            previousMinLevel = overridesHolder.SetMinLevel(nextMinLevel);
        }

        bool? previousTeeFile = null;
        if (incoming.TeeLiveToFile is { } teeFile)
            previousTeeFile = overridesHolder.SetTeeLiveToFile(teeFile);

        bool? previousTeeUrl = null;
        if (incoming.TeeLiveToUrl is { } teeUrl)
            previousTeeUrl = overridesHolder.SetTeeLiveToUrl(teeUrl);

        // Mirror to the builder so the JSON serializer carries the
        // change forward. Null fields fall through MergeWith. For
        // MinLevel an explicit clear ("none" / "") MUST overwrite a
        // previous gate, so we store the literal "none" sentinel
        // here rather than null — null in MergeWith means "don't
        // touch this field" and would silently keep the old value.
        var snapshotForBuilder = new Configuration.Runtime.SinkRuntimeOverride(
            RunState: nextRunState?.ToString().ToLowerInvariant(),
            TeeLiveToFile: incoming.TeeLiveToFile,
            TeeLiveToUrl: incoming.TeeLiveToUrl,
            MinLevel: minLevelChanged ? (nextMinLevel?.Key ?? "none") : null);
        _ctx.Builder.SinkRuntimeOverrides.Merge(sinkId, snapshotForBuilder);

        // Surface persistence failure through the wrapped ManagementResult so
        // the dashboard tells the operator the runtime was applied IN MEMORY
        // but the on-disk config wasn't updated. Previously the swallowed
        // exception let the API report success on a vaporised edit.
        var persistError = _ctx.PersistConfig();
        var apiResult = persistError is null
            ? ManagementResult.Ok($"Sink '{sinkId}' runtime updated.")
            : ManagementResult.Fail(
                $"Sink '{sinkId}' runtime updated in memory but {_ctx.FormatPersistFailure(persistError)}");

        return new HeraldManagementApi.SinkRuntimeApplyResult(
            apiResult,
            PreviousRunState:      previousRunState?.ToString().ToLowerInvariant(),
            RunState:              nextRunState?.ToString().ToLowerInvariant(),
            PreviousMinLevel:      minLevelChanged ? (previousMinLevel?.Key ?? "none") : null,
            MinLevel:              minLevelChanged ? (nextMinLevel?.Key ?? "none") : null,
            PreviousTeeLiveToFile: previousTeeFile,
            TeeLiveToFile:         incoming.TeeLiveToFile,
            PreviousTeeLiveToUrl:  previousTeeUrl,
            TeeLiveToUrl:          incoming.TeeLiveToUrl);
    }

    private static Configuration.Runtime.SinkRunState? ParseRunStateOrNull(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "disabled" => Configuration.Runtime.SinkRunState.Disabled,
            "live"     => Configuration.Runtime.SinkRunState.Live,
            "test"     => Configuration.Runtime.SinkRunState.Test,
            _          => (Configuration.Runtime.SinkRunState?)null
        };

    // ── Plugin sink provider configuration ────────────────────────────

    public ManagementResult ConfigureSinkProvider(
        string sinkKind, IReadOnlyDictionary<string, object?> values)
    {
        if (_ctx.EnsureAuthorized(nameof(ConfigureSinkProvider)) is { } denied) return denied;
        var provider = _ctx.Builder.SinkProviders.Get(sinkKind);
        if (provider is not Routing.IConfigurableSinkProvider configurable)
            return ManagementResult.Fail($"Sink provider '{sinkKind}' is not configurable or not registered.");

        var (success, error) = configurable.ApplyConfiguration(values);
        if (!success)
            return ManagementResult.Fail($"Configuration rejected by '{sinkKind}': {error}");

        return _ctx.AutoCommitOrStage($"Sink provider '{sinkKind}' configured.");
    }

    // ── Channels ──────────────────────────────────────────────────────

    public IReadOnlyList<ChannelInfo> GetChannels()
    {
        var channels = new List<ChannelInfo>();
        var inspection = _ctx.Builder.Inspect();
        foreach (var name in inspection.ChannelNames)
        {
            // We can't inspect the internal writer type, so report what we know
            channels.Add(new ChannelInfo(name, "configured", null, null));
        }
        return channels;
    }

    public ManagementResult AddChannel(ChannelInfo channel)
    {
        if (_ctx.EnsureAuthorized(nameof(AddChannel)) is { } denied) return denied;
        ArgumentNullException.ThrowIfNull(channel);
        if (string.IsNullOrWhiteSpace(channel.Name))
            return new ManagementResult(false, "Channel name is required.");

        MMP.Herald.Output.Rich.IRenderedLogOutputWriter writer = channel.OutputKind?.ToLowerInvariant() switch
        {
            "file" when !string.IsNullOrWhiteSpace(channel.Path) =>
                new MMP.Herald.Output.Writers.FileOutputWriter(channel.Path),
            "console" or null or "" =>
                new MMP.Herald.Output.Rich.DefaultRichConsoleWriter(),
            _ => new MMP.Herald.Output.Rich.DefaultRichConsoleWriter()
        };

        _ctx.Builder.Channels.Add(channel.Name, writer);
        return _ctx.AutoCommitOrStage($"Channel '{channel.Name}' added ({channel.OutputKind ?? "console"}).");
    }

    public ManagementResult RemoveChannel(string channelName)
    {
        if (_ctx.EnsureAuthorized(nameof(RemoveChannel)) is { } denied) return denied;
        _ctx.Builder.WithoutChannel(channelName);
        return _ctx.AutoCommitOrStage($"Channel '{channelName}' removed.");
    }

    public ManagementResult ClearChannels()
    {
        if (_ctx.EnsureAuthorized(nameof(ClearChannels)) is { } denied) return denied;
        _ctx.Builder.ClearChannels();
        return _ctx.AutoCommitOrStage("All channels cleared.");
    }

    // ── ApplySinkConfig dispatch (used by CommitFull) ─────────────────

    /// <summary>
    /// Apply sink-specific configuration from the commit payload.
    /// Maps JSON properties back to builder methods. Reads the v1/v2
    /// envelope through <see cref="SinkConfigEnvelope.Lift"/> so a
    /// single shape feeds the rest of the method.
    /// </summary>
    public void ApplySinkConfig(JsonElement sinkEl, JsonElement configEl)
    {
        var sinkId = sinkEl.TryGetProperty("sinkId", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(sinkId)) return;

        // Determine sink kind from sinkId (e.g. "console", "json_file", "json_file:new_1")
        var kind = sinkId.Contains(':') ? sinkId.Split(':')[0] : sinkId;

        // v1/v2 envelope normalization is one helper now — see
        // SinkConfigEnvelope.Lift. Returns the inner properties element
        // (or the original under v1), the outer minLevel string, and a
        // flag so the file-sink reader below can re-target the outer
        // envelope for minLevel under v2.
        var envelope = SinkConfigEnvelope.Lift(configEl);
        var sinkConfigEl = envelope.Properties;
        var isV2Envelope = envelope.IsV2Envelope;

        // Apply minLevel if present. minLevel is metadata, not a sink-config
        // property, so it stays on the outer envelope under both contracts.
        if (envelope.MinLevel is not null || configEl.TryGetProperty("minLevel", out _))
        {
            var minLevel = envelope.MinLevel;
            if (kind == Services.KnownSinkKinds.Console)
                _ctx.Builder.UpdateConsoleMinLevel(minLevel);
            else if (kind == Services.KnownSinkKinds.JsonFile || kind == Services.KnownSinkKinds.TextFile)
                _ctx.Builder.UpdateFileMinLevel(minLevel);
        }

        // Apply file sink properties. The dashboard POSTs camelCase JSON
        // matching FileSinkConfig's [JsonPropertyName] attributes, so a
        // single Deserialize call replaces the per-key TryGetProperty
        // pile that used to live here.
        if (kind == Services.KnownSinkKinds.JsonFile || kind == Services.KnownSinkKinds.TextFile || kind == Services.KnownSinkKinds.ProtobufFile)
        {
            // Source-gen typed overload keeps this AOT/trim-safe — see
            // HeraldJsonContext for the FileSinkConfig binding.
            var fileConfig = sinkConfigEl.Deserialize(HeraldJsonContext.Default.FileSinkConfig)
                ?? new FileSinkConfig();
            // text_file's v2 mmpform renamed fileNamePattern → namePattern.
            // FileSinkConfig still binds the legacy name; pull the alt
            // spelling out of the bag so a v2 commit's name pattern lands.
            if (string.IsNullOrEmpty(fileConfig.FileNamePattern)
                && sinkConfigEl.TryGetProperty("namePattern", out var npEl)
                && npEl.ValueKind == JsonValueKind.String)
            {
                fileConfig = fileConfig with { FileNamePattern = npEl.GetString() };
            }
            // minLevel rides on the outer envelope, not in the bag — re-read
            // it here when the v2 lift moved focus to the inner properties.
            if (isV2Envelope && envelope.MinLevel is not null)
            {
                fileConfig = fileConfig with { MinLevel = envelope.MinLevel };
            }
            if (!string.IsNullOrEmpty(fileConfig.LogFileTemplate))
            {
                var path = string.IsNullOrEmpty(fileConfig.LogDirectory)
                    ? fileConfig.LogFileTemplate
                    : $"{fileConfig.LogDirectory}/{fileConfig.LogFileTemplate}";
                if (!string.IsNullOrEmpty(fileConfig.LogExtension)) path += $".{fileConfig.LogExtension}";

                // Confine the operator-supplied path through
                // LogRootDirectory before wiring it into the pipeline.
                // ResolveFileSinkPath throws InvalidOperationException
                // on an escape; the enclosing CommitFull catch turns
                // that into a ManagementResult.Fail with the resolver's
                // message, matching the "reject, don't crash" contract.
                path = _ctx.ResolveFileSinkPath(path);

                if (fileConfig.RollingLogsEnabled)
                {
                    // fileNamePattern is the .NET date format injected into rolled
                    // file names. It maps to JsonFileRollingConfig.FileNameSuffix
                    // — the same field BuilderInspection surfaces back as
                    // FileNamePattern, so the form's value round-trips through
                    // both directions of the API.
                    _ctx.Builder.WithFileSink(
                        path,
                        fileConfig.RollingInterval ?? "daily",
                        maxBytes:          ParseFileSize(fileConfig.MaxFileSize),
                        maxRetainedFiles:  fileConfig.MaxRetainedFiles,
                        fileNameSuffix:    fileConfig.FileNamePattern,
                        minLevel:          fileConfig.MinLevel,
                        retentionDays:     fileConfig.RetentionDays,
                        totalSizeCapBytes: ParseFileSize(fileConfig.TotalSizeCap));
                }
                else
                {
                    _ctx.Builder.WithFileSink(path, minLevel: fileConfig.MinLevel);
                }
            }
        }

        // Network / integration sinks. The per-kind dispatch lives in
        // NetworkSinkDispatch so this method and RestoreBuilderFromConfig
        // share one table — adding a new URI sink takes one row, not
        // two parallel branches.
        if (NetworkSinkDispatch.UriSinks.TryGetValue(kind, out var uriApply))
        {
            var uri = ReadStringProperty(configEl, "uri");
            var minLvl = ReadStringProperty(configEl, "minLevel");
            // Optional headers ride on the same JSON shape the round-trip
            // emits: { "headers": { "Authorization": "Bearer ..." } }.
            // Missing object → null; missing keys / non-string values are
            // skipped silently so a malformed header entry does not crash
            // sink wiring.
            var headers = ReadHeadersProperty(configEl);
            if (!string.IsNullOrEmpty(uri)) uriApply(_ctx.Builder, uri, minLvl, headers);
        }
        else if (NetworkSinkDispatch.HostPortSinks.TryGetValue(kind, out var hpSpec))
        {
            var host = ReadStringProperty(configEl, "host");
            var port = ReadIntProperty(configEl, "port") ?? hpSpec.DefaultPort;
            var minLvl = ReadStringProperty(configEl, "minLevel");
            if (!string.IsNullOrEmpty(host)) hpSpec.Apply(_ctx.Builder, host, port, minLvl);
        }
        else if (kind == Services.KnownSinkKinds.Channel)
        {
            var channelName = configEl.TryGetProperty("name", out var cnEl) && cnEl.ValueKind == JsonValueKind.String ? cnEl.GetString() : null;
            if (!string.IsNullOrEmpty(channelName))
            {
                // Channel sinks need a writer — use the default console writer as a placeholder
                // The real writer is configured at construction time, not through JSON
            }
        }

        // Apply config to custom configurable sink providers
        var provider = _ctx.Builder.SinkProviders.Get(kind);
        if (provider is Routing.IConfigurableSinkProvider configurable)
        {
            var values = new Dictionary<string, object?>();
            foreach (var prop in configEl.EnumerateObject())
            {
                values[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.TryGetInt32(out var i) ? i : prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
            }
            configurable.ApplyConfiguration(values);
        }
    }

    /// <summary>Parse human-readable file size (100MB, 1GB, 500KB) to bytes.</summary>
    internal static long? ParseFileSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "0") return null;
        value = value.Trim().ToUpperInvariant();
        if (value.EndsWith("GB")) return (long)(double.Parse(value[..^2]) * 1024 * 1024 * 1024);
        if (value.EndsWith("MB")) return (long)(double.Parse(value[..^2]) * 1024 * 1024);
        if (value.EndsWith("KB")) return (long)(double.Parse(value[..^2]) * 1024);
        if (long.TryParse(value, out var bytes)) return bytes;
        return null;
    }

    // Small JSON-element readers shared by the network-sink dispatch
    // (and anywhere else that needs the "string | null" / "int | null"
    // shape from a TryGetProperty + ValueKind check). One pair instead
    // of inline two-liners scattered across the file.
    internal static string? ReadStringProperty(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    internal static int? ReadIntProperty(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : (int?)null;

    /// <summary>
    /// Read an optional <c>headers</c> object out of a sink-config JSON
    /// element. Used by ApplySinkConfig so the dashboard's "save sink"
    /// payload can carry Bearer-token / API-key headers alongside the
    /// usual uri + minLevel fields. Non-string values are skipped; an
    /// absent or empty object yields null so sinks that don't use
    /// headers see the pre-existing shape exactly.
    ///
    /// <para>Names and values are validated through
    /// <see cref="HttpHeaderSanitizer.Sanitize"/> so a CRLF in a value
    /// or a non-token character in a name silently drops the entry
    /// rather than letting it ride through to the HTTP client and
    /// split the request on the wire.</para>
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? ReadHeadersProperty(JsonElement el)
    {
        if (!el.TryGetProperty("headers", out var v)) return null;
        if (v.ValueKind != JsonValueKind.Object) return null;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in v.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                var s = prop.Value.GetString();
                if (s is not null) result[prop.Name] = s;
            }
        }
        return HttpHeaderSanitizer.Sanitize(result);
    }
}
