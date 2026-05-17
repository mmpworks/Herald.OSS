#nullable enable

using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Tests for the <see cref="IManagementApiAuthorizer"/> seam on
/// <see cref="HeraldManagementApi"/> (principal-review queue #1).
///
/// <para>
/// Three contracts:
/// </para>
/// <list type="number">
///   <item>Default authorizer (<see cref="RejectAllAuthorizer"/>)
///   rejects every mutating call so an unconfigured host can't be
///   tricked into mutating its pipeline.</item>
///   <item>Explicit <see cref="AllowAllAuthorizer"/> bypasses the
///   gate for test harnesses and trusted in-process callers.</item>
///   <item>Operation name flows through the authorizer so custom
///   implementations can audit and decide per-operation.</item>
/// </list>
/// </summary>
public sealed class HeraldManagementApiAuthorizationTests
{
    [Fact]
    public void Default_authorizer_rejects_mutating_calls()
    {
        // The OSS default is RejectAllAuthorizer — wiring this default
        // is what prevents an unauthenticated HTTP layer from quietly
        // forwarding mutations through this API. Pin the contract: a
        // mutating call WITHOUT an explicit authorizer must fail with
        // an operator-readable message that names the missing seam.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result);

        var apiResult = api.SetMinimumLevel("info");

        apiResult.Success.Should().BeFalse(
            "the default RejectAllAuthorizer denies every mutating call");
        apiResult.Message.Should().Contain("IManagementApiAuthorizer",
            "the rejection message must point operators at the seam they need to configure");
    }

    [Fact]
    public void AllowAllAuthorizer_permits_mutating_calls()
    {
        // The escape hatch for test harnesses and trusted in-process
        // callers. Explicitly opt in by passing AllowAllAuthorizer.
        // This is the only way the parameterless constructor used to
        // behave; making it an explicit choice prevents accidental
        // production exposure of an unauthenticated API.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance);

        var apiResult = api.SetMinimumLevel("info");

        apiResult.Success.Should().BeTrue(
            "AllowAllAuthorizer is the documented opt-out for trusted callers");
    }

    [Fact]
    public void Authorizer_setter_swaps_the_active_authorizer_mid_lifetime()
    {
        // Lets an operator who wires authentication after the host
        // has already started switch the gate without recreating the
        // API. The setter normalises null → RejectAllAuthorizer so a
        // misuse can't accidentally open the API.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance);

        api.SetMinimumLevel("info").Success.Should().BeTrue();

        api.Authorizer = RejectAllAuthorizer.Instance;
        api.SetMinimumLevel("debug").Success.Should().BeFalse(
            "switching to RejectAllAuthorizer closes the gate immediately");

        api.Authorizer = null!; // normalised to RejectAllAuthorizer
        api.SetMinimumLevel("warn").Success.Should().BeFalse(
            "null authorizer must normalise to the safe default, not open the gate");
    }

    [Fact]
    public void Custom_authorizer_receives_operation_name_and_can_decide_per_method()
    {
        // Pin the operation-name contract: the authorizer sees the
        // mutating-method name so a custom implementation can grant
        // read/list endpoints while withholding writes, audit-log per
        // operation, or rate-limit specific calls.
        var seen = new System.Collections.Generic.List<string>();
        var custom = new RecordingAuthorizer(seen, allow: true);

        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, custom);

        api.SetMinimumLevel("info");
        api.SetConsoleSink(enabled: true);

        seen.Should().Equal(new[] { "SetMinimumLevel", "SetConsoleSink" });
    }

    private sealed class RecordingAuthorizer : IManagementApiAuthorizer
    {
        private readonly System.Collections.Generic.List<string> _seen;
        private readonly bool _allow;
        public RecordingAuthorizer(System.Collections.Generic.List<string> seen, bool allow)
        {
            _seen  = seen;
            _allow = allow;
        }

        public bool IsAuthorized(string operation, out string? reason)
        {
            _seen.Add(operation);
            reason = _allow ? null : "denied by recording authorizer";
            return _allow;
        }
    }
}
