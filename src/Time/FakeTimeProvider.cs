#nullable enable

using System;

namespace MMP.Herald.Time;

/// <summary>
/// Deterministic time provider for testing.
/// Allows complete control over timestamps in log events,
/// enabling reproducible test assertions without real clock dependency.
///
/// Usage:
///   var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));
///
///   var result = QuickLogBuilder.Create()
///       .WithConsoleSink()
///       .WithTimeProvider(time)
///       .Build();
///
///   // Log events now have the controlled timestamp.
///   time.Advance(TimeSpan.FromSeconds(30));
///   // Next events are 30 seconds later.
/// </summary>
public sealed class FakeTimeProvider : IDateTimeProvider
{
    private DateTimeOffset _current;
    private readonly object _lock = new();

    public FakeTimeProvider(DateTimeOffset startTime)
    {
        _current = startTime;
    }

    /// <summary>
    /// Create a fake time provider starting at 2020-01-01T00:00:00Z.
    /// </summary>
    public FakeTimeProvider()
        : this(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    public DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _current;
        }
    }

    /// <summary>
    /// Advance the clock by the given duration.
    /// </summary>
    public void Advance(TimeSpan duration)
    {
        lock (_lock)
        {
            _current = _current.Add(duration);
        }
    }

    /// <summary>
    /// Set the clock to an exact time.
    /// </summary>
    public void SetUtcNow(DateTimeOffset time)
    {
        lock (_lock)
        {
            _current = time;
        }
    }
}
