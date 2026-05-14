#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Configuration.Runtime;

namespace MMP.Herald.Configuration;

/// <summary>
/// Compares two logging runtime configurations and produces a diff
/// describing which areas changed. Enables surgical hot reload decisions.
///
/// <para>
/// The sink comparison reports both <c>SinksChanged</c> (boolean) and
/// <c>SinkChange</c> (typed kind). A property-only edit on an existing
/// sink — change of <c>path</c>, <c>uri</c>, retry policy, run state, or
/// any property-bag value — produces <c>SinksChanged = true</c> and
/// <c>SinkChange = PropertyOnly</c>. A change to the set of <c>(Name, Kind)</c>
/// pairs (added, removed, or kind-renamed) produces
/// <c>SinkChange = Structural</c>. Pre-fix only the structural case was
/// detected; property-only edits silently dropped on the level-only fast
/// path.
/// </para>
/// </summary>
public static class ConfigDiffDetector
{
    public static ConfigDiff Detect(
        LoggingRuntimeConfiguration oldConfig,
        LoggingRuntimeConfiguration newConfig)
    {
        ArgumentNullException.ThrowIfNull(oldConfig);
        ArgumentNullException.ThrowIfNull(newConfig);

        var levelChanged = !string.Equals(
            oldConfig.PipelinePolicy.MinimumLevel.Key,
            newConfig.PipelinePolicy.MinimumLevel.Key,
            StringComparison.OrdinalIgnoreCase);

        var sinkChange = ClassifySinkChange(oldConfig.Sinks, newConfig.Sinks);
        var routesChanged = !RoutesEqual(oldConfig.Routes, newConfig.Routes);

        var presentationChanged =
            !StylesEqual(oldConfig.Presentation.LevelStyles, newConfig.Presentation.LevelStyles) ||
            !PropertyStylesEqual(oldConfig.Presentation.PropertyStyles, newConfig.Presentation.PropertyStyles) ||
            !CategoryStylesEqual(oldConfig.Presentation.CategoryStyles, newConfig.Presentation.CategoryStyles) ||
            !AliasesEqual(oldConfig.Presentation.Aliases, newConfig.Presentation.Aliases);

        var pipelineStructureChanged =
            oldConfig.PipelinePolicy.AsyncPolicy.IsEnabled != newConfig.PipelinePolicy.AsyncPolicy.IsEnabled ||
            oldConfig.PipelinePolicy.BatchingPolicy.IsEnabled != newConfig.PipelinePolicy.BatchingPolicy.IsEnabled ||
            oldConfig.PipelinePolicy.HotReloadEnabled != newConfig.PipelinePolicy.HotReloadEnabled ||
            (oldConfig.PipelinePolicy.PostFilteringPolicy is not null) !=
            (newConfig.PipelinePolicy.PostFilteringPolicy is not null) ||
            (oldConfig.PipelinePolicy.FlightRecorderPolicy is not null) !=
            (newConfig.PipelinePolicy.FlightRecorderPolicy is not null) ||
            (oldConfig.PipelinePolicy.SamplingFilter is not null) !=
            (newConfig.PipelinePolicy.SamplingFilter is not null);

        return new ConfigDiff(
            levelChanged,
            routesChanged,
            SinksChanged: sinkChange != SinkChangeKind.None,
            presentationChanged,
            pipelineStructureChanged,
            sinkChange);
    }

    // Walk both lists in order and assign a kind. Two-stage check:
    //   1. If the (Name, Kind) sequence differs → Structural (one or more
    //      sinks added, removed, or had their kind renamed).
    //   2. Otherwise compare each pair's full record. Any field difference
    //      → PropertyOnly. All identical → None.
    // Cognitive Complexity: kept linear by extracting both helpers; the
    // method itself is a three-way switch on observed state.
    private static SinkChangeKind ClassifySinkChange(
        IReadOnlyList<LoggingRuntimeSinkDefinition> a,
        IReadOnlyList<LoggingRuntimeSinkDefinition> b)
    {
        if (a.Count != b.Count) return SinkChangeKind.Structural;
        if (!IdentitySequenceEqual(a, b)) return SinkChangeKind.Structural;
        return AllSinksDeepEqual(a, b) ? SinkChangeKind.None : SinkChangeKind.PropertyOnly;
    }

    private static bool IdentitySequenceEqual(
        IReadOnlyList<LoggingRuntimeSinkDefinition> a,
        IReadOnlyList<LoggingRuntimeSinkDefinition> b)
    {
        // Kind matches the OrdinalIgnoreCase contract every sink-kind
        // registry uses (S2). Two configs that differ only by casing on
        // Kind ("Console" vs "console") resolve the same provider and
        // must classify as identical here. Name stays case-sensitive
        // because operators reference names directly in routes.
        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Name, b[i].Name, StringComparison.Ordinal)) return false;
            if (!string.Equals(a[i].Kind, b[i].Kind, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static bool AllSinksDeepEqual(
        IReadOnlyList<LoggingRuntimeSinkDefinition> a,
        IReadOnlyList<LoggingRuntimeSinkDefinition> b)
    {
        for (var i = 0; i < a.Count; i++)
        {
            if (!SinkDefinitionEqual(a[i], b[i])) return false;
        }
        return true;
    }

    // Compare every field individually. Record-default equality would do
    // a reference compare on the Properties dict (and on RetryPolicy /
    // RollingPolicy), so two structurally equal configs loaded twice would
    // differ. The explicit walk below uses value equality everywhere,
    // including a key/value compare on Properties.
    private static bool SinkDefinitionEqual(LoggingRuntimeSinkDefinition x, LoggingRuntimeSinkDefinition y) =>
        string.Equals(x.Name, y.Name, StringComparison.Ordinal)
        // Kind compare must match IdentitySequenceEqual's OrdinalIgnoreCase
        // so two configs differing only by Kind casing classify as None,
        // not PropertyOnly — they resolve the same provider via S2.
        && string.Equals(x.Kind, y.Kind, StringComparison.OrdinalIgnoreCase)
        && string.Equals(x.Path, y.Path, StringComparison.Ordinal)
        && string.Equals(x.Alias, y.Alias, StringComparison.Ordinal)
        && string.Equals(x.Uri, y.Uri, StringComparison.Ordinal)
        && string.Equals(x.Host, y.Host, StringComparison.Ordinal)
        && x.Port == y.Port
        && Equals(x.RetryPolicy, y.RetryPolicy)
        && Equals(x.RollingPolicy, y.RollingPolicy)
        && Equals(x.MinLevel, y.MinLevel)
        && string.Equals(x.OutputTemplate, y.OutputTemplate, StringComparison.Ordinal)
        && x.IsAudit == y.IsAudit
        && x.EnableMetrics == y.EnableMetrics
        && string.Equals(x.Label, y.Label, StringComparison.Ordinal)
        && x.RunState == y.RunState
        && string.Equals(x.TestOutFileSuffix, y.TestOutFileSuffix, StringComparison.Ordinal)
        && x.TeeLiveToFile == y.TeeLiveToFile
        && x.TeeLiveToUrl == y.TeeLiveToUrl
        && PropertiesEqual(x.Properties, y.Properties);

    private static bool PropertiesEqual(
        IReadOnlyDictionary<string, object?>? a,
        IReadOnlyDictionary<string, object?>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null) return b is null || b.Count == 0;
        if (b is null) return a.Count == 0;
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var other)) return false;
            if (!Equals(kv.Value, other)) return false;
        }
        return true;
    }

    private static bool RoutesEqual(
        System.Collections.Generic.IReadOnlyList<LoggingRuntimeRouteDefinition> a,
        System.Collections.Generic.IReadOnlyList<LoggingRuntimeRouteDefinition> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        return a.Select(r => r.SinkName).SequenceEqual(b.Select(r => r.SinkName));
    }

    private static bool StylesEqual(
        System.Collections.Generic.IReadOnlyList<LoggingRuntimeLevelStyleDefinition> a,
        System.Collections.Generic.IReadOnlyList<LoggingRuntimeLevelStyleDefinition> b)
    {
        return a.Count == b.Count && a.SequenceEqual(b);
    }

    private static bool PropertyStylesEqual(
        System.Collections.Generic.IReadOnlyList<LoggingRuntimePropertyStyleDefinition> a,
        System.Collections.Generic.IReadOnlyList<LoggingRuntimePropertyStyleDefinition> b)
    {
        return a.Count == b.Count && a.SequenceEqual(b);
    }

    private static bool AliasesEqual(
        System.Collections.Generic.IReadOnlyList<LoggingRuntimeAliasDefinition> a,
        System.Collections.Generic.IReadOnlyList<LoggingRuntimeAliasDefinition> b)
    {
        return a.Count == b.Count && a.SequenceEqual(b);
    }

    private static bool CategoryStylesEqual(
        System.Collections.Generic.IReadOnlyList<LoggingRuntimeCategoryStyleDefinition>? a,
        System.Collections.Generic.IReadOnlyList<LoggingRuntimeCategoryStyleDefinition>? b)
    {
        // Treat missing or empty as equivalent: a category-style upgrade on
        // an older config does not surface as a presentation change on the
        // first reload after the upgrade.
        var left = a ?? Array.Empty<LoggingRuntimeCategoryStyleDefinition>();
        var right = b ?? Array.Empty<LoggingRuntimeCategoryStyleDefinition>();
        return left.Count == right.Count && left.SequenceEqual(right);
    }
}
