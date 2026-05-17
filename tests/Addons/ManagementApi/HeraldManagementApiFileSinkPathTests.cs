#nullable enable

using System;
using System.IO;
using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Tests for the file-sink path confinement on
/// <see cref="HeraldManagementApi"/> (principal-review queue #2).
///
/// <para>
/// Before the fix: any caller holding a Management API instance
/// could call SetFileSink with an arbitrary path and have the log
/// pipeline append to any file the process could write — including
/// well-known cron / SSH / Task Scheduler locations. This test pins
/// the rejection contract: when LogRootDirectory is configured, a
/// path that escapes the root is rejected through
/// <see cref="ManagementResult.Fail"/> rather than wired into the
/// pipeline.
/// </para>
/// </summary>
public sealed class HeraldManagementApiFileSinkPathTests : IDisposable
{
    private readonly string _logRoot;

    public HeraldManagementApiFileSinkPathTests()
    {
        _logRoot = Path.Combine(Path.GetTempPath(), $"herald-oss-logroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_logRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_logRoot, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void SetFileSink_rejects_path_escaping_LogRootDirectory()
    {
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result)
        {
            LogRootDirectory = _logRoot,
        };

        // Classic escape: ../../etc/passwd-equivalent. The confine guard
        // canonicalises both paths before comparing, so .. segments
        // collapse before the prefix check sees them.
        var escape = Path.Combine("..", "..", "outside.log");

        var apiResult = api.SetFileSink(enabled: true, path: escape);

        apiResult.Success.Should().BeFalse(
            "a file-sink path that escapes LogRootDirectory must be rejected through ManagementResult.Fail");
        apiResult.Message.Should().Contain("escapes",
            "the failure message should explain that the path escaped the confined base");
    }

    [Fact]
    public void SetFileSink_accepts_path_within_LogRootDirectory()
    {
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result)
        {
            LogRootDirectory = _logRoot,
        };

        // The complementary case: a normal "subdir/today.log" stays
        // inside the root and the API must accept it. Without this we
        // can't tell whether the rejection above was the seam working
        // or the API being broken for all inputs.
        var apiResult = api.SetFileSink(enabled: true, path: "today.log");

        apiResult.Success.Should().BeTrue();
    }

    [Fact]
    public void SetFileSink_with_no_LogRootDirectory_falls_back_to_legacy_passthrough()
    {
        // Backward-compatibility: pre-1.0 callers that haven't set
        // LogRootDirectory continue to work. The runtime-notice channel
        // surfaces a Warning so an operator running diagnostics sees
        // the gap; the API itself doesn't fail.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result); // LogRootDirectory unset

        var apiResult = api.SetFileSink(enabled: true, path: "anywhere.log");

        apiResult.Success.Should().BeTrue(
            "pre-1.0 callers without LogRootDirectory still receive Ok; the warning is on the runtime-notice channel");
    }
}
