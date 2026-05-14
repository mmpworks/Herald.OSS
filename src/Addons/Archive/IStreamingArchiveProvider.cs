#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.Herald.Addons.Archive;

/// <summary>
/// Streaming variant of <see cref="IArchiveProvider"/>. Where the closed-file
/// archive path uploads a rolled log after rotation, a streaming provider
/// accepts events (as pre-serialised NDJSON bytes) one at a time and flushes
/// them to the remote as the pipeline runs. Closes the "hours of data in
/// flight" window that the closed-file path leaves open between rotations.
///
/// <para>
/// A provider is stateless at the provider level; per-destination lifecycle
/// lives on the <see cref="IStreamingArchiveSession"/> returned by
/// <see cref="OpenAsync"/>. An opened session owns a single remote object
/// (a blob, an S3 multipart upload, a tar segment) and finalises it on
/// <see cref="IStreamingArchiveSession.DisposeAsync"/>.
/// </para>
///
/// <para><b>Thread safety.</b> Providers must tolerate concurrent
/// <see cref="OpenAsync"/> calls from different pipelines. Sessions are
/// single-owner — one decorator opens a session at pipeline build, holds
/// it for the pipeline's lifetime, and disposes at pipeline teardown.
/// Session implementations do not need to be concurrency-safe internally;
/// the calling decorator serialises writes.</para>
///
/// <para><b>Failure model.</b> <see cref="IStreamingArchiveSession.AppendAsync"/>
/// must not throw except on cancellation — streaming archive is best-effort
/// backup, not an audit path. Transient failures should be logged via stderr
/// inside the session (not bubbled) so a sick remote does not take the
/// pipeline down. Use <see cref="Compliance.HmacChainLogger"/> for paths
/// that must propagate failure.</para>
/// </summary>
public interface IStreamingArchiveProvider
{
    /// <summary>
    /// Provider key matching <see cref="StreamingArchivePolicy.Kind"/>.
    /// Lowercase, no whitespace, stable across versions.
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// Open a streaming session against the destination described by
    /// <paramref name="policy"/>. The session owns the remote object until
    /// <see cref="IStreamingArchiveSession.DisposeAsync"/> finalises it.
    /// Session construction may perform network I/O (e.g. creating an
    /// append blob or initiating a multipart upload) and is therefore async.
    /// </summary>
    Task<IStreamingArchiveSession> OpenAsync(StreamingArchivePolicy policy, CancellationToken cancellationToken);
}

/// <summary>
/// Per-destination streaming session. Opened by
/// <see cref="IStreamingArchiveProvider.OpenAsync"/>, holds the remote
/// object's lifecycle, and is disposed by the owning decorator at pipeline
/// teardown.
///
/// <para><b>Append semantics.</b> Bytes passed to
/// <see cref="AppendAsync"/> are written to the remote in order. The
/// session may buffer internally; it is the session's responsibility to
/// flush on <see cref="DisposeAsync"/> so no buffered events are lost on
/// clean shutdown. A hard process kill can still lose in-flight bytes —
/// that is the trade-off for streaming (vs. the closed-file path which
/// never acknowledges until the remote verifies).</para>
/// </summary>
public interface IStreamingArchiveSession : IAsyncDisposable
{
    /// <summary>
    /// Identifier the operator can use to locate the remote object (for
    /// example, <c>azure://container/path/blob</c>). Stable for the
    /// lifetime of the session; implementations compute it at
    /// <see cref="IStreamingArchiveProvider.OpenAsync"/> time.
    /// </summary>
    string RemoteIdentifier { get; }

    /// <summary>
    /// Append a single pre-serialised payload to the remote. The payload is
    /// typically one NDJSON event (a JSON object followed by <c>\n</c>).
    /// The session may buffer and flush in larger batches — the only
    /// contract is that calls to <see cref="AppendAsync"/> preserve order
    /// and that <see cref="DisposeAsync"/> flushes every buffered byte.
    ///
    /// <para>Implementations must not throw on transient remote failures;
    /// log to stderr and continue. <see cref="OperationCanceledException"/>
    /// on <paramref name="cancellationToken"/> is the one exception that
    /// must propagate.</para>
    /// </summary>
    Task AppendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}
