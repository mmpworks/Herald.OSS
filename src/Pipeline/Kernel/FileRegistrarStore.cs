#nullable enable

using System;
using System.IO;
using System.Text.Json;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// File-backed implementation of <see cref="IRegistrarStore"/>. Persists
/// the snapshot as JSON at a configured path. Atomic write via
/// temp-file-plus-rename so a crash mid-write leaves the previous
/// snapshot intact.
///
/// <para>
/// <b>Permissions.</b> The file is sensitive — the reference token in
/// it lets a holder mint derived keys for any sink in the pipeline.
/// On Unix the store sets mode <c>0600</c> after every write
/// (owner read/write, no group, no other). On Windows the store
/// relies on the parent directory's ACL — operators who need stricter
/// per-file ACLs should set them on the directory before the first
/// boot. The store does not silently change Windows ACLs because the
/// shape varies by environment (domain accounts, service accounts,
/// container identities), and getting it wrong is worse than relying
/// on the directory's posture.
/// </para>
///
/// <para>
/// <b>Atomic write.</b>
/// <list type="number">
///   <item>Serialise to a temp file at <c>&lt;path&gt;.tmp</c>.</item>
///   <item>Flush to disk via <see cref="FileStream.Flush(bool)"/>.</item>
///   <item>Apply restrictive permissions on the temp file.</item>
///   <item>Rename the temp file over the target via <see cref="File.Move(string, string, bool)"/>.</item>
/// </list>
/// On NTFS and ext4 the rename is atomic — readers see either the old
/// snapshot or the new one, never a half-written file.
/// </para>
///
/// <para>
/// <b>Threading.</b> The store has its own write lock so two threads
/// calling <see cref="Save"/> serialise. The registrar already
/// serialises mutations through its own lock; the second lock here is
/// belt-and-suspenders for callers that share the store across
/// registrars.
/// </para>
/// </summary>
public sealed class FileRegistrarStore : IRegistrarStore
{
    private readonly string _path;
    private readonly object _writeLock = new();

    public FileRegistrarStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    /// <summary>The fully-qualified path the store reads and writes.</summary>
    public string Path_ => _path;

    public RegistrarSnapshot? Load()
    {
        if (!File.Exists(_path)) return null;

        var json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json)) return null;

        RegistrarSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize(
                json, RegistrarJsonContext.Default.RegistrarSnapshot);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Registrar snapshot at '{_path}' is not valid JSON. " +
                $"Inspect the file or delete it to start fresh. " +
                $"Original error: {ex.Message}", ex);
        }

        if (snapshot is null) return null;

        if (snapshot.SchemaVersion > RegistrarSnapshot.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Registrar snapshot at '{_path}' has schema version " +
                $"{snapshot.SchemaVersion}; this build understands up to " +
                $"version {RegistrarSnapshot.CurrentSchemaVersion}. The " +
                $"snapshot was likely written by a newer build — upgrade " +
                $"or remove the file to start fresh.");
        }

        return snapshot;
    }

    public void Save(RegistrarSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_writeLock)
        {
            EnsureDirectoryExists(_path);

            var tempPath = _path + ".tmp";

            // Serialize + flush in a using block so the file handle is
            // closed before the rename. Windows blocks rename-over-open.
            using (var fs = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(
                    fs, snapshot, RegistrarJsonContext.Default.RegistrarSnapshot);
                fs.Flush(flushToDisk: true);
            }

            ApplyRestrictivePermissions(tempPath);

            // File.Move with overwrite is atomic on NTFS / ext4 — readers
            // see either the old file or the new one, never a torn write.
            File.Move(tempPath, _path, overwrite: true);
        }
    }

    private static void EnsureDirectoryExists(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static void ApplyRestrictivePermissions(string path)
    {
        // Unix: 0600 (owner rw, no group, no other). The reference token
        // in the file is sensitive — a holder can mint keys for every
        // sink in the pipeline. On Windows the store relies on the
        // parent directory's ACL because per-file ACL setting requires
        // System.IO.FileSystem.AccessControl and the right ACL shape
        // varies by host environment. Operators with stricter
        // requirements set the directory ACL before first boot.
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (PlatformNotSupportedException)
            {
                // No Unix file mode on this runtime — fall through. The
                // file is still usable; the operator just doesn't get the
                // mode-bit guarantee.
            }
        }
    }
}
