// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Pipeline-policy management operations on <see cref="HeraldManagementApi"/>:
/// strategy selection, the async / batching / sampling / flight-recorder
/// / post-filtering knobs the fast-path companion steps read, plus the
/// enricher, event-processor, alias, property-style, and category-style
/// registries.
///
/// <para>The facade holds the public surface; this class holds the
/// bodies the facade delegates to.</para>
/// </summary>
internal sealed class PolicyManagement
{
    private readonly IManagementContext _ctx;

    public PolicyManagement(IManagementContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    // ── Strategy ──────────────────────────────────────────────────────

    public ManagementResult SetPipelineStrategy(string strategyName)
    {
        if (_ctx.EnsureAuthorized(nameof(SetPipelineStrategy)) is { } denied) return denied;
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);

        PipelineStrategy strategy = strategyName.ToLowerInvariant() switch
        {
            "default" => PipelineStrategy.Default(),
            "filterearly" or "filter_early" or "filter-early" => PipelineStrategy.FilterEarly(),
            "minimal" => PipelineStrategy.Minimal(),
            _ => throw new ArgumentException($"Unknown strategy: '{strategyName}'. Use: default, filterEarly, minimal.")
        };

        _ctx.Builder.WithPipelineStrategy(strategy);
        return _ctx.AutoCommitOrStage($"Pipeline strategy set to '{strategyName}'.");
    }

    public ManagementResult SetPipelineStrategyCustom(IReadOnlyList<string> stepNames)
    {
        if (_ctx.EnsureAuthorized(nameof(SetPipelineStrategyCustom)) is { } denied) return denied;
        ArgumentNullException.ThrowIfNull(stepNames);
        try
        {
            var strategy = PipelineStrategy.FromNames(stepNames);
            _ctx.Builder.WithPipelineStrategy(strategy);
            return _ctx.AutoCommitOrStage($"Custom pipeline strategy set: [{string.Join(", ", stepNames)}].");
        }
        catch (ArgumentException ex)
        {
            return ManagementResult.Fail(ex.Message);
        }
    }

    public ManagementResult SetTraceCorrelation(bool enabled)
    {
        if (_ctx.EnsureAuthorized(nameof(SetTraceCorrelation)) is { } denied) return denied;
        if (enabled)
            _ctx.Builder.WithTraceCorrelation();
        else
            _ctx.Builder.WithoutTraceCorrelation();
        return _ctx.AutoCommitOrStage(enabled ? "Trace correlation enabled." : "Trace correlation disabled.");
    }

    public ManagementResult SetLevelDump(bool enabled)
    {
        if (_ctx.EnsureAuthorized(nameof(SetLevelDump)) is { } denied) return denied;
        if (enabled)
            _ctx.Builder.WithLevelDump();
        else
            _ctx.Builder.WithoutLevelDump();
        return _ctx.AutoCommitOrStage(enabled ? "Level dump enabled." : "Level dump disabled.");
    }

    // ── Fast-path companion policies (async / batching / sampling /
    //    flight recorder / post-filtering) ─────────────────────────────

    public ManagementResult SetAsyncConfig(
        bool enabled,
        int? capacity,
        string? dropStrategy,
        bool? deferRendering)
    {
        if (_ctx.EnsureAuthorized(nameof(SetAsyncConfig)) is { } denied) return denied;
        if (!enabled)
        {
            _ctx.Builder.WithoutAsyncLogging();
            return _ctx.AutoCommitOrStage("Async logging disabled.");
        }

        // Guard clauses: validate before any mutation so a rejected
        // request leaves the builder untouched.
        var capacityValue = capacity ?? Services.PipelineDefaults.AsyncCapacity;
        if (capacityValue <= 0)
            return ManagementResult.Fail($"Async capacity must be > 0 (got {capacityValue}).");

        var strategyValue = dropStrategy ?? Services.KnownDropStrategies.DropWrite;
        if (!IsKnownDropStrategy(strategyValue))
            return ManagementResult.Fail(
                $"Unknown async drop strategy '{strategyValue}'. Expected one of: " +
                $"{Services.KnownDropStrategies.DropWrite}, {Services.KnownDropStrategies.DropOldest}, {Services.KnownDropStrategies.Wait}.");

        var deferValue = deferRendering ?? false;
        _ctx.Builder.WithAsyncLogging(capacityValue, strategyValue, deferValue);
        return _ctx.AutoCommitOrStage(
            $"Async logging enabled (capacity={capacityValue}, dropStrategy={strategyValue}, deferRendering={deferValue}).");
    }

    public ManagementResult SetBatchingConfig(
        bool enabled,
        int? maxBatchSize,
        int? maxBatchDelayMs)
    {
        if (_ctx.EnsureAuthorized(nameof(SetBatchingConfig)) is { } denied) return denied;
        if (!enabled)
        {
            _ctx.Builder.WithoutBatching();
            return _ctx.AutoCommitOrStage("Batching disabled.");
        }

        var sizeValue = maxBatchSize ?? Services.PipelineDefaults.BatchSize;
        if (sizeValue <= 0)
            return ManagementResult.Fail($"Batch size must be > 0 (got {sizeValue}).");

        var delayValue = maxBatchDelayMs ?? Services.PipelineDefaults.BatchDelayMs;
        if (delayValue <= 0)
            return ManagementResult.Fail($"Batch delay must be > 0 ms (got {delayValue}).");

        _ctx.Builder.WithBatching(sizeValue, delayValue);
        return _ctx.AutoCommitOrStage(
            $"Batching enabled (maxBatchSize={sizeValue}, maxBatchDelayMs={delayValue}).");
    }

    public ManagementResult SetSamplingRate(int rate)
    {
        if (_ctx.EnsureAuthorized(nameof(SetSamplingRate)) is { } denied) return denied;
        if (rate < 0)
            return ManagementResult.Fail($"Sampling rate must be >= 0 (got {rate}).");

        if (rate == 0)
        {
            _ctx.Builder.WithoutSampling();
            return _ctx.AutoCommitOrStage("Sampling disabled.");
        }

        _ctx.Builder.WithSampling(rate);
        return _ctx.AutoCommitOrStage($"Sampling enabled (1-in-{rate}).");
    }

    public ManagementResult SetFlightRecorderConfig(
        bool enabled,
        int? bufferSize,
        string? minLevel,
        string? triggerLevel)
    {
        if (_ctx.EnsureAuthorized(nameof(SetFlightRecorderConfig)) is { } denied) return denied;
        if (!enabled)
        {
            _ctx.Builder.WithoutFlightRecorder();
            return _ctx.AutoCommitOrStage("Flight recorder disabled.");
        }

        var sizeValue = bufferSize ?? 200;
        if (sizeValue <= 0)
            return ManagementResult.Fail($"Flight-recorder buffer size must be > 0 (got {sizeValue}).");

        _ctx.Builder.WithFlightRecorder(sizeValue, minLevel, triggerLevel);
        return _ctx.AutoCommitOrStage(
            $"Flight recorder enabled (bufferSize={sizeValue}, minLevel={minLevel ?? "inherit"}, triggerLevel={triggerLevel ?? "error"}).");
    }

    public ManagementResult SetPostFilteringConfig(
        bool enabled,
        Predicates.PredicateSpec? triggerCondition,
        Predicates.PredicateSpec? normalFilter,
        Predicates.PredicateSpec? escalatedFilter,
        int? maxBatchSize,
        int? maxBatchDelayMs)
    {
        if (_ctx.EnsureAuthorized(nameof(SetPostFilteringConfig)) is { } denied) return denied;
        if (!enabled)
        {
            _ctx.Builder.WithoutPostFiltering();
            return _ctx.AutoCommitOrStage("Post-filtering disabled.");
        }

        if (triggerCondition is null || normalFilter is null || escalatedFilter is null)
            return ManagementResult.Fail("Post-filtering requires triggerCondition, normalFilter, and escalatedFilter.");

        var sizeValue = maxBatchSize ?? Services.PipelineDefaults.BatchSize;
        if (sizeValue <= 0)
            return ManagementResult.Fail($"Post-filtering batch size must be > 0 (got {sizeValue}).");

        var delayValue = maxBatchDelayMs ?? Services.PipelineDefaults.BatchDelayMs;
        if (delayValue <= 0)
            return ManagementResult.Fail($"Post-filtering batch delay must be > 0 ms (got {delayValue}).");

        _ctx.Builder.WithPostFiltering(triggerCondition, normalFilter, escalatedFilter, sizeValue, delayValue);
        return _ctx.AutoCommitOrStage(
            $"Post-filtering enabled (maxBatchSize={sizeValue}, maxBatchDelayMs={delayValue}).");
    }

    // Drop-strategy validation kept as a small private helper so the
    // valid set lives in one place. The known strategies are constants on
    // KnownDropStrategies, so a switch expression stays exhaustive and
    // dirt-cheap.
    private static bool IsKnownDropStrategy(string value) => value switch
    {
        Services.KnownDropStrategies.DropWrite => true,
        Services.KnownDropStrategies.DropOldest => true,
        Services.KnownDropStrategies.Wait => true,
        _ => false
    };

    // ── Enrichers ─────────────────────────────────────────────────────

    public ManagementResult RemoveEnricher(string name)
    {
        if (_ctx.EnsureAuthorized(nameof(RemoveEnricher)) is { } denied) return denied;
        _ctx.Builder.Enrichers.Remove(name);
        return _ctx.AutoCommitOrStage($"Enricher '{name}' removed.");
    }

    public ManagementResult ClearEnrichers()
    {
        if (_ctx.EnsureAuthorized(nameof(ClearEnrichers)) is { } denied) return denied;
        _ctx.Builder.Enrichers.Clear();
        return _ctx.AutoCommitOrStage("All enrichers cleared.");
    }

    public ManagementResult ResetEnrichers()
    {
        if (_ctx.EnsureAuthorized(nameof(ResetEnrichers)) is { } denied) return denied;
        _ctx.Builder.Enrichers.Reset();
        return _ctx.AutoCommitOrStage("Enrichers reset to defaults.");
    }

    // ── Event processors ──────────────────────────────────────────────

    public ManagementResult RemoveEventProcessor(string name)
    {
        if (_ctx.EnsureAuthorized(nameof(RemoveEventProcessor)) is { } denied) return denied;
        _ctx.Builder.WithoutEventProcessor(name);
        return _ctx.AutoCommitOrStage($"Event processor '{name}' removed.");
    }

    public ManagementResult ClearEventProcessors()
    {
        if (_ctx.EnsureAuthorized(nameof(ClearEventProcessors)) is { } denied) return denied;
        _ctx.Builder.ClearEventProcessors();
        return _ctx.AutoCommitOrStage("All non-protected event processors cleared.");
    }

    // ── Property styles ───────────────────────────────────────────────

    public ManagementResult SetPropertyStyle(string propertyName, string colorName,
        bool bold, bool italic, string? backgroundColor)
    {
        if (_ctx.EnsureAuthorized(nameof(SetPropertyStyle)) is { } denied) return denied;
        _ctx.Builder.SetPropertyStyle(propertyName, colorName, bold, italic, backgroundColor);
        return _ctx.AutoCommitOrStage($"Property style set for '{propertyName}'.");
    }

    public ManagementResult RemovePropertyStyle(string propertyName)
    {
        if (_ctx.EnsureAuthorized(nameof(RemovePropertyStyle)) is { } denied) return denied;
        _ctx.Builder.WithoutPropertyStyle(propertyName);
        return _ctx.AutoCommitOrStage($"Property style removed for '{propertyName}'.");
    }

    public ManagementResult ClearPropertyStyles()
    {
        if (_ctx.EnsureAuthorized(nameof(ClearPropertyStyles)) is { } denied) return denied;
        _ctx.Builder.ClearPropertyStyles();
        return _ctx.AutoCommitOrStage("All custom property styles cleared.");
    }

    // ── Category styles ───────────────────────────────────────────────

    public IReadOnlyList<CategoryStyleInfo> GetCategoryStyles()
    {
        var inspection = _ctx.Builder.Inspect();
        return inspection.CategoryStyles ?? [];
    }

    public ManagementResult SetCategoryStyle(string categoryName, string? colorName,
        bool bold, bool italic, string? backgroundColorName)
    {
        if (_ctx.EnsureAuthorized(nameof(SetCategoryStyle)) is { } denied) return denied;
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);
        _ctx.Builder.SetCategoryStyle(categoryName, colorName, bold, italic, backgroundColorName);
        return _ctx.AutoCommitOrStage($"Category style for '{categoryName}' set.");
    }

    public ManagementResult RemoveCategoryStyle(string categoryName)
    {
        if (_ctx.EnsureAuthorized(nameof(RemoveCategoryStyle)) is { } denied) return denied;
        _ctx.Builder.WithoutCategoryStyle(categoryName);
        return _ctx.AutoCommitOrStage($"Category style override for '{categoryName}' removed.");
    }

    public ManagementResult ClearCategoryStyles()
    {
        if (_ctx.EnsureAuthorized(nameof(ClearCategoryStyles)) is { } denied) return denied;
        _ctx.Builder.ClearCategoryStyles();
        return _ctx.AutoCommitOrStage("All category style overrides cleared.");
    }

    // ── Aliases ───────────────────────────────────────────────────────

    public IReadOnlyDictionary<string, string> GetAliases() => _ctx.Builder.GetAliases();

    public ManagementResult SetAlias(string id, string alias)
    {
        if (_ctx.EnsureAuthorized(nameof(SetAlias)) is { } denied) return denied;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _ctx.Builder.WithAlias(id, alias ?? "");
        return _ctx.AutoCommitOrStage($"Alias for '{id}' set to '{alias}'.");
    }

    public ManagementResult RemoveAlias(string id)
    {
        if (_ctx.EnsureAuthorized(nameof(RemoveAlias)) is { } denied) return denied;
        _ctx.Builder.WithoutAlias(id);
        return _ctx.AutoCommitOrStage($"Alias for '{id}' removed.");
    }

    // ── Pipeline-decorator plugin configuration ───────────────────────

    public ManagementResult ConfigurePipelineDecorator(
        string stepName, IReadOnlyDictionary<string, object?> values)
    {
        if (_ctx.EnsureAuthorized(nameof(ConfigurePipelineDecorator)) is { } denied) return denied;
        var decorator = _ctx.Builder.GetPipelineDecorator(stepName);
        if (decorator is null)
            return ManagementResult.Fail($"Pipeline decorator '{stepName}' is not registered.");

        var (success, error) = decorator.ApplyConfiguration(values);
        if (!success)
            return ManagementResult.Fail($"Configuration rejected by '{stepName}': {error}");

        return _ctx.AutoCommitOrStage($"Pipeline decorator '{stepName}' configured.");
    }
}
