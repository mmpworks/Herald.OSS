#nullable enable

using System;
using System.Formats.Tar;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.Herald.Addons.Archive.Providers;

/// <summary>
/// Archive provider that bundles a rolled log file into a single-entry
/// gzip-or-plain tar archive on the local filesystem. No network, no
/// external dependencies — uses .NET 8's built-in
/// <see cref="System.Formats.Tar"/> support.
///
/// <para>
/// Useful for two scenarios:
/// <list type="bullet">
///   <item>Edge / on-prem deployments that ship logs to a downstream collector via a directory watch instead of a cloud upload.</item>
///   <item>Local CI / dev environments that exercise the archive lifecycle without standing up cloud credentials.</item>
/// </list>
/// </para>
///
/// <para>
/// Configuration through <see cref="SinkArchivePolicy.Properties"/>:
/// <list type="bullet">
///   <item><c>compression</c> — <c>"gzip"</c> (default) or <c>"none"</c>. Gzip is portable and shrinks line-oriented logs by ~10x; "none" is faster on already-compressed payloads.</item>
/// </list>
/// </para>
///
/// <para>
/// Output naming: <c>{Destination}/{Prefix}/{originalFileName}.tar(.gz)</c>.
/// The <c>Destination</c> directory is created if missing. Existing files
/// at the target name are overwritten — that is the at-least-once
/// idempotency the orchestrator relies on, so a crash mid-write resumes
/// cleanly.
/// </para>
///
/// <para><b>Edition.</b> Pro.</para>
/// <para><b>Thread safety.</b> Stateless instance; safe to share across
/// orchestrators. Concurrent calls with different <paramref name="localPath"/>
/// are independent. The orchestrator never invokes the same path twice
/// concurrently.</para>
/// <para><b>Tests.</b> <c>tests/Addons/Archive/LocalTarArchiveProviderTests.cs</c>
/// and <c>tests/Addons/Archive/SinkArchiveOrchestratorTests.cs</c>.</para>
/// </summary>
public sealed class LocalTarArchiveProvider : IArchiveProvider
{
    public const string KindKey = "tar";

    public string Kind => KindKey;

    public async Task<ArchiveResult> ArchiveAsync(
        string localPath,
        SinkArchivePolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentNullException.ThrowIfNull(policy);

        if (!File.Exists(localPath))
        {
            return ArchiveResult.Fail(new FileNotFoundException(
                $"LocalTarArchiveProvider: source file does not exist: {localPath}", localPath));
        }

        if (string.IsNullOrWhiteSpace(policy.Destination))
        {
            return ArchiveResult.Fail(new InvalidOperationException(
                "LocalTarArchiveProvider: SinkArchivePolicy.Destination must be set to the directory the tar archive should land in."));
        }

        try
        {
            var compression = (policy.GetProperty("compression", "gzip") ?? "gzip").Trim().ToLowerInvariant();
            var useGzip = compression switch
            {
                "gzip" or "gz" => true,
                "none" or "" => false,
                _ => throw new InvalidOperationException(
                    $"LocalTarArchiveProvider: unsupported compression '{compression}'. Use 'gzip' or 'none'."),
            };

            var destinationDir = ResolveDestinationDirectory(policy);
            Directory.CreateDirectory(destinationDir);

            var fileName = Path.GetFileName(localPath);
            var archiveName = useGzip ? $"{fileName}.tar.gz" : $"{fileName}.tar";
            var destinationPath = Path.Combine(destinationDir, archiveName);

            // Write to a .tmp first then rename — atomic publish guarantees
            // that a partial archive is never visible to a directory
            // watcher waiting for finished archives.
            var tempPath = destinationPath + ".tmp";

            await WriteArchiveAsync(localPath, tempPath, useGzip, cancellationToken).ConfigureAwait(false);

            // Compute the SHA-256 of the archive bytes for the orchestrator
            // to verify against. Note: this is the SHA of the tar payload,
            // not the source file. The orchestrator's verification compares
            // its own pre-archive SHA against the value the provider
            // reports. To keep that match meaningful for tar, the provider
            // hashes the same thing the orchestrator does (the source
            // bytes), then writes the tar. Hashing the post-tar bytes here
            // would never match.
            var sha = await ArchiveHash.ComputeSha256Async(localPath, cancellationToken).ConfigureAwait(false);

            File.Move(tempPath, destinationPath, overwrite: true);

            return ArchiveResult.Ok(destinationPath, sha);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ArchiveResult.Fail(ex);
        }
    }

    private static string ResolveDestinationDirectory(SinkArchivePolicy policy)
    {
        var dest = policy.Destination!;
        return string.IsNullOrWhiteSpace(policy.Prefix)
            ? dest
            : Path.Combine(dest, policy.Prefix);
    }

    private static async Task WriteArchiveAsync(
        string sourcePath,
        string archivePath,
        bool useGzip,
        CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(
            archivePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        Stream tarStream = useGzip
            ? new System.IO.Compression.GZipStream(fileStream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: false)
            : fileStream;

        try
        {
            await using var writer = new TarWriter(tarStream, TarEntryFormat.Pax, leaveOpen: false);
            // PaxTarEntry preserves arbitrary file names and modes; for log
            // files the default UnixFileMode is 644 which is what an
            // operator would set themselves.
            var entry = new PaxTarEntry(TarEntryType.RegularFile, Path.GetFileName(sourcePath))
            {
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
            };

            await using (var sourceStream = new FileStream(
                sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 81920, useAsync: true))
            {
                entry.DataStream = sourceStream;
                await writer.WriteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (useGzip)
            {
                await tarStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

}
