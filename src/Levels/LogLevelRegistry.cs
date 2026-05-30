#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace MMP.Herald.Levels;
/// <summary>
/// Mutable registry that owns log level ordering.
/// Inserting before or after a known level automatically shifts later ranks.
/// </summary>
public sealed class LogLevelRegistry : ILogLevelRegistry
{
    private readonly List<LogLevel> _levels = new();
    private readonly Dictionary<string, LogLevel> _levelsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _registrationLock = new();

    // Rank cache: maps level key → integer rank for O(1) hot-path lookups.
    // Rebuilt on every Register/Insert call (registration is rare, lookups are per-event).
    // Swapped atomically — readers see a consistent snapshot without locking.
    private volatile Dictionary<string, int> _rankCache = new(StringComparer.OrdinalIgnoreCase);

    public static LogLevelRegistry CreateDefault()
    {
        var registry = new LogLevelRegistry();
        registry.Register(KnownLogLevels.Verbose);
        registry.Register(KnownLogLevels.Debug);
        registry.Register(KnownLogLevels.Information);
        registry.Register(KnownLogLevels.Warning);
        registry.Register(KnownLogLevels.Error);
        return registry;
    }

    public void Register(LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        lock (_registrationLock)
        {
            EnsureLevelCanBeRegistered(level);
            _levels.Add(level);
            _levelsByKey.Add(level.Key, level);
            RebuildRankCache();
        }
    }

    public void RegisterBefore(string existingLevelKey, LogLevel level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existingLevelKey);
        ArgumentNullException.ThrowIfNull(level);
        lock (_registrationLock)
        {
            var existingIndex = FindIndex(existingLevelKey);
            EnsureLevelCanBeRegistered(level);
            InsertAt(existingIndex, level);
        }
    }

    public void RegisterAfter(string existingLevelKey, LogLevel level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existingLevelKey);
        ArgumentNullException.ThrowIfNull(level);
        lock (_registrationLock)
        {
            var existingIndex = FindIndex(existingLevelKey);
            EnsureLevelCanBeRegistered(level);
            InsertAt(existingIndex + 1, level);
        }
    }

    public bool Contains(string levelKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(levelKey);
        return _levelsByKey.ContainsKey(levelKey);
    }

    public LogLevel? GetByKeyOrNull(string levelKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(levelKey);
        // Task 9: alias map removed. Old keys (info/warn/critical/trace) arriving
        // at the wire/registry boundary return null (loud-reject). On-disk config
        // files use the separate S-3 migration shim in LoggingJsonSerializer.
        return _levelsByKey.TryGetValue(levelKey, out var level) ? level : null;
    }

    public bool Contains(LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return _levelsByKey.ContainsKey(level.Key);
    }

    public int GetRank(LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        // Fast path: use rank cache (O(1) dictionary lookup, no allocation)
        if (_rankCache.TryGetValue(level.Key, out var rank))
            return rank;
        // Fallback: linear scan for levels not yet in cache (shouldn't happen)
        return GetRegisteredLevel(level).Rank;
    }

    public RegisteredLogLevel GetRegisteredLevel(LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);

        var index = FindIndex(level.Key);
        return new RegisteredLogLevel(_levels[index], index);
    }

    public IReadOnlyList<RegisteredLogLevel> GetRegisteredLevels()
    {
        var output = new List<RegisteredLogLevel>(_levels.Count);

        for (var index = 0; index < _levels.Count; index += 1)
        {
            output.Add(new RegisteredLogLevel(_levels[index], index));
        }

        return output;
    }

    public bool IsAtOrAbove(LogLevel candidate, LogLevel minimum)
    {
        // Hot path: two dictionary lookups, zero allocation, no linear scan.
        // This is called on every log event for level filtering.
        if (candidate is not null && minimum is not null
            && _rankCache.TryGetValue(candidate.Key, out var candidateRank)
            && _rankCache.TryGetValue(minimum.Key, out var minimumRank))
        {
            return candidateRank >= minimumRank;
        }

        // Cold path: null checks + fallback
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(minimum);
        return GetRank(candidate) >= GetRank(minimum);
    }

    public bool IsBelow(LogLevel candidate, LogLevel minimum)
    {
        if (candidate is not null && minimum is not null
            && _rankCache.TryGetValue(candidate.Key, out var candidateRank)
            && _rankCache.TryGetValue(minimum.Key, out var minimumRank))
        {
            return candidateRank < minimumRank;
        }

        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(minimum);
        return GetRank(candidate) < GetRank(minimum);
    }

    public string DumpLevels()
    {
        var builder = new StringBuilder();

        foreach (var registeredLevel in GetRegisteredLevels())
        {
            builder.Append(registeredLevel.Rank);
            builder.Append(" - ");
            builder.Append(registeredLevel.Level.DisplayName);
            builder.Append(" [");
            builder.Append(registeredLevel.Level.Key);
            builder.Append(']');
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private void EnsureLevelCanBeRegistered(LogLevel level)
    {
        if (string.IsNullOrWhiteSpace(level.Key))
        {
            throw new ArgumentException("Log level key must not be null, empty, or whitespace.", nameof(level));
        }

        if (_levelsByKey.ContainsKey(level.Key))
        {
            throw new InvalidOperationException(
                $"A log level with key '{level.Key}' is already registered.");
        }
    }

    private int FindIndex(string levelKey)
    {
        for (var index = 0; index < _levels.Count; index += 1)
        {
            if (string.Equals(_levels[index].Key, levelKey, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        throw new KeyNotFoundException($"Log level '{levelKey}' was not found in the registry.");
    }

    private void InsertAt(int index, LogLevel level)
    {
        _levels.Insert(index, level);
        _levelsByKey.Add(level.Key, level);
        RebuildRankCache();
    }

    /// <summary>
    /// Rebuild the rank cache after any registration change.
    /// Registration is rare (once at startup); lookups happen on every event.
    /// The cache trades O(1) startup cost for O(1) hot-path performance.
    /// </summary>
    private void RebuildRankCache()
    {
        var cache = new Dictionary<string, int>(_levels.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _levels.Count; i++)
            cache[_levels[i].Key] = i;
        _rankCache = cache;
    }
}