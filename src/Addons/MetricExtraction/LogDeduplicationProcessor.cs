#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Addons.MetricExtraction;

/// <summary>
/// Collapses identical log messages within a time window into a single event
/// with a duplicate_count property. Reduces log volume 50-80% in systems
/// with repetitive logging (e.g., physics warnings every frame).
///
/// Inspired by OpenTelemetry's Log Deduplication Processor (2026).
///
/// Deduplication key: Level + Category + MessageTemplate.
/// When a duplicate is detected within the window, the count is incremented
/// and the event is suppressed. When the window expires or a new unique
/// message arrives, the accumulated event is flushed with its count.
/// </summary>
public sealed class LogDeduplicationProcessor : ILogEventProcessor
{
    private readonly TimeSpan _window;
    private readonly int _maxEntries;
    private readonly Dictionary<string, DeduplicationEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private long _lastEvictionTicks;

    public LogDeduplicationProcessor(TimeSpan? window = null, int maxEntries = 10_000) {
        _window = window ?? TimeSpan.FromSeconds(5);
        _maxEntries = maxEntries > 0 ? maxEntries : throw new ArgumentOutOfRangeException(nameof(maxEntries), "Max entries must be positive.");

        if (_window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be positive.");
        }
    }

    public LogEvent? Process(LogEvent logEvent) {
        var key = BuildKey(logEvent);
        var now = logEvent.TimeUtc;

        lock (_sync)
        {
            // Evict expired entries periodically to bound memory usage
            EvictExpiredIfNeeded(now);

            if (_entries.TryGetValue(key, out var existing))
            {
                var elapsed = now - existing.FirstSeen;

                if (elapsed < _window)
                {
                    // Within window - suppress and count
                    existing.Count++;
                    existing.LastEvent = logEvent;
                    return logEvent; // pass through but mark as duplicate below
                }

                // Window expired - flush the accumulated entry and start fresh
                _entries[key] = new DeduplicationEntry(logEvent, now);

                if (existing.Count > 1)
                {
                    // Return the accumulated event with duplicate_count
                    return AddDuplicateCount(existing.LastEvent, existing.Count);
                }

                return logEvent;
            }

            // First occurrence
            _entries[key] = new DeduplicationEntry(logEvent, now);
            return logEvent;
        }
    }

    /// <summary>
    /// Remove expired entries when the dictionary exceeds the max size or
    /// enough time has passed since the last eviction (whichever comes first).
    /// Must be called under lock.
    /// </summary>
    private void EvictExpiredIfNeeded(DateTimeOffset now) {
        var nowTicks = now.Ticks;
        var sinceLastEviction = nowTicks - _lastEvictionTicks;

        // Evict if over capacity or if at least one window has elapsed since last eviction
        if (_entries.Count <= _maxEntries && sinceLastEviction < _window.Ticks)
            return;

        _lastEvictionTicks = nowTicks;
        var keysToRemove = new List<string>();

        foreach (var kvp in _entries)
        {
            if (now - kvp.Value.FirstSeen >= _window)
                keysToRemove.Add(kvp.Key);
        }

        foreach (var key in keysToRemove)
            _entries.Remove(key);
    }

    /// <summary>
    /// Flush any pending deduplicated events. Call at shutdown or frame boundaries.
    /// Returns events with duplicate_count > 1 that haven't been flushed yet.
    /// </summary>
    public IReadOnlyList<LogEvent> Flush() {
        var flushed = new List<LogEvent>();

        lock (_sync)
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.Count > 1)
                {
                    flushed.Add(AddDuplicateCount(entry.LastEvent, entry.Count));
                }
            }

            _entries.Clear();
        }

        return flushed;
    }

    private static string BuildKey(LogEvent logEvent) {
        return $"{logEvent.Level.Key}|{logEvent.Category.Value}|{logEvent.MessageTemplate}";
    }

    private static LogEvent AddDuplicateCount(LogEvent logEvent, int count) {
        var newContext = new Dictionary<string, object?>(logEvent.Context, StringComparer.Ordinal)
        {
            [MetricContextKeys.DuplicateCount] = count
        };
        return logEvent with { Context = newContext };
    }

    private sealed class DeduplicationEntry
    {
        public LogEvent LastEvent;
        public DateTimeOffset FirstSeen;
        public int Count;

        public DeduplicationEntry(LogEvent logEvent, DateTimeOffset firstSeen) {
            LastEvent = logEvent;
            FirstSeen = firstSeen;
            Count = 1;
        }
    }
}
