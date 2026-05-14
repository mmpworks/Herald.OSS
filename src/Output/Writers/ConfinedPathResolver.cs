#nullable enable

using System;
using System.IO;

namespace MMP.Herald.Output.Writers;

/// <summary>
/// Path resolver that confines all resolved paths to a configured base
/// directory. Any attempt to resolve a path that escapes the base (via
/// parent-directory references, absolute overrides, or symlink-adjacent
/// tricks on the textual path) is rejected with
/// <see cref="InvalidOperationException"/>.
///
/// Use when configuration originates from an untrusted source (user upload,
/// HTTP request body, hot-reloaded JSON). The default
/// <see cref="DefaultLogFilePathResolver"/> passes paths through unchanged
/// and is appropriate only when the caller already trusts its configuration.
///
/// Example:
/// <code>
/// var resolver = new ConfinedPathResolver("/var/log/app");
/// var writer   = new FileLineWriter("today.log", resolver);
/// // writes to /var/log/app/today.log
/// // "../../../etc/passwd" is rejected.
/// </code>
/// </summary>
public sealed class ConfinedPathResolver : ILogFilePathResolver
{
    private readonly string _baseDirectory;

    public ConfinedPathResolver(string baseDirectory) {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        // Canonicalize once at construction so every Resolve call compares
        // against a stable, absolute form.
        _baseDirectory = Path.GetFullPath(baseDirectory);
    }

    /// <summary>
    /// Base directory that every resolved path must live within.
    /// Exposed for diagnostic and test assertions.
    /// </summary>
    public string BaseDirectory => _baseDirectory;

    public string Resolve(string filePath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Combine and canonicalize together so "../" segments collapse before
        // we compare. Path.Combine drops the base when filePath is rooted,
        // which is exactly the case we want to catch; GetFullPath then
        // produces an absolute form we can prefix-compare.
        var combined = Path.Combine(_baseDirectory, filePath);
        var resolved = Path.GetFullPath(combined);

        if (!IsWithinBase(resolved))
        {
            throw new InvalidOperationException(
                $"Resolved path '{resolved}' escapes the configured base directory '{_baseDirectory}'.");
        }

        return resolved;
    }

    private bool IsWithinBase(string resolved) {
        // Exact match to the base dir is OK (legal file write at the root
        // would still need a file name, so callers rarely hit this; included
        // for correctness).
        if (string.Equals(resolved, _baseDirectory, StringComparison.Ordinal))
        {
            return true;
        }

        // Normal case: resolved path starts with "<base><sep>...".
        var prefix = _baseDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? _baseDirectory
            : _baseDirectory + Path.DirectorySeparatorChar;

        return resolved.StartsWith(prefix, StringComparison.Ordinal);
    }
}
