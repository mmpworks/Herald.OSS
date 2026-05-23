#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MMP.Herald.Quick;

/// <summary>
/// Standalone builder for the per-pipeline BOOTSTRAP JSON shape that the
/// Herald.RestApi.FakeServer / Herald.ServerOSS expects on disk
/// (<c>data/pipelines/{tenant}/{pipeline}.json</c>) and that the Dashboard
/// Pipeline JSON tab renders under <c>bootstrap.config</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a standalone builder and not a partial on QuickLogBuilder:</b> the
/// QuickLogBuilder owns the C# Logger pipeline (sinks, enrichers, channels,
/// templates) — its surface is large and the bootstrap-shape emission is a
/// distinct concern. Keeping the two concerns in separate types preserves
/// CUPID-Composable and keeps QuickLogBuilder's cognitive complexity flat. A
/// caller that wants both can construct each independently and combine their
/// outputs at the call site.
/// </para>
/// <para>
/// <b>NAME-bound throughout:</b> per the
/// project-pipeline-json-bootstrap-chrome-split memory rule, every entry in
/// <see cref="Levels"/>, <see cref="Categories"/>, <see cref="Enrichers"/>,
/// and <see cref="MinimumLevel"/> is a verbatim operator or QuickLogBuilder
/// string, not a slug or priority number. The server resolves NAMES to
/// indices at hydration; this builder never thinks about indices. The
/// flight-recorder level NAME lives on the loggers-array entry (see
/// <see cref="WithFlightRecorder"/>); same NAME-bound rule applies.
/// </para>
/// <para>
/// <b>Sinks, buffer, filters as opaque JSON:</b> these shapes are owned by
/// Herald.OSS / Herald.Pro and may evolve. The bootstrap builder accepts them
/// as <see cref="JsonElement"/> and round-trips them as-is so the emitted
/// JSON matches whatever the consuming server expects, without this builder
/// needing to track schema drift.
/// </para>
/// </remarks>
public sealed class PipelineBootstrapBuilder
{
    private string _name = "default";
    private JsonElement? _buffer;
    private JsonElement? _sinks;
    private JsonElement? _filters;
    private readonly List<string> _levels = new();
    private string? _minimumLevel;
    // Per project_flight_recorder_becomes_logger: the flight-recorder now lives
    // as a typed entry in the loggers array, not as a pair of root fields. The
    // builder tracks one nullable record; ExportJson emits the loggers array
    // when this is non-null and omits the array entirely otherwise. The SPA's
    // FR-button visibility rule reads the presence of this entry.
    private FlightRecorderLoggerConfig? _flightRecorder;
    private readonly List<string> _categories = new();
    private readonly List<string> _enrichers = new();

    /// <summary>Creates a bootstrap builder for the named pipeline.</summary>
    public PipelineBootstrapBuilder(string pipelineName = "default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineName);
        _name = pipelineName;
    }

    /// <summary>Sets the pipeline NAME (default <c>"default"</c>).</summary>
    public PipelineBootstrapBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// Sets the buffer config as opaque JSON. Pass a parsed
    /// <see cref="JsonElement"/> (e.g. <c>JsonDocument.Parse("{\"kind\":\"memory\",\"capacity\":1024}").RootElement</c>).
    /// Null clears the field.
    /// </summary>
    public PipelineBootstrapBuilder WithBuffer(JsonElement? buffer)
    {
        _buffer = buffer;
        return this;
    }

    /// <summary>
    /// Sets the sink list as opaque JSON. Pass a parsed JSON array.
    /// Null clears the field.
    /// </summary>
    public PipelineBootstrapBuilder WithSinks(JsonElement? sinks)
    {
        _sinks = sinks;
        return this;
    }

    /// <summary>Sets the filter list as opaque JSON.</summary>
    public PipelineBootstrapBuilder WithFilters(JsonElement? filters)
    {
        _filters = filters;
        return this;
    }

    /// <summary>
    /// Sets the ordered level NAME list. Index 0 = highest priority (wire
    /// priority 1). Replaces any prior level list. Names are stored verbatim.
    /// </summary>
    public PipelineBootstrapBuilder WithLevels(params string[] levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        _levels.Clear();
        foreach (var l in levels)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(l);
            _levels.Add(l);
        }
        return this;
    }

    /// <summary>
    /// Sets the minimum-visible-level NAME pointer. Must reference a level in
    /// <see cref="WithLevels(string[])"/>; this builder does not verify the
    /// reference (the server's hydrator does), so a typo here surfaces as a
    /// hydrator warning at boot.
    /// </summary>
    public PipelineBootstrapBuilder WithMinimumLevel(string levelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(levelName);
        _minimumLevel = levelName;
        return this;
    }

    /// <summary>
    /// Adds (or replaces) the flight-recorder entry in the loggers array. Per
    /// the project_flight_recorder_becomes_logger memory rule, the FR lives as
    /// a logger pipeline step rather than two bootstrap-root fields. The SPA
    /// gates its FR-button visibility on the presence of this entry — calling
    /// <see cref="WithoutFlightRecorder"/> removes the entry and hides the
    /// button; calling this method with <paramref name="enabled"/> = false
    /// keeps the button visible but disarmed and preserves the operator's
    /// level pick across toggles.
    /// </summary>
    /// <param name="levelName">
    /// The level NAME below which events are buffered. Same NAME-bound
    /// contract as <see cref="WithMinimumLevel"/>; a typo surfaces as a
    /// hydrator warning at boot.
    /// </param>
    /// <param name="enabled">Armed state. Defaults to false (button visible, toggle off).</param>
    /// <param name="bufferSize">
    /// Ring-buffer capacity. Null keeps the canonical 200-event default;
    /// other values require Pro edition (the configurability gate runs on the
    /// Core side, not in this builder).
    /// </param>
    /// <param name="triggerLevel">
    /// Level NAME at-or-above which the buffer drains. Null keeps the
    /// canonical "Error" default; other values require Pro edition.
    /// </param>
    public PipelineBootstrapBuilder WithFlightRecorder(
        string levelName,
        bool enabled = false,
        int? bufferSize = null,
        string? triggerLevel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(levelName);
        _flightRecorder = new FlightRecorderLoggerConfig
        {
            Name = "fr",
            MinimumLevel = levelName,
            Enabled = enabled,
            BufferSize = bufferSize,
            TriggerLevel = triggerLevel,
        };
        return this;
    }

    /// <summary>
    /// Removes the flight-recorder entry from the loggers array. After this
    /// call the emitted bootstrap has no flight-recorder logger and the SPA
    /// hides the FR button entirely.
    /// </summary>
    public PipelineBootstrapBuilder WithoutFlightRecorder()
    {
        _flightRecorder = null;
        return this;
    }

    /// <summary>Sets the ordered category NAME list. Replaces any prior list.</summary>
    public PipelineBootstrapBuilder WithCategories(params string[] categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        _categories.Clear();
        foreach (var c in categories)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(c);
            _categories.Add(c);
        }
        return this;
    }

    /// <summary>Sets the ordered enricher NAME list. Replaces any prior list.</summary>
    public PipelineBootstrapBuilder WithEnrichers(params string[] enrichers)
    {
        ArgumentNullException.ThrowIfNull(enrichers);
        _enrichers.Clear();
        foreach (var e in enrichers)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(e);
            _enrichers.Add(e);
        }
        return this;
    }

    /// <summary>Read-only view of the current level NAMES (in order).</summary>
    public IReadOnlyList<string> Levels => _levels.AsReadOnly();

    /// <summary>Read-only view of the current category NAMES (in order).</summary>
    public IReadOnlyList<string> Categories => _categories.AsReadOnly();

    /// <summary>Read-only view of the current enricher NAMES (in order).</summary>
    public IReadOnlyList<string> Enrichers => _enrichers.AsReadOnly();

    /// <summary>The current minimumLevel NAME pointer (null when unset).</summary>
    public string? MinimumLevel => _minimumLevel;

    /// <summary>
    /// The current flight-recorder level NAME (null when no flight-recorder is
    /// configured). Convenience accessor that projects the flight-recorder
    /// logger entry's <see cref="FlightRecorderLoggerConfig.MinimumLevel"/>.
    /// </summary>
    public string? FlightRecorder => _flightRecorder?.MinimumLevel;

    /// <summary>True when a flight-recorder logger entry is configured.</summary>
    public bool HasFlightRecorder => _flightRecorder is not null;

    /// <summary>
    /// Serializes the bootstrap to the canonical JSON shape the server consumes.
    /// Property order is fixed: <c>name</c>, <c>buffer</c>, <c>sinks</c>,
    /// <c>filters</c>, <c>levels</c>, <c>minimumLevel</c>, <c>categories</c>,
    /// <c>enrichers</c>, <c>loggers</c>. This matches
    /// <c>DemoSeeder.DefaultPipelineConfigJson</c> exactly so a
    /// QuickLogBuilder-equivalent caller and the FakeServer's first-boot seed
    /// produce byte-identical bootstrap documents under the same inputs.
    /// </summary>
    /// <param name="indented">
    /// When true, produces a pretty-printed document (matches the FakeServer's
    /// hand-edit-friendly default). When false, single-line for benchmarks /
    /// network payloads.
    /// </param>
    public string ExportJson(bool indented = true)
    {
        var loggers = _flightRecorder is null
            ? Array.Empty<LoggerEntryDocument>()
            : new[] { LoggerEntryDocument.FromFlightRecorder(_flightRecorder) };

        var document = new BootstrapDocument
        {
            Name = _name,
            Buffer = _buffer,
            Sinks = _sinks,
            Filters = _filters,
            Levels = _levels.ToArray(),
            MinimumLevel = _minimumLevel,
            Categories = _categories.ToArray(),
            Enrichers = _enrichers.ToArray(),
            Loggers = loggers,
        };
        return JsonSerializer.Serialize(document, indented ? IndentedOptions : CompactOptions);
    }

    // Serialization options. Indented mirrors the FakeServer's hand-edit-friendly
    // shape; both options honor camelCase + ignore null so optional pointers
    // (minimumLevel) drop out of the document cleanly.
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Internal property-ordered shape for the bootstrap JSON. The property
    /// declaration order in C# 12 governs the System.Text.Json output order
    /// when no <see cref="JsonPropertyOrderAttribute"/> is applied, so the
    /// order below IS the on-wire order — keep it stable.
    /// </summary>
    private sealed record BootstrapDocument
    {
        public string Name { get; init; } = string.Empty;
        public JsonElement? Buffer { get; init; }
        public JsonElement? Sinks { get; init; }
        public JsonElement? Filters { get; init; }
        public IReadOnlyList<string> Levels { get; init; } = Array.Empty<string>();
        public string? MinimumLevel { get; init; }
        public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Enrichers { get; init; } = Array.Empty<string>();
        public IReadOnlyList<LoggerEntryDocument> Loggers { get; init; } = Array.Empty<LoggerEntryDocument>();
    }

    /// <summary>
    /// On-wire shape for one logger entry. Field order matches the FakeServer's
    /// <c>LoggerEntry</c> record so a QuickLogBuilder-emitted bootstrap and a
    /// FakeServer-persisted bootstrap stay byte-equivalent for the same inputs.
    /// </summary>
    private sealed record LoggerEntryDocument
    {
        public string Kind { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? MinimumLevel { get; init; }
        public int? BufferSize { get; init; }
        public string? TriggerLevel { get; init; }
        public bool Enabled { get; init; }

        public static LoggerEntryDocument FromFlightRecorder(FlightRecorderLoggerConfig config) =>
            new()
            {
                Kind = "flightRecorder",
                Name = config.Name,
                MinimumLevel = config.MinimumLevel,
                BufferSize = config.BufferSize,
                TriggerLevel = config.TriggerLevel,
                Enabled = config.Enabled,
            };
    }
}

/// <summary>
/// Builder-internal configuration record for the flight-recorder logger entry.
/// Public so the matching FakeServer-side <c>LoggerEntry</c> can be constructed
/// from the same field set without round-tripping through JSON in unit tests.
/// </summary>
public sealed record FlightRecorderLoggerConfig
{
    /// <summary>Operator-friendly id for the logger instance. Defaults to <c>"fr"</c>.</summary>
    public string Name { get; init; } = "fr";

    /// <summary>Level NAME below which events are buffered. Null = inherit pipeline minimum.</summary>
    public string? MinimumLevel { get; init; }

    /// <summary>Ring-buffer capacity. Null = canonical 200-event default.</summary>
    public int? BufferSize { get; init; }

    /// <summary>Level NAME at-or-above which the buffer drains. Null = canonical "Error" default.</summary>
    public string? TriggerLevel { get; init; }

    /// <summary>Armed state. False = button visible, toggle off.</summary>
    public bool Enabled { get; init; }
}
