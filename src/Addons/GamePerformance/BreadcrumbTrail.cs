#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;

namespace MMP.Herald.Addons.GamePerformance;

/// <summary>
/// Records a trail of significant game moments (breadcrumbs) for crash investigation.
/// Distinct from FlightRecorderLogger (which captures all below-threshold log events).
/// Breadcrumbs are intentional markers: scene transitions, save points, boss encounters,
/// state changes - the "interesting moments" trail attached to error reports.
///
/// Inspired by Sentry's breadcrumb model.
///
/// Usage:
///   var trail = new BreadcrumbTrail(maxBreadcrumbs: 50);
///   trail.Add("navigation", "Scene loaded: BossArena");
///   trail.Add("gameplay", "Boss fight started", BreadcrumbLevel.Info);
///   trail.Add("save", "Autosave completed");
///
///   // On crash/error, attach trail to report:
///   var breadcrumbs = trail.GetTrail();
/// </summary>
public sealed class BreadcrumbTrail
{
    private readonly int _maxBreadcrumbs;
    private readonly LinkedList<Breadcrumb> _breadcrumbs = new();
    private readonly object _sync = new();

    public BreadcrumbTrail(int maxBreadcrumbs = 50) {
        if (maxBreadcrumbs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBreadcrumbs), "Must be positive.");
        }

        _maxBreadcrumbs = maxBreadcrumbs;
    }

    /// <summary>
    /// Record a breadcrumb. Thread-safe.
    /// </summary>
    public void Add(
        string category,
        string message,
        BreadcrumbLevel level = BreadcrumbLevel.Info,
        IReadOnlyDictionary<string, object?>? data = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var breadcrumb = new Breadcrumb(
            DateTimeOffset.UtcNow,
            category,
            message,
            level,
            data);

        lock (_sync)
        {
            _breadcrumbs.AddLast(breadcrumb);

            while (_breadcrumbs.Count > _maxBreadcrumbs)
            {
                _breadcrumbs.RemoveFirst();
            }
        }
    }

    /// <summary>
    /// Get the current breadcrumb trail (oldest first). Thread-safe snapshot.
    /// </summary>
    public IReadOnlyList<Breadcrumb> GetTrail() {
        lock (_sync)
        {
            return new List<Breadcrumb>(_breadcrumbs);
        }
    }

    /// <summary>
    /// Clear all breadcrumbs. Call on session reset.
    /// </summary>
    public void Clear() {
        lock (_sync)
        {
            _breadcrumbs.Clear();
        }
    }

    /// <summary>
    /// Current number of breadcrumbs in the trail.
    /// </summary>
    public int Count {
        get { lock (_sync) { return _breadcrumbs.Count; } }
    }
}

/// <summary>
/// A single breadcrumb - a recorded moment of significance.
/// </summary>
public sealed record Breadcrumb(
    DateTimeOffset Timestamp,
    string Category,
    string Message,
    BreadcrumbLevel Level = BreadcrumbLevel.Info,
    IReadOnlyDictionary<string, object?>? Data = null);

/// <summary>
/// Severity of a breadcrumb.
/// </summary>
public enum BreadcrumbLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}
