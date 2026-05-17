#nullable enable

using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Pins the ADR-001 Seam 3 contract:
/// <see cref="HeraldManagementApi.RejectUnconfinedFileSinkPaths"/> is
/// a strict-mode opt-in. When <c>true</c> AND
/// <see cref="HeraldManagementApi.LogRootDirectory"/> is null, a
/// file-sink wiring call is rejected through
/// <see cref="ManagementResult.Fail"/> instead of falling through to
/// the legacy pass-through warning.
///
/// <para>
/// Default <c>false</c> preserves OSS Community behaviour — this is
/// tested separately by
/// <see cref="HeraldManagementApiFileSinkPathTests.SetFileSink_with_no_LogRootDirectory_falls_back_to_legacy_passthrough"/>.
/// </para>
/// </summary>
public sealed class RejectUnconfinedFileSinkPathsTests
{
    [Fact]
    public void Strict_mode_with_no_LogRootDirectory_rejects_file_sink_wiring()
    {
        // The strict-mode contract: opt in by setting the bool, and
        // the legacy pass-through path is closed off. A SetFileSink
        // call without a configured root must fail with an
        // operator-readable message that names the seam.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance)
        {
            RejectUnconfinedFileSinkPaths = true,
            // LogRootDirectory deliberately left null.
        };

        var apiResult = api.SetFileSink(enabled: true, path: "anywhere.log");

        apiResult.Success.Should().BeFalse(
            "strict mode without a configured root must reject the file-sink call");
        apiResult.Message.Should().Contain("LogRootDirectory",
            "the failure message must name the seam the operator needs to wire");
        apiResult.Message.Should().Contain("RejectUnconfinedFileSinkPaths",
            "the failure message must name the strict-mode property so the operator can find the call site");
    }

    [Fact]
    public void Strict_mode_with_configured_LogRootDirectory_accepts_paths_inside_root()
    {
        // Pin the happy path under strict mode: a configured root
        // re-enables file-sink wiring for paths that resolve inside
        // the root, identical to the non-strict behaviour. Without
        // this, the strict toggle would be all-or-nothing.
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"herald-strict-{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(root);
        try
        {
            var builder = QuickLogBuilder.Create().WithConsoleSink();
            var result  = builder.BuildAndCommit();
            var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance)
            {
                RejectUnconfinedFileSinkPaths = true,
                LogRootDirectory              = root,
            };

            var apiResult = api.SetFileSink(enabled: true, path: "today.log");

            apiResult.Success.Should().BeTrue();
        }
        finally
        {
            try { System.IO.Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Strict_mode_default_is_false_so_pre_1_0_callers_keep_working()
    {
        // Pin the default to false. The Community deployment shape
        // is "no LogRootDirectory, legacy pass-through with a
        // warning" — flipping the default would break every host
        // that hasn't wired the root yet.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance);

        api.RejectUnconfinedFileSinkPaths.Should().BeFalse(
            "default-false preserves pre-1.0 behaviour for Community consumers without a wired log root");
    }

    [Fact]
    public void Strict_mode_can_be_toggled_after_construction()
    {
        // Pin the setter contract: the property is mutable so a
        // host that boots in permissive mode and tightens later
        // (post-bootstrap policy load) can flip it without
        // recreating the API.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance);

        api.SetFileSink(enabled: true, path: "permissive.log").Success.Should().BeTrue(
            "without strict mode the legacy pass-through succeeds");

        api.RejectUnconfinedFileSinkPaths = true;
        api.SetFileSink(enabled: true, path: "denied.log").Success.Should().BeFalse(
            "flipping strict mode mid-lifetime closes the unconfined path immediately");
    }
}
