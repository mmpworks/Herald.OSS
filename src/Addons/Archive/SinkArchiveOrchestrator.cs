#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.Herald.Addons.Archive;

/// <summary>
/// Owns the flush → archive → verify → delete lifecycle for one rolled log
/// file. Crash-safe via <see cref="SinkArchiveCheckpoint"/>: every state
/// transition lands on disk before the next side effect, so a process
/// crash leaves a checkpoint the next run can resume from.
///
/// <para>
/// One orchestrator is typically created per archive policy and called
/// repeatedly by a scheduler (in-process timer, external cron, or
/// rotation-triggered hook) with the rolled file paths to ship. The
/// orchestrator is stateless apart from the on-disk checkpoint, so it is
/// safe to recreate across calls.
/// </para>
///
/// <para><b>Edition.</b> Pro for tar; Enterprise for cloud providers.</para>
/// <para><b>Thread safety.</b> The orchestrator delegates concurrency to
/// the caller — the same <paramref name="localPath"/> must not be passed
/// to two concurrent <see cref="ArchiveAsync"/> calls. Different paths in
/// parallel are safe and benefit from the underlying provider's
/// concurrency.</para>
/// <para><b>Tests.</b> <c>tests/Addons/Archive/SinkArchiveOrchestratorTests.cs</c>
/// (covers happy path, crash mid-archive resume, and crash-after-upload-before-checkpoint
/// idempotent re-upload).</para>
/// </summary>
public sealed class SinkArchiveOrchestrator
{
    private readonly IArchiveProvider _provider;
    private readonly Func<DateTimeOffset> _clock;

    public SinkArchiveOrchestrator(IArchiveProvider provider, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Archive a single rolled log file end-to-end. Returns the final
    /// outcome — success means the upload was confirmed, the checksum
    /// matched, and (when the policy says so) the local file was deleted.
    ///
    /// <para>
    /// The lifecycle in detail:
    /// <list type="number">
    ///   <item>If a verified checkpoint exists from a prior crash, jump straight to the delete step.</item>
    ///   <item>Compute SHA-256 of the local file. Bytes the provider sees and bytes we hash are the same — providers do not transform the payload.</item>
    ///   <item>Write an in-progress checkpoint with the SHA. After this point a crash leaves the checkpoint visible; the next call re-runs the provider.</item>
    ///   <item>Call the provider. On failure, return early with the error; the in-progress checkpoint stays for the next pass.</item>
    ///   <item>Verify the provider-reported SHA matches the locally-computed SHA. Mismatch fails the operation; the local file stays put.</item>
    ///   <item>Promote the checkpoint to verified.</item>
    ///   <item>If the policy says <see cref="SinkArchivePolicy.DeleteAfterVerify"/>, delete the local file and the checkpoint.</item>
    /// </list>
    /// </para>
    /// </summary>
    public async Task<ArchiveResult> ArchiveAsync(
        string localPath,
        SinkArchivePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentNullException.ThrowIfNull(policy);

        if (!File.Exists(localPath))
        {
            return ArchiveResult.Fail(new FileNotFoundException(
                $"Archive source does not exist: {localPath}", localPath));
        }

        var checkpointPath = SinkArchiveCheckpoint.CheckpointPathFor(localPath);
        var existing = SinkArchiveCheckpoint.TryRead(checkpointPath);

        if (existing is { Status: SinkArchiveCheckpointStatus.Verified })
        {
            // Resume of a delete that did not complete: re-run the delete step.
            return CompleteVerified(localPath, checkpointPath, existing, policy);
        }

        var sha = await ArchiveHash.ComputeSha256Async(localPath, cancellationToken).ConfigureAwait(false);

        // Write the in-progress checkpoint BEFORE invoking the provider so
        // that a crash mid-upload is recoverable. The checkpoint records
        // the SHA we expect the provider to confirm.
        var inProgress = new SinkArchiveCheckpoint(
            SinkArchiveCheckpointStatus.InProgress,
            localPath,
            sha,
            RemoteIdentifier: existing?.RemoteIdentifier,
            Timestamp: _clock());
        inProgress.WriteAtomic(checkpointPath);

        ArchiveResult providerResult;
        try
        {
            providerResult = await _provider.ArchiveAsync(localPath, policy, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a failure. Leave the in-progress
            // checkpoint so the next run resumes.
            throw;
        }
        catch (Exception ex)
        {
            return ArchiveResult.Fail(ex);
        }

        if (!providerResult.Success)
        {
            return providerResult;
        }

        if (!string.Equals(providerResult.Sha256, sha, StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveResult.Fail(new InvalidOperationException(
                $"Archive verification failed for '{localPath}': local SHA-256 {sha} did not match provider-reported {providerResult.Sha256}. " +
                $"The local file is intact and will be retried on the next pass."));
        }

        var verified = new SinkArchiveCheckpoint(
            SinkArchiveCheckpointStatus.Verified,
            localPath,
            sha,
            providerResult.RemoteIdentifier,
            _clock());
        verified.WriteAtomic(checkpointPath);

        return CompleteVerified(localPath, checkpointPath, verified, policy);
    }

    private static ArchiveResult CompleteVerified(
        string localPath,
        string checkpointPath,
        SinkArchiveCheckpoint checkpoint,
        SinkArchivePolicy policy)
    {
        if (policy.DeleteAfterVerify)
        {
            try { File.Delete(localPath); }
            catch (Exception ex)
            {
                // The upload succeeded; the only remaining concern is the
                // local file lingering. Surface the error so the operator
                // can clean up, but do not undo the verified state.
                return ArchiveResult.Fail(new InvalidOperationException(
                    $"Archive succeeded but local delete failed for '{localPath}'. " +
                    $"The remote copy is intact; clean up the local file manually.", ex));
            }
        }

        // Successful archive run completes by removing the checkpoint so
        // the next iteration starts from a clean slate.
        SinkArchiveCheckpoint.TryDelete(checkpointPath);

        return ArchiveResult.Ok(checkpoint.RemoteIdentifier ?? "", checkpoint.Sha256 ?? "");
    }

}
