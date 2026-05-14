#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Addons.Archive;

/// <summary>
/// Operator-facing description of a streaming archive destination. Paired
/// with <see cref="IStreamingArchiveProvider"/> at pipeline build time,
/// dispatched by <see cref="Kind"/> against the streaming-provider
/// registry.
///
/// <para>
/// Related to <see cref="SinkArchivePolicy"/> (the closed-file archive
/// path) but intentionally separate. Streaming and closed-file archives
/// have different lifecycles (per-session vs. per-rotation) and different
/// failure semantics (best-effort backup vs. verified upload), so merging
/// them into one policy record would push an OR-typed shape onto
/// operators.
/// </para>
///
/// <para>
/// <b>DSL predicate.</b> <see cref="Predicate"/> is an optional
/// <see cref="Query.LogEventQuery"/> expression. When set, only events
/// whose <see cref="Events.LogEvent"/> matches the compiled query are
/// streamed. When unset, every event the decorator sees is streamed. The
/// predicate compiles once at pipeline build; matching runs in the hot
/// path so keep expressions simple (<c>level:error</c>,
/// <c>category:audit AND level:warn</c>).
/// </para>
///
/// <para><b>Edition.</b> Enterprise. Streaming archive requires the
/// cloud-provider dependencies (Azure Blob, S3 multipart) that Community
/// and Pro builds exclude.</para>
/// </summary>
public sealed record StreamingArchivePolicy
{
    /// <summary>Provider key — matches <see cref="IStreamingArchiveProvider.Kind"/>.</summary>
    public string Kind { get; init; } = "";

    /// <summary>
    /// Destination identifier. For <c>azureblob-stream</c> this is the
    /// container name; for <c>s3-stream</c> it is the bucket; other
    /// providers use the same convention as their closed-file counterparts.
    /// </summary>
    public string? Destination { get; init; }

    /// <summary>
    /// Optional path/key prefix inside the destination. Common pattern:
    /// namespace by environment plus date (<c>prod/2026/04/19</c>) so
    /// archive blobs are easy to list and enumerate.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// Template for the remote object name inside <see cref="Destination"/>
    /// after <see cref="Prefix"/>. Supported tokens:
    /// <list type="bullet">
    ///   <item><c>{timestamp}</c> — UTC ISO 8601 at session open.</item>
    ///   <item><c>{hostname}</c> — <see cref="Environment.MachineName"/>.</item>
    ///   <item><c>{pid}</c> — current process id.</item>
    /// </list>
    /// Default is <c>{timestamp}-{hostname}-{pid}.jsonl</c>, which gives
    /// every session a unique name without coordination. Providers may
    /// substitute a different default if the remote requires a specific
    /// suffix (e.g. <c>.parquet</c> for a columnar store).
    /// </summary>
    public string? ObjectNameTemplate { get; init; }

    /// <summary>
    /// Optional DSL predicate (see <see cref="Query.LogEventQuery"/>).
    /// When set, only matching events are streamed to the archive.
    /// Syntax: <c>field:value</c>, <c>=</c>, <c>!=</c>, <c>~</c>,
    /// <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>,
    /// <c>AND</c>/<c>OR</c>/<c>NOT</c>, parens, field paths.
    /// Examples: <c>level:error</c>,
    /// <c>category:compliance AND level&gt;=warn</c>.
    /// </summary>
    public string? Predicate { get; init; }

    /// <summary>
    /// Free-form key/value bag for provider-specific configuration. Mirrors
    /// <see cref="SinkArchivePolicy.Properties"/>; see
    /// <c>AzureBlobStreamingArchiveProvider</c> in the sibling
    /// <c>Herald.Core.Azure</c> assembly for the concrete keys each streaming
    /// provider consumes.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Properties { get; init; }

    /// <summary>
    /// Resolve a property value for <paramref name="key"/>, returning
    /// <paramref name="fallback"/> when absent.
    /// </summary>
    public string? GetProperty(string key, string? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (Properties is null) return fallback;
        return Properties.TryGetValue(key, out var value) ? value : fallback;
    }
}
