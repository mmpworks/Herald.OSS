#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace MMP.Herald.Addons.Archive;

/// <summary>
/// Contract every archive provider implements. The orchestrator at
/// <see cref="SinkArchiveOrchestrator"/> wires providers in by their
/// <see cref="Kind"/>, dispatching against the policy's
/// <see cref="SinkArchivePolicy.Kind"/>.
///
/// <para>
/// Providers must be at-least-once delivery. The orchestrator's crash-safe
/// checkpoint means a partially-completed archive may be retried after a
/// process crash; the provider must tolerate seeing the same local file
/// passed twice without corrupting the destination.
/// </para>
///
/// <para><b>Thread safety.</b> Providers are typically singletons shared
/// across all sinks that target the same destination. Implementations
/// must be safe to call concurrently from multiple threads with different
/// <paramref name="localPath"/> values; the orchestrator never invokes a
/// provider concurrently with the same path.</para>
/// </summary>
public interface IArchiveProvider
{
    /// <summary>
    /// Provider key matching <see cref="SinkArchivePolicy.Kind"/>.
    /// Lowercase, no whitespace, stable across versions (operator configs
    /// reference this string).
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// Ship <paramref name="localPath"/> to the destination described by
    /// <paramref name="policy"/>. The result reports whether the provider
    /// confirmed the upload, the remote identifier the operator can use to
    /// locate the archive, and the SHA-256 checksum the orchestrator
    /// records in the checkpoint for verification.
    ///
    /// <para>
    /// Providers must compute the SHA-256 of the local file's bytes
    /// (post-tar for the tar provider) so the orchestrator can confirm the
    /// upload landed unchanged. Returning a different SHA than the one
    /// computed at archive time fails the verification step and the
    /// orchestrator does not delete the local file.
    /// </para>
    /// </summary>
    /// <param name="localPath">Absolute path to the rolled log file or staging file to archive. The provider may read it but must not move or delete it — the orchestrator owns deletion.</param>
    /// <param name="policy">Effective policy. Provider-specific knobs come through <see cref="SinkArchivePolicy.Properties"/>.</param>
    /// <param name="cancellationToken">Cancels an in-progress upload. The provider must propagate cancellation; partial uploads on the destination side are allowed (re-upload on retry will overwrite).</param>
    /// <returns>An <see cref="ArchiveResult"/> capturing success/failure and the verifiable identifier + checksum on success.</returns>
    Task<ArchiveResult> ArchiveAsync(string localPath, SinkArchivePolicy policy, CancellationToken cancellationToken);
}
