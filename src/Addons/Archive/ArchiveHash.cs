#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.Herald.Addons.Archive;

/// <summary>
/// Shared SHA-256 helper for <see cref="IArchiveProvider"/>
/// implementations. Every provider needs the same pre-upload hash so the
/// orchestrator can verify the bytes that left disk match the bytes the
/// provider reports shipping; centralising the routine keeps the hash
/// format and I/O options consistent across providers and makes future
/// swaps (larger buffer, different algorithm, cancellation shape)
/// single-site.
///
/// <para><b>Thread safety.</b> The helper is a pure function. Each call
/// opens its own <see cref="FileStream"/> with <see cref="FileShare.ReadWrite"/>
/// so it coexists with live writers (the Stage 7 long-lived FileStream
/// pattern).</para>
/// </summary>
public static class ArchiveHash
{
    /// <summary>
    /// Compute the lowercase hex SHA-256 digest of the bytes at
    /// <paramref name="path"/>. 81 920 byte buffer matches every provider's
    /// prior inline implementation.
    /// </summary>
    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 81920, useAsync: true);
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Compute the raw MD5 bytes of the file at <paramref name="path"/>.
    /// Used by <c>AzureBlobArchiveProvider</c> (Herald.Core.Azure) for the
    /// <c>Content-MD5</c> HTTP header that Azure Blob Storage verifies on
    /// the transit hop. The orchestrator's end-to-end integrity check is
    /// SHA-256; MD5 here is only the Azure-API-level trip-wire.
    /// </summary>
    public static async Task<byte[]> ComputeMd5Async(string path, CancellationToken cancellationToken)
    {
        using var md5 = MD5.Create();
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 81920, useAsync: true);
        return await md5.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}
