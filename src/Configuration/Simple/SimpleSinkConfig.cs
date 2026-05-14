#nullable enable

using System.Text.Json.Serialization;

namespace MMP.Herald.Configuration.Simple;

/// <summary>
/// JSON-friendly sink descriptor for the "simple" pipeline schema shared by
/// Herald.Lean and Herald.Embed. Keeps the keys users actually write in a
/// config file — Kind plus the transport-specific fields — and delegates
/// full schema concerns (rolling, routes, themes) to <see cref="QuickLogBuilder"/>
/// when the caller needs them.
///
/// Supported kinds are documented on <see cref="SimpleConfigFactory"/>.
/// Unknown kinds throw at factory time so misconfigured daemons fail fast.
/// </summary>
public sealed record SimpleSinkConfig
{
    /// <summary>Sink kind discriminator (e.g. "console", "json_file", "http_json").</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    /// <summary>File path for file-backed sinks (text_file, json_file, protobuf_file).</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>Endpoint URL for network sinks (http_json, elasticsearch, slack, webhook, otlp_*).</summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    /// <summary>Host name for TCP sinks.</summary>
    [JsonPropertyName("host")]
    public string? Host { get; init; }

    /// <summary>Port for TCP sinks.</summary>
    [JsonPropertyName("port")]
    public int? Port { get; init; }

    /// <summary>Minimum level for this sink. Falls back to the pipeline minimum when null.</summary>
    [JsonPropertyName("minLevel")]
    public string? MinLevel { get; init; }

    /// <summary>Reserved for future format hints (e.g. structured layout variants).</summary>
    [JsonPropertyName("format")]
    public string? Format { get; init; }
}
