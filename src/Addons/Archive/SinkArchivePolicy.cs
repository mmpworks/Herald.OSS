#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Addons.Archive;

/// <summary>
/// Operator-facing description of an archive destination for rotated log
/// files. Wired onto a file sink via the orchestrator under
/// <see cref="SinkArchiveOrchestrator"/>; the kind is dispatched against
/// the <see cref="IArchiveProvider"/> registry to pick the concrete provider.
///
/// <para>
/// Today's providers:
/// <list type="bullet">
///   <item><c>tar</c> — local on-disk archive, no network. Fully implemented in <see cref="Providers.LocalTarArchiveProvider"/>.</item>
///   <item><c>s3</c> — Amazon S3 bucket. Implementation lives in <c>S3ArchiveProvider</c> in the sibling <c>Herald.Core.Aws</c> assembly.</item>
///   <item><c>azureblob</c> — Azure Blob Storage container. Implementation lives in <c>AzureBlobArchiveProvider</c> in the sibling <c>Herald.Core.Azure</c> assembly.</item>
/// </list>
/// </para>
///
/// <para>
/// Provider-specific knobs (credentials, region, endpoint URL) live in
/// <see cref="Properties"/> rather than as typed fields so that adding a
/// new provider does not bloat the policy record. The provider validates
/// what it needs at construction time.
/// </para>
///
/// <para><b>Edition.</b> Pro for tar; Enterprise for cloud providers.</para>
/// </summary>
public sealed record SinkArchivePolicy
{
    /// <summary>Provider key — matches <see cref="IArchiveProvider.Kind"/>. Required.</summary>
    public string Kind { get; init; } = "";

    /// <summary>
    /// Destination identifier. For <c>tar</c> this is the directory the
    /// archive file lands in; for <c>s3</c> it is the bucket name; for
    /// <c>azureblob</c> it is the container name.
    /// </summary>
    public string? Destination { get; init; }

    /// <summary>
    /// Optional path/key prefix the provider applies inside the destination.
    /// Lets operators namespace archives by environment, tenant, or date.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// When set, controls how often the orchestrator scans the rolled-file
    /// directory for archivable files. <c>null</c> means archive on every
    /// rotation event (immediate). Most operators want a small interval
    /// (e.g. one minute) so a burst of rotations coalesces into a single
    /// scan pass.
    /// </summary>
    public TimeSpan? Schedule { get; init; }

    /// <summary>
    /// Delete the local file after the provider confirms a successful upload
    /// and the SHA-256 verification matches. Default true. Set false when
    /// downstream processes still need the local copy (e.g. duplicate
    /// shipping to a fallback collector).
    /// </summary>
    public bool DeleteAfterVerify { get; init; } = true;

    /// <summary>
    /// Free-form key/value bag for provider-specific configuration. Common
    /// keys: <c>region</c> (S3), <c>endpoint</c> (S3 or compatible),
    /// <c>connectionString</c> (Azure), <c>accessKey</c> / <c>secretKey</c>
    /// (S3 — better to use environment-based credentials in production).
    /// </summary>
    public IReadOnlyDictionary<string, string>? Properties { get; init; }

    /// <summary>
    /// Resolve a property value for <paramref name="key"/>, returning
    /// <paramref name="fallback"/> when the key is absent. Convenience for
    /// providers that need to read optional knobs without the
    /// dictionary-null + try-get dance at every call site.
    /// </summary>
    public string? GetProperty(string key, string? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (Properties is null) return fallback;
        return Properties.TryGetValue(key, out var value) ? value : fallback;
    }
}
