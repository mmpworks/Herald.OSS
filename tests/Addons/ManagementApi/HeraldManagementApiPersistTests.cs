#nullable enable

using System;
using System.IO;
using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Contract tests for the persistence-failure surfacing fix on
/// <see cref="HeraldManagementApi"/> (principal-review queue #3).
///
/// <para>
/// The previous shape swallowed every <c>PersistConfig</c> exception
/// and reported <c>Ok</c>. An operator's last edits could vaporise on
/// reboot with no visible signal. The fix bubbles the error through
/// <see cref="ManagementResult.Fail"/>, so a failed disk write is
/// no longer indistinguishable from a successful one.
/// </para>
/// </summary>
public sealed class HeraldManagementApiPersistTests : IDisposable
{
    // A path whose parent component IS A FILE rather than a directory.
    // System.IO.Directory.CreateDirectory rejects this with IOException
    // on every platform we ship on, which exercises the catch branch
    // inside PersistConfig deterministically without any FS permissions
    // dance.
    private readonly string _blockingFile;
    private readonly string _badConfigPath;

    public HeraldManagementApiPersistTests()
    {
        _blockingFile  = Path.Combine(Path.GetTempPath(), $"herald-oss-persist-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(_blockingFile, "occupied");
        _badConfigPath = Path.Combine(_blockingFile, "child", "pipeline.json");
    }

    public void Dispose()
    {
        try { File.Delete(_blockingFile); } catch { /* swallow — best-effort cleanup */ }
    }

    [Fact]
    public void SetMinimumLevel_surfaces_persist_failure_through_ManagementResult()
    {
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance) { ConfigPath = _badConfigPath };

        var apiResult = api.SetMinimumLevel("debug");

        // The fix's contract: when the disk write fails, the API must
        // NOT report Ok. The previous behavior swallowed the exception
        // and the dashboard happily told the operator their edit was
        // saved.
        apiResult.Success.Should().BeFalse(
            "PersistConfig failed; the API must surface the failure rather than report Ok");
        apiResult.Message.Should().Contain(_badConfigPath,
            "the failure message should name the config path so the operator can find it");
        apiResult.Message.Should().Contain("Configuration save failed",
            "the failure must use the operator-readable persistence prefix");
    }

    [Fact]
    public void Persist_with_no_config_path_returns_ok_without_writing()
    {
        // The complementary case: when ConfigPath is null, there is
        // nothing to persist and the API must report Ok. PersistConfig
        // returns null on the no-path branch so failure-surfacing logic
        // doesn't accidentally flip in-memory-only flows into failures.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance); // ConfigPath unset

        var apiResult = api.SetMinimumLevel("info");

        apiResult.Success.Should().BeTrue(
            "no ConfigPath means there is nothing to persist; the operation is otherwise valid");
    }

    [Fact]
    public void CommitFull_returns_Fail_when_persist_fails()
    {
        // CommitFull is the dashboard's primary write path. The failure
        // mode this fix closes: validation passes, the in-memory builder
        // updates, the disk write blows up, and the API used to return
        // Ok with the failure invisible to the operator. Now it returns
        // Fail with the cause.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance) { ConfigPath = _badConfigPath };

        // Minimal valid commit payload — strategy + console sink, no
        // exotic configuration. The point of the test is the persist
        // step, not the commit body.
        const string body = """
        {
          "sinks": [
            { "sinkId": "console", "config": { "minLevel": "info" } }
          ]
        }
        """;

        var apiResult = api.CommitFull(body);

        apiResult.Success.Should().BeFalse(
            "CommitFull must not report Ok when the on-disk save failed");
        apiResult.Message.Should().Contain("Configuration save failed");
    }
}
