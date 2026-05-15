#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Quick;
using MMP.Herald.Templating;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Captures log events for live streaming over HTTP (SSE).
/// Register as a bridge sink to receive all pipeline events.
///
/// Usage:
///   var capture = new LiveLogCapture();
///   capture.StyleProvider = () => api.GetLevelStyles();
///   builder.WithBridge(capture);
///   var result = builder.Build();
///
/// <para>
/// AOT-safe: the per-event broadcast serialization runs through the
/// source-generated <see cref="LiveLogCaptureJsonContext"/> declared at
/// the bottom of this file. The prior <c>[RequiresUnreferencedCode]</c>
/// / <c>[RequiresDynamicCode]</c> annotations on the class dropped with
/// P2 because the reflection path is gone.
/// </para>
/// </summary>
public sealed class LiveLogCapture : ILogger, IKernelSink
{
    // Source-generated context bound to a JsonSerializerOptions instance
    // that carries the prior CaptureJsonOptions shape (camelCase naming,
    // drop nulls). The generator emits serializers for LiveLogEntry and
    // every type it reaches through the property graph.
    private static readonly LiveLogCaptureJsonContext CaptureJsonContext = new(
        new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

    private readonly ConcurrentQueue<LiveLogEntry> _buffer = new();
    private readonly ConcurrentDictionary<int, LiveLogSubscriber> _subscribers = new();
    private readonly int _maxBufferSize;
    private readonly int _maxSubscribers;
    private long _nextId;
    private int _nextSubscriberId;

    // Cached styles - refreshed periodically, not on every event
    private IReadOnlyList<LevelStyleInfo>? _cachedStyles;
    private long _stylesCacheExpiry;
    private IReadOnlyList<CategoryStyleInfo>? _cachedCategoryStyles;
    private long _categoryStylesCacheExpiry;
    private const long StylesCacheTicks = 20_000_000; // 2 seconds in ticks

    /// <summary>
    /// Optional delegate that returns the current level styles.
    /// When set, live log fragments use configured colors instead of hardcoded defaults.
    /// Cached for 2 seconds to avoid per-event overhead.
    /// </summary>
    public Func<IReadOnlyList<LevelStyleInfo>>? StyleProvider { get; set; }

    /// <summary>
    /// Optional delegate that returns the current category styles. When set,
    /// the category fragment on each live-log entry picks up the configured
    /// color, boldness, italic, and background for its matching category name
    /// instead of the hardcoded purple default. The lookup is
    /// case-insensitive and scoped to whatever the pipeline's owning
    /// management API exposes via <c>GetCategoryStyles()</c> — there is no
    /// process-global category-style registry, so two tenants on the same
    /// host can style the same category name differently.
    /// Cached for 2 seconds to avoid per-event overhead.
    /// </summary>
    public Func<IReadOnlyList<CategoryStyleInfo>>? CategoryStyleProvider { get; set; }

    public LiveLogCapture(int maxBufferSize = 1000, int maxSubscribers = 50)
    {
        _maxBufferSize = maxBufferSize;
        _maxSubscribers = maxSubscribers > 0 ? maxSubscribers : throw new ArgumentOutOfRangeException(nameof(maxSubscribers), "Max subscribers must be positive.");

        // Mirror filter-side rejections through the broadcaster into
        // the same buffer + SSE fanout. Rejected entries carry the
        // Rejected flag + RejectionReason so the dashboard can dim
        // them; the LogEvent payload is identical to what an accepted
        // entry would carry. The handler is a static method to avoid
        // capturing `this` in a closure that would prevent collection
        // during process shutdown — instead we use a lambda over `this`
        // and unsubscribe on disposal would be ideal, but LiveLogCapture
        // is process-singleton so the subscription lives for the whole
        // run.
        RejectedEventBroadcaster.OnRejected += LogRejected;
    }

    public void Log(LogEvent logEvent)
    {
        BroadcastEntry(logEvent, rejected: false, rejectionReason: null);
    }

    // Kernel-path entry. SSE fragments and the query-endpoint cache both
    // read LogEvent.Message; materialise + render at the boundary.
    public void Log(in LogEventBuffer buffer) =>
        Log(KernelBufferAdapter.MaterializeAndRender(in buffer));

    /// <summary>
    /// Capture an event the pipeline rejected before reaching a sink.
    /// Same buffer + SSE fanout as <see cref="Log"/>; the resulting
    /// <see cref="LiveLogEntry"/> carries
    /// <see cref="LiveLogEntry.Rejected"/> = true plus the reason
    /// string from <see cref="DropReasons"/> so the dashboard can
    /// render it dimmed with a reason badge.
    /// </summary>
    public void LogRejected(LogEvent logEvent, string reason)
    {
        BroadcastEntry(logEvent, rejected: true, rejectionReason: reason);
    }

    private void BroadcastEntry(LogEvent logEvent, bool rejected, string? rejectionReason)
    {
        var id = Interlocked.Increment(ref _nextId);
        var styles = GetCachedStyles();
        var categoryStyles = GetCachedCategoryStyles();
        var entry = LiveLogEntry.FromLogEvent(id, logEvent, styles, categoryStyles, rejected, rejectionReason);

        _buffer.Enqueue(entry);
        while (_buffer.Count > _maxBufferSize)
            _buffer.TryDequeue(out _);

        // Only serialize and broadcast if there are subscribers
        if (!_subscribers.IsEmpty)
        {
            var json = JsonSerializer.Serialize(entry, CaptureJsonContext.LiveLogEntry);
            foreach (var kvp in _subscribers)
            {
                kvp.Value.TryEnqueue(json);
            }
        }
    }

    private IReadOnlyList<LevelStyleInfo>? GetCachedStyles()
    {
        var provider = StyleProvider;
        if (provider is null) return null;

        var now = Environment.TickCount64;
        if (_cachedStyles is not null && now < _stylesCacheExpiry)
            return _cachedStyles;

        _cachedStyles = provider();
        _stylesCacheExpiry = now + StylesCacheTicks / 10_000; // Convert ticks to ms
        return _cachedStyles;
    }

    private IReadOnlyList<CategoryStyleInfo>? GetCachedCategoryStyles()
    {
        var provider = CategoryStyleProvider;
        if (provider is null) return null;

        var now = Environment.TickCount64;
        if (_cachedCategoryStyles is not null && now < _categoryStylesCacheExpiry)
            return _cachedCategoryStyles;

        _cachedCategoryStyles = provider();
        _categoryStylesCacheExpiry = now + StylesCacheTicks / 10_000;
        return _cachedCategoryStyles;
    }

    public IReadOnlyList<LiveLogEntry> GetRecent(int count = 200)
    {
        var items = _buffer.ToArray();
        var start = Math.Max(0, items.Length - count);
        var result = new LiveLogEntry[items.Length - start];
        Array.Copy(items, start, result, 0, result.Length);
        return result;
    }

    /// <summary>
    /// Return at most <paramref name="count"/> recent entries whose raw event
    /// satisfies <paramref name="predicate"/>. Entries captured before the
    /// Source field was added (should not happen at runtime, but possible in
    /// tests) are skipped. The filter is evaluated in buffer order so the
    /// newest matching entries win when the buffer is larger than count.
    /// </summary>
    public IReadOnlyList<LiveLogEntry> SearchRecent(Func<LogEvent, bool> predicate, int count = 200)
    {
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));
        if (count <= 0) return Array.Empty<LiveLogEntry>();

        var items = _buffer.ToArray();
        var hits = new List<LiveLogEntry>(Math.Min(count, items.Length));

        // Walk newest-first so we stop early once `count` matches are found.
        for (var i = items.Length - 1; i >= 0 && hits.Count < count; i--)
        {
            var entry = items[i];
            if (entry.Source is null) continue;
            if (predicate(entry.Source)) hits.Add(entry);
        }

        // Reverse so callers see oldest-to-newest order like GetRecent.
        hits.Reverse();
        return hits;
    }

    public LiveLogSubscriber Subscribe()
    {
        if (_subscribers.Count >= _maxSubscribers)
            throw new InvalidOperationException($"Maximum subscriber limit ({_maxSubscribers}) reached. Dispose an existing subscriber first.");

        var subId = Interlocked.Increment(ref _nextSubscriberId);
        var sub = new LiveLogSubscriber(subId, this);

        if (!_subscribers.TryAdd(subId, sub))
            throw new InvalidOperationException("Failed to register subscriber.");

        // Double-check: if we raced past the count check, remove and reject
        if (_subscribers.Count > _maxSubscribers)
        {
            _subscribers.TryRemove(subId, out _);
            sub.Dispose();
            throw new InvalidOperationException($"Maximum subscriber limit ({_maxSubscribers}) reached.");
        }

        return sub;
    }

    internal void RemoveSubscriber(int id) => _subscribers.TryRemove(id, out _);

    /// <summary>
    /// Drain all subscriber channels immediately. Completes each channel writer
    /// so SSE endpoints exit their read loops without processing the backlog.
    /// New events after drain are silently dropped until subscribers reconnect.
    /// </summary>
    public void DrainSubscribers()
    {
        foreach (var kvp in _subscribers)
        {
            kvp.Value.Drain();
        }
    }
}

/// <summary>
/// Represents a single subscriber to the live log stream.
/// Backed by a bounded channel that drops oldest entries under pressure.
/// </summary>
public sealed class LiveLogSubscriber : IDisposable
{
    private Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly int _id;
    private readonly LiveLogCapture _owner;

    internal LiveLogSubscriber(int id, LiveLogCapture owner)
    {
        _id = id;
        _owner = owner;
    }

    public ChannelReader<string> Reader => _channel.Reader;

    internal void TryEnqueue(string json) => _channel.Writer.TryWrite(json);

    /// <summary>
    /// Complete the channel writer and replace with a fresh channel.
    /// The SSE endpoint's ReadAsync will throw ChannelClosedException,
    /// causing it to exit cleanly. New events go to the fresh channel.
    /// </summary>
    internal void Drain()
    {
        _channel.Writer.TryComplete();
        _channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _owner.RemoveSubscriber(_id);
    }
}

/// <summary>
/// A single log line captured from the pipeline.
/// Contains styled fragments that can be rendered as colored HTML spans.
/// </summary>
public sealed class LiveLogEntry
{
    public long Id { get; init; }
    public string LevelKey { get; init; } = "";
    public string? PipelineName { get; init; }
    public string? Category { get; init; }
    public List<string> PropertyNames { get; init; } = new();

    /// <summary>
    /// Rendered string values keyed by property name. Populated alongside
    /// <see cref="PropertyNames"/> so the Dashboard can promote one named
    /// property to a top-level column (game-dev "Frame", scientific
    /// computing "MpiRank", etc.) without re-parsing the styled fragments.
    /// Silent properties are excluded — they don't appear in the rendered
    /// output and shouldn't appear in the lookup map either.
    /// </summary>
    public Dictionary<string, string> PropertyValues { get; init; } = new();

    public List<LiveLogFragment> Fragments { get; init; } = new();

    /// <summary>
    /// True when this entry represents an event the pipeline rejected
    /// before it reached a sink (a level filter dropped it, a sampling
    /// gate skipped it, etc.). The dashboard renders rejected entries
    /// dimmed alongside accepted ones so the operator can see the
    /// full simulator output and which decisions the pipeline made.
    /// Default false for backward compatibility with consumers that
    /// only expected accepted events.
    /// </summary>
    public bool Rejected { get; init; }

    /// <summary>
    /// When <see cref="Rejected"/> is true, the canonical drop-reason
    /// string from <see cref="MMP.Herald.Metrics.DropReasons"/>
    /// (level / sampling / throttling / predicate / filter /
    /// redaction). Null on accepted entries.
    /// </summary>
    public string? RejectionReason { get; init; }

    /// <summary>
    /// The raw source event used to build this entry. Excluded from JSON so
    /// stream and recent-log responses are unchanged; kept in memory so the
    /// query endpoint can evaluate a <see cref="MMP.Herald.Events.LogEvent"/>
    /// without re-parsing the rendered fragments.
    /// </summary>
    [JsonIgnore]
    internal LogEvent? Source { get; init; }

    /// <summary>
    /// Convert a LogEvent to styled fragments.
    /// Uses configured level styles when available, falling back to hardcoded defaults.
    /// </summary>
    public static LiveLogEntry FromLogEvent(long id, LogEvent logEvent,
        IReadOnlyList<LevelStyleInfo>? styles = null,
        IReadOnlyList<CategoryStyleInfo>? categoryStyles = null,
        bool rejected = false,
        string? rejectionReason = null)
    {
        var levelKey = logEvent.Level.Key;

        // Try to find a configured style for this level
        LevelStyleInfo? style = null;
        if (styles is not null)
        {
            foreach (var s in styles)
            {
                if (string.Equals(s.LevelKey, levelKey, StringComparison.OrdinalIgnoreCase))
                {
                    style = s;
                    break;
                }
            }
        }

        // Same lookup shape for the category style. A null / missing entry
        // falls back to the hardcoded purple category colour.
        var categoryName = logEvent.Category.Value;
        CategoryStyleInfo? categoryStyle = null;
        if (categoryStyles is not null)
        {
            foreach (var c in categoryStyles)
            {
                if (string.Equals(c.CategoryName, categoryName, StringComparison.OrdinalIgnoreCase))
                {
                    categoryStyle = c;
                    break;
                }
            }
        }

        // Use configured style or fall back to hardcoded defaults
        var levelColor = style?.ColorName ?? DefaultLevelColor(levelKey);
        var levelBg = style?.BackgroundColorName ?? DefaultLevelBackground(levelKey);
        var levelBold = style?.UseBold ?? DefaultLevelBold(levelKey);
        var levelItalic = style?.UseItalic ?? false;

        var fragments = new List<LiveLogFragment>(12); // pre-size for typical entry

        // Timestamp
        fragments.Add(new LiveLogFragment
        {
            Text = logEvent.TimeUtc.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            Color = "gray"
        });

        fragments.Add(new LiveLogFragment { Text = " " });

        // Level badge
        fragments.Add(new LiveLogFragment
        {
            Text = $"[{logEvent.Level.DisplayName}]",
            Color = levelColor,
            Bg = levelBg,
            Bold = levelBold ? true : null,
            Italic = levelItalic ? true : null
        });

        fragments.Add(new LiveLogFragment { Text = " " });

        // Category — honours the configured category style when one is
        // registered for this category name, otherwise falls back to the
        // historical purple. Background, bold, and italic also respect the
        // configured style so the Dashboard can render "Combat" in red-bold
        // and "Audio" in muted blue-italic without touching the renderer.
        fragments.Add(new LiveLogFragment
        {
            Text = categoryName,
            Color = categoryStyle?.ColorName ?? "purple",
            Bg = categoryStyle?.BackgroundColorName,
            Bold = (categoryStyle?.UseBold ?? false) ? true : null,
            Italic = (categoryStyle?.UseItalic ?? false) ? true : null
        });

        fragments.Add(new LiveLogFragment
        {
            Text = " - ",
            Color = "gray"
        });

        // Message - use the level's background color as text highlight for warn/error,
        // falling back to the foreground color so it stays readable on dark backgrounds
        fragments.Add(new LiveLogFragment
        {
            Text = logEvent.Message,
            Color = levelKey switch
            {
                "error" or "critical" => levelBg ?? levelColor,
                "warn" => levelBg ?? "yellow",
                _ => null
            }
        });

        // Properties
        foreach (var prop in logEvent.Properties)
        {
            if (prop.IsSilent) continue;
            fragments.Add(new LiveLogFragment { Text = " " });
            fragments.Add(new LiveLogFragment
            {
                Text = prop.Name,
                Color = "cyan"
            });
            fragments.Add(new LiveLogFragment
            {
                Text = "=",
                Color = "gray"
            });
            fragments.Add(new LiveLogFragment
            {
                Text = prop.ResolvedValue?.ToString() ?? "null",
                Color = "light_gray"
            });
        }

        // Exception
        if (logEvent.CausedBy is not null)
        {
            fragments.Add(new LiveLogFragment
            {
                Text = "\n  " + logEvent.CausedBy,
                Color = "red"
            });
        }

        // Extract pipeline identity from context (set by ServiceIdentityEnricher)
        string? pipelineName = null;
        if (logEvent.Context.TryGetValue("service.name", out var svcName) && svcName is string svc)
            pipelineName = svc;

        var propertyNames = new List<string>();
        var propertyValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in logEvent.Properties)
        {
            if (prop.IsSilent) continue;
            propertyNames.Add(prop.Name);
            // Last write wins on duplicate names — same precedence the
            // template renderer uses, so what the operator sees in the
            // fragment column matches what they get from the lookup map.
            propertyValues[prop.Name] = prop.Value?.ToString() ?? string.Empty;
        }

        return new LiveLogEntry
        {
            Id = id,
            LevelKey = levelKey,
            PipelineName = pipelineName,
            Category = logEvent.Category.Value,
            PropertyNames = propertyNames,
            PropertyValues = propertyValues,
            Fragments = fragments,
            Rejected = rejected,
            RejectionReason = rejectionReason,
            Source = logEvent
        };
    }

    // Cognitive Complexity note: each case is an independent color lookup - no nesting.
    private static string DefaultLevelColor(string levelKey)
    {
        return levelKey switch
        {
            "trace" => "gray",
            "debug" => "cyan",
            "info" => "green",
            "notice" => "light_blue",
            "success" => "green",
            "warn" => "black",
            "error" => "black",
            "critical" => "crimson",
            "security" => "magenta",
            _ => "white"
        };
    }

    private static string? DefaultLevelBackground(string levelKey)
    {
        return levelKey switch
        {
            "success" => "green",
            "warn" => "yellow",
            "error" => "red",
            "critical" => "crimson",
            "security" => "magenta",
            _ => null
        };
    }

    private static bool DefaultLevelBold(string levelKey)
    {
        return levelKey switch
        {
            "notice" or "success" or "warn" or "error" or "critical" or "security" => true,
            _ => false
        };
    }
}

/// <summary>
/// A styled text fragment within a log entry.
/// </summary>
public sealed class LiveLogFragment
{
    public string Text { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Color { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Bold { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Italic { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Bg { get; init; }
}

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the SSE
/// broadcast payload. Bound to <see cref="LiveLogEntry"/> and every type
/// the generator reaches through the property graph
/// (<see cref="LiveLogFragment"/>, <see cref="System.Collections.Generic.List{T}"/>
/// of string / LiveLogFragment, primitives). AOT-safe — no reflection
/// required at runtime.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LiveLogEntry))]
internal partial class LiveLogCaptureJsonContext : JsonSerializerContext
{
}
