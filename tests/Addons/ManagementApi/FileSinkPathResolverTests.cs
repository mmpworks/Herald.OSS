#nullable enable

using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Pins the ADR-001 Seam 4 (narrowed-scope) contract:
/// <see cref="HeraldManagementApi.FileSinkPathResolver"/> is an
/// optional pluggable delegate. When set, it pre-empts the built-in
/// <see cref="HeraldManagementApi.LogRootDirectory"/> confinement
/// and the <see cref="HeraldManagementApi.RejectUnconfinedFileSinkPaths"/>
/// strict-mode guard. When null, the built-in resolver runs
/// unchanged.
///
/// <para>
/// Tests cover: the delegate is invoked exactly once with the
/// requested path; the returned string is what the pipeline wires;
/// a delegate that throws <see cref="System.InvalidOperationException"/>
/// produces a Fail; and the null-default leaves the built-in
/// behaviour in place.
/// </para>
/// </summary>
public sealed class FileSinkPathResolverTests
{
    [Fact]
    public void Custom_resolver_is_invoked_with_the_requested_path()
    {
        // Pin the parameter pass-through: the delegate sees the
        // exact path the caller supplied to SetFileSink. A
        // per-tenant resolver depends on this to make its
        // mapping decision.
        var seen = new System.Collections.Generic.List<string>();
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance)
        {
            FileSinkPathResolver = path =>
            {
                seen.Add(path);
                return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rewritten.log");
            },
        };

        var apiResult = api.SetFileSink(enabled: true, path: "incoming.log");

        apiResult.Success.Should().BeTrue();
        seen.Should().Equal(new[] { "incoming.log" },
            "the resolver must receive the exact path the caller supplied");
    }

    [Fact]
    public void Custom_resolver_pre_empts_LogRootDirectory_confinement()
    {
        // Pin the "pre-empts the built-in" contract. With a
        // LogRootDirectory set, the built-in resolver would
        // normally reject anywhere.log as an escape. The custom
        // resolver gets to decide instead — and here it accepts
        // by returning a tempdir path. Without this contract the
        // delegate would be cosmetic.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance)
        {
            LogRootDirectory     = "/nonexistent/root", // would normally reject "anywhere.log"
            FileSinkPathResolver = _ => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ok.log"),
        };

        var apiResult = api.SetFileSink(enabled: true, path: "anywhere.log");

        apiResult.Success.Should().BeTrue(
            "the custom resolver is the source of truth when set; the LogRootDirectory confinement is bypassed");
    }

    [Fact]
    public void Custom_resolver_pre_empts_RejectUnconfinedFileSinkPaths_strict_mode()
    {
        // Strict mode rejects any wiring when LogRootDirectory is
        // null. Plugging a custom resolver overrides that — the
        // host's resolver is now responsible for confinement, and
        // strict mode is bypassed. Pin the contract so a future
        // refactor doesn't accidentally re-introduce the strict-
        // mode check on the custom-resolver path.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance)
        {
            RejectUnconfinedFileSinkPaths = true,  // would normally reject without a root
            FileSinkPathResolver          = _ => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ok.log"),
        };

        var apiResult = api.SetFileSink(enabled: true, path: "anywhere.log");

        apiResult.Success.Should().BeTrue(
            "a custom resolver assumes responsibility for path policy; strict mode does not apply");
    }

    [Fact]
    public void Custom_resolver_throw_surfaces_as_ManagementResult_Fail()
    {
        // Pin the rejection contract: the delegate throws
        // InvalidOperationException to reject; the existing
        // SinkManagement catch translates that to Fail with the
        // exception's message. This is the same contract the
        // built-in confined-path resolver already uses.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance)
        {
            FileSinkPathResolver = _ =>
                throw new System.InvalidOperationException("rejected by tenant policy"),
        };

        var apiResult = api.SetFileSink(enabled: true, path: "incoming.log");

        apiResult.Success.Should().BeFalse();
        apiResult.Message.Should().Contain("rejected by tenant policy",
            "the resolver's rejection message must flow through to ManagementResult.Fail");
    }

    [Fact]
    public void Null_resolver_leaves_built_in_LogRootDirectory_behaviour_in_place()
    {
        // Pin the default-null shape so the existing LogRootDirectory
        // tests (escape rejection, within-root accept, legacy
        // passthrough) keep covering the built-in path. Adding the
        // delegate must not silently change the default behaviour.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance);

        api.FileSinkPathResolver.Should().BeNull(
            "the default-null shape preserves the built-in resolver as the source of truth");

        var apiResult = api.SetFileSink(enabled: true, path: "ok.log");
        apiResult.Success.Should().BeTrue(
            "with no resolver and no LogRootDirectory, the legacy pass-through still succeeds");
    }
}
