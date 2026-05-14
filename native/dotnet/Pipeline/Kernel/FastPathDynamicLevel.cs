#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MMP.Herald.Events;
using MMP.Herald.Levels;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Kernel-aware dynamic-level resolver. Wraps a global
/// <see cref="LogLevelSwitch"/> whose level can be mutated at runtime
/// and exposes a <see cref="ShouldAccept(LogLevel)"/> check that the
/// dispatch site reads on every call. Optionally also wraps a
/// <see cref="CategoryLevelSwitchMap"/> for per-category overrides;
/// when configured, the per-category switch takes precedence over the
/// global one for matching categories.
///
/// <para>
/// <b>Why this exists.</b> Today's
/// <see cref="Configuration.DynamicLevelPolicy"/> disqualifies the
/// kernel fast path because the chain pipeline owns the level-switch
/// machinery downstream. A pipeline whose dynamic-level needs are
/// "global threshold I can change at runtime, optionally with per-
/// subsystem overrides" does not need that. This resolver lives at
/// the dispatch boundary, reads the current level via a single
/// volatile-load + frozen-map lookup, and stays kernel-eligible.
/// </para>
///
/// <para>
/// <b>Per-category cost.</b> When no <see cref="CategoryLevelSwitchMap"/>
/// is configured the fast path is byte-equivalent to the global-only
/// shape — the null-check on the field folds away under inlining and
/// the same two frozen-map lookups run. When a map is configured the
/// path adds one <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}.TryGetValue"/>
/// per accepted call (string-keyed, case-insensitive). Categories with
/// no override hit the same global path; categories with an override
/// read the override's switch instead.
/// </para>
/// </summary>
public sealed class FastPathDynamicLevel
{
    private readonly LogLevelSwitch _switch;
    private readonly CategoryLevelSwitchMap? _categoryMap;
    private readonly FrozenDictionary<string, int> _rankByKey;

    /// <summary>
    /// Build a global-only resolver. No per-category overrides.
    /// </summary>
    public FastPathDynamicLevel(LogLevelSwitch levelSwitch, ILogLevelRegistry registry)
        : this(levelSwitch, categoryMap: null, registry)
    {
    }

    /// <summary>
    /// Build a resolver that consults <paramref name="categoryMap"/>
    /// before falling back to <paramref name="levelSwitch"/>. Pass
    /// <c>null</c> for <paramref name="categoryMap"/> to get the
    /// global-only behaviour.
    /// </summary>
    public FastPathDynamicLevel(
        LogLevelSwitch levelSwitch,
        CategoryLevelSwitchMap? categoryMap,
        ILogLevelRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(levelSwitch);
        ArgumentNullException.ThrowIfNull(registry);
        _switch = levelSwitch;
        _categoryMap = categoryMap;
        _rankByKey = BuildRankMap(registry);
    }

    /// <summary>The global mutable level switch this resolver reads from.</summary>
    public LogLevelSwitch Switch => _switch;

    /// <summary>
    /// The per-category override map, or <c>null</c> when only the
    /// global switch governs acceptance.
    /// </summary>
    public CategoryLevelSwitchMap? CategoryMap => _categoryMap;

    /// <summary>
    /// Global-switch acceptance check — does not consult the category
    /// map even when one is configured. Kept for callers that already
    /// resolved the relevant category elsewhere or that have no
    /// category to supply.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldAccept(LogLevel candidate)
    {
        var minLevel = _switch.MinimumLevel;
        return EvaluateRank(candidate, minLevel);
    }

    /// <summary>
    /// Per-category-aware acceptance check. When the category has an
    /// override in the map, the override's minimum level governs;
    /// otherwise the global switch governs. Inlined so the JIT collapses
    /// the call into the dispatch frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldAccept(LogCategory category, LogLevel candidate)
    {
        ArgumentNullException.ThrowIfNull(category);

        // Hot-path branching: when the field is null the JIT folds the
        // entire branch out (the field is readonly + set at construction).
        var map = _categoryMap;
        var effectiveSwitch = (map is null)
            ? _switch
            : map.GetOrDefault(category.Value, _switch);

        var minLevel = effectiveSwitch.MinimumLevel;
        return EvaluateRank(candidate, minLevel);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EvaluateRank(LogLevel candidate, LogLevel minLevel)
    {
        if (!_rankByKey.TryGetValue(candidate.Key, out var candidateRank)) return false;
        if (!_rankByKey.TryGetValue(minLevel.Key, out var minRank)) return false;
        return candidateRank >= minRank;
    }

    private static FrozenDictionary<string, int> BuildRankMap(ILogLevelRegistry registry)
    {
        var registered = registry.GetRegisteredLevels();
        var map = new Dictionary<string, int>(registered.Count, StringComparer.Ordinal);
        foreach (var entry in registered)
        {
            map[entry.Level.Key] = entry.Rank;
        }
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
