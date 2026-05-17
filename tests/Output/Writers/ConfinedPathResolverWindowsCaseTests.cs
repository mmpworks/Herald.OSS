#nullable enable

using System;
using System.IO;
using FluentAssertions;
using MMP.Herald.Output.Writers;
using Xunit;

namespace MMP.Herald.OSS.Tests.Output.Writers;

/// <summary>
/// Tests for the Windows case-insensitive comparison fix in
/// <see cref="ConfinedPathResolver"/> (principal-review queue #2).
///
/// <para>
/// Background: file systems on Windows are case-insensitive by
/// default. The previous code used <see cref="StringComparison.Ordinal"/>
/// on the prefix check, which rejected a resolved path whose only
/// difference from the configured base was casing — the OS would
/// have opened the same file either way, so the confine guard
/// flagged a non-escape as an escape.
/// </para>
///
/// <para>
/// These tests are conditional on the runtime OS: the Windows case
/// runs only on Windows, and the non-Windows case runs only on
/// non-Windows. Either platform asserts the same contract — the
/// resolver matches what the OS will actually open.
/// </para>
/// </summary>
public sealed class ConfinedPathResolverWindowsCaseTests
{
    [Fact]
    public void Windows_accepts_case_differing_subpath_within_base()
    {
        if (!OperatingSystem.IsWindows()) return; // platform-specific

        // Construct the base with one casing; resolve with a different
        // casing on the same logical directory. NTFS/ReFS treat both as
        // the same file system object, so the resolver must too.
        var baseDir = Path.Combine(Path.GetTempPath(), $"Herald-OSS-Case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);
        try
        {
            var resolver = new ConfinedPathResolver(baseDir);

            // Same directory, lowercase variant — Windows opens the
            // same file whichever casing the operator types.
            var lowered = baseDir.ToLowerInvariant();
            var probe   = Path.Combine(lowered, "today.log");

            var result = resolver.Resolve(probe);
            result.Should().NotBeNullOrEmpty();
            // The resolver may canonicalise casing in either direction;
            // the contract is that it does NOT throw the escape exception
            // when only casing differs.
        }
        finally
        {
            try { Directory.Delete(baseDir); } catch { /* swallow cleanup error */ }
        }
    }

    [Fact]
    public void Windows_still_rejects_actual_parent_dir_escape()
    {
        if (!OperatingSystem.IsWindows()) return; // platform-specific

        // The case-insensitive fix MUST NOT widen the escape guard. A
        // textual escape with ../ that lands outside the base is still
        // an escape, casing or no casing.
        var baseDir = Path.Combine(Path.GetTempPath(), $"Herald-OSS-Esc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);
        try
        {
            var resolver = new ConfinedPathResolver(baseDir);

            var action = () => resolver.Resolve("../outside.log");
            action.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            try { Directory.Delete(baseDir); } catch { /* swallow cleanup error */ }
        }
    }

    [Fact]
    public void Non_Windows_rejects_case_differing_subpath_within_base()
    {
        if (OperatingSystem.IsWindows()) return; // platform-specific

        // On non-Windows, the file system is case-sensitive in practice.
        // The resolver MUST stay strict so a path that wouldn't open at
        // the OS layer doesn't pass the confine guard.
        var baseDir = Path.Combine(Path.GetTempPath(), $"Herald-OSS-CaseUnix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDir);
        try
        {
            var resolver = new ConfinedPathResolver(baseDir);

            var probe = Path.Combine(baseDir.ToUpperInvariant(), "today.log");
            var action = () => resolver.Resolve(probe);

            // Unless the test happened to construct a base whose upper-
            // case form equals its lower-case form (no letters), the
            // resolver should not accept a casing-differing path on a
            // case-sensitive file system.
            if (!string.Equals(baseDir, baseDir.ToUpperInvariant(), StringComparison.Ordinal))
            {
                action.Should().Throw<InvalidOperationException>();
            }
        }
        finally
        {
            try { Directory.Delete(baseDir); } catch { /* swallow cleanup error */ }
        }
    }
}
