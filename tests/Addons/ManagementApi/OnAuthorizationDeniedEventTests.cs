#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Pins the ADR-001 Seam 2 contract:
/// <see cref="HeraldManagementApi.OnAuthorizationDenied"/> fires on
/// every denied operation with the operation name and the exact
/// <see cref="AuthorizationDecision"/> instance the authorizer
/// returned. The event is observation-only — subscribers can audit,
/// alert, and correlate, but can't change the decision.
///
/// <para>
/// Tests cover: event fires on denial with the right payload, does
/// NOT fire on success, multiple subscribers all receive the event,
/// and a throwing subscriber doesn't break delivery to other
/// subscribers (mirrors the runtime-notice channel's existing
/// isolation contract).
/// </para>
/// </summary>
public sealed class OnAuthorizationDeniedEventTests
{
    [Fact]
    public void Event_fires_on_denial_with_operation_and_decision()
    {
        // Pin the core contract: a denied mutating call produces
        // exactly one event with the operation name and the
        // authorizer's decision unchanged.
        var captured = new List<(string Op, AuthorizationDecision Decision)>();
        var authorizer = new FixedDenialAuthorizer(
            AuthorizationDecision.Deny("blocked", DenialKind.Policy));

        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, authorizer);
        api.OnAuthorizationDenied += (op, decision) => captured.Add((op, decision));

        api.SetMinimumLevel("info");

        captured.Should().HaveCount(1, "exactly one event for one denied call");
        captured[0].Op.Should().Be("SetMinimumLevel");
        captured[0].Decision.Allowed.Should().BeFalse();
        captured[0].Decision.Reason.Should().Be("blocked");
        captured[0].Decision.Kind.Should().Be(DenialKind.Policy);
    }

    [Fact]
    public void Event_does_not_fire_on_successful_operation()
    {
        // Pin the no-false-positive contract: a successful call
        // must not produce an event. Without this, an audit log
        // would record every operation as a denial — useless for
        // forensics.
        var captured = new List<string>();
        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, AllowAllAuthorizer.Instance);
        api.OnAuthorizationDenied += (op, _) => captured.Add(op);

        api.SetMinimumLevel("info").Success.Should().BeTrue();

        captured.Should().BeEmpty("a successful operation does not fire the denial event");
    }

    [Fact]
    public void Multiple_subscribers_all_receive_the_event()
    {
        // Pin the multi-cast contract: audit, SIEM, dashboard all
        // listen at once. Each gets its own callback with the same
        // payload.
        var sub1 = new List<string>();
        var sub2 = new List<string>();
        var sub3 = new List<string>();
        var authorizer = new FixedDenialAuthorizer(
            AuthorizationDecision.Deny("nope", DenialKind.Auth));

        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, authorizer);
        api.OnAuthorizationDenied += (op, _) => sub1.Add(op);
        api.OnAuthorizationDenied += (op, _) => sub2.Add(op);
        api.OnAuthorizationDenied += (op, _) => sub3.Add(op);

        api.SetMinimumLevel("info");

        sub1.Should().Equal(new[] { "SetMinimumLevel" });
        sub2.Should().Equal(new[] { "SetMinimumLevel" });
        sub3.Should().Equal(new[] { "SetMinimumLevel" });
    }

    [Fact]
    public void Throwing_subscriber_does_not_break_delivery_to_other_subscribers()
    {
        // The subscriber-isolation contract: a faulty audit hook
        // must not suppress delivery to a healthy SIEM hook. The
        // dispatch swallows exceptions per-subscriber so an early
        // failure can't blackhole the rest of the invocation list.
        var sawOk = false;
        var sawAfter = false;
        var authorizer = new FixedDenialAuthorizer(
            AuthorizationDecision.Deny("nope", DenialKind.Auth));

        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, authorizer);
        // Order matters: a throwing subscriber registered first
        // proves the dispatcher keeps going. A second subscriber
        // registered after the throwing one must still receive
        // the event.
        api.OnAuthorizationDenied += (_, _) => sawOk = true;
        api.OnAuthorizationDenied += (_, _) => throw new System.InvalidOperationException("boom");
        api.OnAuthorizationDenied += (_, _) => sawAfter = true;

        // The mutating call must still surface a Fail result —
        // the event-isolation guard runs inside the API and the
        // failure-return path is unaffected.
        var apiResult = api.SetMinimumLevel("info");
        apiResult.Success.Should().BeFalse();

        sawOk.Should().BeTrue("the first subscriber must receive the event");
        sawAfter.Should().BeTrue(
            "a throwing subscriber must not break delivery to subsequent subscribers");
    }

    [Fact]
    public void Event_decision_carries_facts_dictionary_through_to_subscribers()
    {
        // Structured-context contract for the license-failure UX:
        // the Facts bag (customerId, expiry, product, etc.) must
        // arrive at the subscriber by reference — the whole point
        // of the typed shape is to skip the string-parse hack.
        var facts = new Dictionary<string, string>
        {
            ["customerId"] = "cust_123",
            ["product"]    = "herald-enterprise",
        };
        IReadOnlyDictionary<string, string>? capturedFacts = null;
        var authorizer = new FixedDenialAuthorizer(
            AuthorizationDecision.Deny("license expired", DenialKind.License, facts));

        var builder = QuickLogBuilder.Create().WithConsoleSink();
        var result  = builder.BuildAndCommit();
        var api     = new HeraldManagementApi(builder, result, authorizer);
        api.OnAuthorizationDenied += (_, decision) => capturedFacts = decision.Facts;

        api.SetMinimumLevel("info");

        capturedFacts.Should().BeSameAs(facts,
            "the facts dictionary must arrive by reference so no string-parsing is required");
    }

    private sealed class FixedDenialAuthorizer : IManagementApiAuthorizer
    {
        private readonly AuthorizationDecision _decision;
        public FixedDenialAuthorizer(AuthorizationDecision decision) => _decision = decision;
        public AuthorizationDecision Decide(string operation) => _decision;
    }
}
