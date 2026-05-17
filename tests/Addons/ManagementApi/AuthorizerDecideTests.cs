#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Pins the contract of the ADR-001 Seam 5 reshape:
/// <see cref="IManagementApiAuthorizer.Decide"/> +
/// <see cref="IManagementApiAuthorizer.DecideAsync"/> returning a
/// structured <see cref="AuthorizationDecision"/>. Covers both
/// built-in implementations
/// (<see cref="RejectAllAuthorizer"/> / <see cref="AllowAllAuthorizer"/>)
/// across the sync and async entries, the default-body sync-forward
/// wrap, the context's conversion of denial → <see cref="ManagementResult"/>
/// with the typed <see cref="DenialKind"/> preserved, and the
/// kind-propagation through to the
/// <see cref="HeraldManagementApi"/> surface.
/// </summary>
public sealed class AuthorizerDecideTests
{
    // ── Built-in: RejectAllAuthorizer ─────────────────────────────────

    [Fact]
    public void RejectAllAuthorizer_Decide_denies_with_Auth_kind_and_operator_message()
    {
        var decision = RejectAllAuthorizer.Instance.Decide("SetMinimumLevel");

        decision.Allowed.Should().BeFalse();
        decision.Kind.Should().Be(DenialKind.Auth,
            "an unconfigured host's rejection is an authentication-shaped denial — the operator hasn't wired a real authorizer yet");
        decision.Reason.Should().Contain("IManagementApiAuthorizer",
            "the rejection message must name the seam the operator needs to configure");
        decision.Facts.Should().BeNull();
    }

    [Fact]
    public async Task RejectAllAuthorizer_DecideAsync_default_body_forwards_to_Decide()
    {
        // The default async body wraps Decide in a completed
        // ValueTask. A sync authorizer pays no async-state-machine
        // cost — pinning the default behaviour so an async-only
        // decorator can dispatch through DecideAsync without
        // worrying that built-in sync authorizers misbehave.
        // DecideAsync is a default-interface-method so the call
        // dispatches through the interface, mirroring how a
        // production decorator would reach it.
        IManagementApiAuthorizer authorizer = RejectAllAuthorizer.Instance;
        var async = await authorizer.DecideAsync("SetMinimumLevel");
        var sync  = authorizer.Decide("SetMinimumLevel");

        async.Should().Be(sync);
    }

    // ── Built-in: AllowAllAuthorizer ──────────────────────────────────

    [Fact]
    public void AllowAllAuthorizer_Decide_allows_with_null_kind_and_reason()
    {
        var decision = AllowAllAuthorizer.Instance.Decide("SetMinimumLevel");

        decision.Allowed.Should().BeTrue();
        decision.Reason.Should().BeNull();
        decision.Kind.Should().BeNull();
        decision.Facts.Should().BeNull();
    }

    [Fact]
    public async Task AllowAllAuthorizer_DecideAsync_default_body_forwards_to_Decide()
    {
        IManagementApiAuthorizer authorizer = AllowAllAuthorizer.Instance;
        var async = await authorizer.DecideAsync("SetMinimumLevel");
        var sync  = authorizer.Decide("SetMinimumLevel");

        async.Should().Be(sync);
    }

    // ── Default async body wrap (sync-only authorizer) ────────────────

    [Fact]
    public async Task Default_DecideAsync_body_invokes_sync_Decide_and_returns_completed_ValueTask()
    {
        // Pin the contract for any future sync-only authorizer:
        // implementing only Decide must produce a completed
        // ValueTask without an async state machine. The completed-
        // ness check uses ValueTask.IsCompletedSuccessfully — the
        // default body MUST not allocate a Task wrapper.
        var sync = new SyncOnlyCountingAuthorizer();
        IManagementApiAuthorizer authorizer = sync;

        var task = authorizer.DecideAsync("op");
        task.IsCompletedSuccessfully.Should().BeTrue(
            "the default async body wraps the sync result in a completed ValueTask — no state machine, no allocation");

        var result = await task;
        result.Allowed.Should().BeTrue();
        sync.SyncCallCount.Should().Be(1, "the async default body invoked Decide exactly once");
    }

    [Fact]
    public async Task Authorizer_with_real_async_override_honours_cancellation_token()
    {
        // Pin the async-override path: an authorizer that overrides
        // DecideAsync with real async work sees the cancellation
        // token. Without this, an async decorator stack can't cancel
        // a slow license-server check-in.
        var authorizer = new AsyncCancellableAuthorizer();
        using var cts  = new CancellationTokenSource();
        cts.Cancel();

        var result = await authorizer.DecideAsync("op", cts.Token);

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("cancelled");
        result.Kind.Should().Be(DenialKind.Policy);
    }

    // ── Context conversion: Decision → ManagementResult ───────────────

    [Fact]
    public void Allow_decision_converts_to_no_failure_result_through_the_API_surface()
    {
        // The Allowed path: EnsureAuthorized returns null, the
        // mutating method runs, the API returns an Ok result.
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance);

        var apiResult = api.SetMinimumLevel("info");

        apiResult.Success.Should().BeTrue();
        apiResult.Kind.Should().BeNull("a successful result carries no denial kind");
    }

    [Fact]
    public void Deny_with_License_kind_flows_through_to_ManagementResult_Kind()
    {
        // The whole point of seams 5 and 6: an authorizer that
        // rejects with DenialKind.License must surface that kind on
        // the ManagementResult so the Dashboard switches on the
        // typed kind instead of parsing the message.
        var authorizer = new FixedDenialAuthorizer(
            AuthorizationDecision.Deny(
                reason: "license expired",
                kind:   DenialKind.License,
                facts:  new Dictionary<string, string> { ["product"] = "herald-enterprise" }));

        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, authorizer);

        var apiResult = api.SetMinimumLevel("info");

        apiResult.Success.Should().BeFalse();
        apiResult.Message.Should().Be("license expired");
        apiResult.Kind.Should().Be(DenialKind.License,
            "the typed kind must survive the EnsureAuthorized → ManagementResult.Fail conversion");
    }

    [Fact]
    public void Deny_with_null_reason_falls_back_to_operation_named_default()
    {
        // Pin the fallback path: an authorizer that returns a Deny
        // without a Reason still produces an operator-readable
        // message that names the operation. Without this, an
        // operator sees an empty error and can't diagnose anything.
        var authorizer = new FixedDenialAuthorizer(
            new AuthorizationDecision(false, Reason: null, Kind: DenialKind.Policy, Facts: null));

        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, authorizer);

        var apiResult = api.SetMinimumLevel("info");

        apiResult.Success.Should().BeFalse();
        apiResult.Message.Should().Contain("SetMinimumLevel",
            "the fallback message must name the operation so the operator can find the call site");
        apiResult.Kind.Should().Be(DenialKind.Policy,
            "the kind survives even when the reason is null");
    }

    // ── Test doubles ──────────────────────────────────────────────────

    private sealed class SyncOnlyCountingAuthorizer : IManagementApiAuthorizer
    {
        public int SyncCallCount;

        public AuthorizationDecision Decide(string operation)
        {
            SyncCallCount++;
            return AuthorizationDecision.Allow();
        }
    }

    private sealed class AsyncCancellableAuthorizer : IManagementApiAuthorizer
    {
        // Sync Decide is never the success path here — the override
        // honours cancellation. Pin the contract by returning Allow
        // synchronously so an unintended sync fallback would be a
        // test failure.
        public AuthorizationDecision Decide(string operation) => AuthorizationDecision.Allow();

        public ValueTask<AuthorizationDecision> DecideAsync(
            string operation,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<AuthorizationDecision>(
                    AuthorizationDecision.Deny("cancelled", DenialKind.Policy));
            }
            return new ValueTask<AuthorizationDecision>(AuthorizationDecision.Allow());
        }
    }

    private sealed class FixedDenialAuthorizer : IManagementApiAuthorizer
    {
        private readonly AuthorizationDecision _decision;
        public FixedDenialAuthorizer(AuthorizationDecision decision) => _decision = decision;
        public AuthorizationDecision Decide(string operation) => _decision;
    }
}
