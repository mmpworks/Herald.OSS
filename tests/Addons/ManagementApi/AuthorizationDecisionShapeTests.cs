#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Pin the shape of the structured authorization primitives shipped
/// in ADR-001 seam-set v2 (seams 5, 6, 7 foundation):
/// <see cref="DenialKind"/>, <see cref="AuthorizationDecision"/>,
/// <see cref="LicenseStatusSnapshot"/>, and the
/// <see cref="ManagementResult.Fail(string, DenialKind?)"/> optional
/// kind parameter.
///
/// <para>
/// These types flow unchanged through the authorizer → context →
/// result → renderer chain. If the shapes drift, every downstream
/// consumer (Dashboard, SIEM, audit log) breaks at once — so the
/// shape gets pinned here before any consumer wires up.
/// </para>
/// </summary>
public sealed class AuthorizationDecisionShapeTests
{
    [Fact]
    public void DenialKind_built_in_instances_have_stable_names()
    {
        // The Name string is the wire shape. Renderers that don't have
        // the static-instance reference (different assembly, JSON
        // round-trip, Dashboard renderer) compare against these
        // literals. If a name changes, every renderer breaks silently.
        DenialKind.Auth.Name.Should().Be("auth");
        DenialKind.License.Name.Should().Be("license");
        DenialKind.Policy.Name.Should().Be("policy");

        DenialKind.Auth.ToString().Should().Be("auth");
        DenialKind.License.ToString().Should().Be("license");
        DenialKind.Policy.ToString().Should().Be("policy");
    }

    [Fact]
    public void DenialKind_equality_is_by_name_not_reference()
    {
        // Sealed record equality is by field. A renderer that builds a
        // DenialKind from a deserialised string ("license") and the
        // built-in DenialKind.License must compare equal so the
        // renderer's switch hits the right arm.
        var rebuilt = new DenialKind("license");
        rebuilt.Should().Be(DenialKind.License);
        rebuilt.Should().NotBeSameAs(DenialKind.License);
    }

    [Fact]
    public void AuthorizationDecision_Allow_factory_produces_empty_denial_fields()
    {
        // The happy-path constructor. Reason / Kind / Facts are null
        // on success — pinning this so an authorizer that returns
        // Allow() can't accidentally leak a non-null Kind into a
        // success path.
        var decision = AuthorizationDecision.Allow();

        decision.Allowed.Should().BeTrue();
        decision.Reason.Should().BeNull();
        decision.Kind.Should().BeNull();
        decision.Facts.Should().BeNull();
    }

    [Fact]
    public void AuthorizationDecision_Deny_factory_defaults_kind_to_Auth()
    {
        // Most rejections are auth rejections. The factory default
        // matches the most common shape so a custom authorizer that
        // calls Deny("nope") gets the expected category without
        // having to remember to pass it.
        var decision = AuthorizationDecision.Deny("nope");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("nope");
        decision.Kind.Should().Be(DenialKind.Auth);
        decision.Facts.Should().BeNull();
    }

    [Fact]
    public void AuthorizationDecision_Deny_carries_kind_and_facts_through_unchanged()
    {
        // The whole point of the structured shape: the license gate's
        // (customerId, expiry, product, accept-set) survives the trip
        // from the authorizer to the renderer without a string-parse
        // hack. Pin the round-trip.
        var facts = new Dictionary<string, string>
        {
            ["customerId"] = "cust_123",
            ["expiry"]     = "2099-01-01",
            ["product"]    = "herald-enterprise",
        };

        var decision = AuthorizationDecision.Deny("license expired", DenialKind.License, facts);

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("license expired");
        decision.Kind.Should().Be(DenialKind.License);
        decision.Facts.Should().BeSameAs(facts,
            "the facts dictionary is passed by reference — copying would defeat the structured-context win");
    }

    [Fact]
    public void AuthorizationDecision_direct_construction_matches_factory_shape()
    {
        // record struct supports value equality, so a direct
        // construction and a factory call with the same arguments
        // must compare equal. This is the contract the Dashboard
        // depends on when it deserialises a JSON denial.
        var direct  = new AuthorizationDecision(false, "blocked", DenialKind.Policy, null);
        var factory = AuthorizationDecision.Deny("blocked", DenialKind.Policy);

        direct.Should().Be(factory);
    }

    [Fact]
    public void LicenseStatusSnapshot_round_trips_through_value_equality()
    {
        // record struct equality pinned for the Dashboard JSON path:
        // two snapshots built from the same fields must compare
        // equal, so a renderer that caches the previous snapshot can
        // skip a re-render when nothing changed.
        var a = new LicenseStatusSnapshot(
            Kind: "valid",
            Edition: "Enterprise",
            ExpiresAt: new System.DateTimeOffset(2099, 1, 1, 0, 0, 0, System.TimeSpan.Zero),
            Reason: null);
        var b = new LicenseStatusSnapshot(
            Kind: "valid",
            Edition: "Enterprise",
            ExpiresAt: new System.DateTimeOffset(2099, 1, 1, 0, 0, 0, System.TimeSpan.Zero),
            Reason: null);

        a.Should().Be(b);
    }

    [Fact]
    public void LicenseStatusSnapshot_allows_null_edition_expiry_and_reason()
    {
        // The "missing" state — no edition, no expiry, no detail —
        // must be representable without optional-field gymnastics.
        // Pin the all-null construction so a host that has nothing
        // to report can still publish a snapshot.
        var missing = new LicenseStatusSnapshot(
            Kind: "missing",
            Edition: null,
            ExpiresAt: null,
            Reason: null);

        missing.Kind.Should().Be("missing");
        missing.Edition.Should().BeNull();
        missing.ExpiresAt.Should().BeNull();
        missing.Reason.Should().BeNull();
    }

    [Fact]
    public void ManagementResult_Fail_without_kind_yields_null_kind_for_legacy_callers()
    {
        // The existing 100+ Fail("...") call sites stay
        // source-compatible. Pin the default-null kind so a future
        // refactor doesn't silently promote them to a non-null kind
        // and break a renderer's "if Kind is null, hide the badge"
        // rule.
        var result = ManagementResult.Fail("something went wrong");

        result.Success.Should().BeFalse();
        result.Message.Should().Be("something went wrong");
        result.Kind.Should().BeNull();
    }

    [Fact]
    public void ManagementResult_Fail_with_kind_carries_it_through()
    {
        // The new shape: when an authorizer rejects with a known
        // kind, the kind survives into the result so the Dashboard
        // can render a "license" badge instead of an "error" badge.
        var result = ManagementResult.Fail("license expired", DenialKind.License);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("license expired");
        result.Kind.Should().Be(DenialKind.License);
    }

    [Fact]
    public void ManagementResult_Ok_never_carries_a_kind()
    {
        // Ok() doesn't take a kind — success has no denial category.
        // Pin the null so a future "ok with diagnostic kind"
        // extension doesn't silently break the "Kind != null ⇒
        // denial" invariant renderers depend on.
        var result = ManagementResult.Ok("done");

        result.Success.Should().BeTrue();
        result.Kind.Should().BeNull();
    }
}
