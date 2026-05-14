#nullable enable

using System;
using System.IO;
using System.Text;

namespace MMP.Herald.Addons.Archive;

/// <summary>
/// On-disk checkpoint that lets <see cref="SinkArchiveOrchestrator"/> resume
/// an interrupted archive across process crashes. One checkpoint file per
/// rolled log file; the file lives next to the rolled log and is named
/// <c>&lt;original&gt;.archive.pos</c> mirroring the WAL position pattern at
/// <c>DurableBufferLogger</c>.
///
/// <para>
/// The checkpoint encodes three states:
/// <list type="bullet">
///   <item><c>InProgress</c> — written before the upload starts. After a crash, finding this status means the upload may or may not have completed; the orchestrator re-runs the provider, which is required to be idempotent (at-least-once).</item>
///   <item><c>Verified</c> — written after the SHA-256 confirmation. The local file is safe to delete; finding this state on resume means delete-was-pending.</item>
///   <item>(file absent) — no archive in progress. Default state.</item>
/// </list>
/// </para>
///
/// <para>
/// File format is deliberately plain-text TSV so an operator can read it
/// with <c>cat</c>:
/// <code>
/// status   in_progress
/// path     /var/log/herald/game.log.2026-04-18-12
/// sha256   3b1d...c7
/// remote   s3://bucket/prefix/game.log.2026-04-18-12
/// time     2026-04-18T22:41:03Z
/// </code>
/// </para>
///
/// <para><b>Atomic write.</b> The writer always writes to <c>&lt;path&gt;.tmp</c>
/// then renames over the final path so a crash mid-write leaves the
/// previous checkpoint (or no checkpoint) intact rather than a half-written
/// one. <see cref="File.Move(string,string,bool)"/> with overwrite is atomic
/// on the same filesystem on every supported runtime.</para>
/// </summary>
public sealed record SinkArchiveCheckpoint(
    SinkArchiveCheckpointStatus Status,
    string Path,
    string? Sha256,
    string? RemoteIdentifier,
    DateTimeOffset Timestamp)
{
    /// <summary>Suffix appended to the archived path to derive the checkpoint file name.</summary>
    public const string FileSuffix = ".archive.pos";

    /// <summary>Build the checkpoint file path that pairs with <paramref name="archivedPath"/>.</summary>
    public static string CheckpointPathFor(string archivedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivedPath);
        return archivedPath + FileSuffix;
    }

    /// <summary>
    /// Atomically persist the checkpoint to <paramref name="checkpointPath"/>
    /// using a write-temp-then-rename sequence. Throws on I/O failure; the
    /// caller treats that as "checkpoint not advanced" and will retry on
    /// the next pass.
    /// </summary>
    public void WriteAtomic(string checkpointPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointPath);
        var tmp = checkpointPath + ".tmp";
        var sb = new StringBuilder()
            .Append("status\t").AppendLine(SerializeStatus(Status))
            .Append("path\t").AppendLine(Path)
            .Append("sha256\t").AppendLine(Sha256 ?? "")
            .Append("remote\t").AppendLine(RemoteIdentifier ?? "")
            .Append("time\t").AppendLine(Timestamp.ToString("O"));
        File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
        File.Move(tmp, checkpointPath, overwrite: true);
    }

    /// <summary>
    /// Read a checkpoint from disk. Returns <c>null</c> when the file is
    /// absent (the no-archive-in-progress state).
    /// </summary>
    public static SinkArchiveCheckpoint? TryRead(string checkpointPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointPath);
        if (!File.Exists(checkpointPath)) return null;

        SinkArchiveCheckpointStatus status = SinkArchiveCheckpointStatus.InProgress;
        string path = "";
        string? sha = null;
        string? remote = null;
        DateTimeOffset time = DateTimeOffset.MinValue;

        foreach (var raw in File.ReadAllLines(checkpointPath, Encoding.UTF8))
        {
            var tabIdx = raw.IndexOf('\t');
            if (tabIdx <= 0) continue;
            var key = raw[..tabIdx];
            var value = raw[(tabIdx + 1)..];
            switch (key)
            {
                case "status": status = ParseStatus(value); break;
                case "path": path = value; break;
                case "sha256": sha = string.IsNullOrEmpty(value) ? null : value; break;
                case "remote": remote = string.IsNullOrEmpty(value) ? null : value; break;
                case "time": _ = DateTimeOffset.TryParse(value, out time); break;
            }
        }

        return new SinkArchiveCheckpoint(status, path, sha, remote, time);
    }

    /// <summary>Best-effort delete of the checkpoint file. No-op if it is already gone.</summary>
    public static void TryDelete(string checkpointPath)
    {
        try { if (File.Exists(checkpointPath)) File.Delete(checkpointPath); }
        catch { /* swallow — the file will be cleaned up on next pass */ }
    }

    private static string SerializeStatus(SinkArchiveCheckpointStatus s) => s switch
    {
        SinkArchiveCheckpointStatus.InProgress => "in_progress",
        SinkArchiveCheckpointStatus.Verified => "verified",
        _ => "in_progress",
    };

    private static SinkArchiveCheckpointStatus ParseStatus(string value) => value.Trim().ToLowerInvariant() switch
    {
        "verified" => SinkArchiveCheckpointStatus.Verified,
        _ => SinkArchiveCheckpointStatus.InProgress,
    };
}

/// <summary>
/// State machine for an archive checkpoint. Used by
/// <see cref="SinkArchiveOrchestrator"/> to decide whether to (re-)upload
/// or just complete the delete-after-verify step.
/// </summary>
public enum SinkArchiveCheckpointStatus
{
    /// <summary>Upload started but not yet confirmed. Resume re-runs the provider.</summary>
    InProgress = 0,

    /// <summary>Upload confirmed and SHA matched. Local delete is the only remaining step.</summary>
    Verified = 1,
}
