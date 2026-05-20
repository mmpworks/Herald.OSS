#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using MMP.Herald;
using Xunit;

namespace MMP.Herald.OSS.Tests;

/// <summary>
/// Pins the shape and ergonomics of <see cref="CapabilityRequirement"/>:
/// implicit string conversion (T-CAP-28, T-CAP-57), value equality,
/// JSON wire shape with no Limit field (T-CAP-56).
/// </summary>
public sealed class CapabilityRequirementTests
{
    [Fact]
    public void Implicit_conversion_from_string_produces_record()
    {
        // T-CAP-28: the call site that writes `"multi-tenant"` gets a
        // CapabilityRequirement materialized by the compiler.
        CapabilityRequirement requirement = "multi-tenant";
        requirement.Name.Should().Be("multi-tenant");
    }

    [Fact]
    public void Collection_expression_with_string_literals_compiles()
    {
        // T-CAP-57: the ergonomic test — a plugin author writes the
        // RequiredCapabilities property as a collection-expression of
        // string literals; the implicit conversion fires per element.
        IReadOnlyList<CapabilityRequirement> caps =
            ["multi-tenant", "compliance-overlay"];

        caps.Should().HaveCount(2);
        caps[0].Name.Should().Be("multi-tenant");
        caps[1].Name.Should().Be("compliance-overlay");
    }

    [Fact]
    public void Implicit_form_equals_explicit_form()
    {
        CapabilityRequirement implicitForm = "multi-tenant";
        var explicitForm = new CapabilityRequirement("multi-tenant");

        implicitForm.Should().Be(explicitForm);
        implicitForm.GetHashCode().Should().Be(explicitForm.GetHashCode());
    }

    [Fact]
    public void Implicit_conversion_from_null_throws()
    {
        // Defensive: null in is unambiguously a programmer error. The
        // record itself doesn't validate (record positional params don't),
        // so the implicit conversion is the chokepoint.
        Action act = () =>
        {
            CapabilityRequirement r = (string)null!;
            _ = r;
        };

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToString_returns_the_capability_name()
    {
        new CapabilityRequirement("multi-tenant").ToString().Should().Be("multi-tenant");
    }

    [Fact]
    public void Default_System_Text_Json_serialization_has_only_the_Name_field()
    {
        // T-CAP-56: at the v1 wire format, the record carries Name only.
        // When a future per-cap Limit field lands additively, this test
        // is the one that confirms older verifiers continue to round-trip
        // without seeing an unknown field.
        var requirement = new CapabilityRequirement("multi-tenant");

        var json = JsonSerializer.Serialize(requirement);

        // The serialized JSON contains the capability name and NO "limit"
        // key. Shape may be `{"Name":"multi-tenant"}` (record default) or
        // a different default depending on options; the invariant is that
        // no Limit-shaped key appears.
        json.Should().Contain("multi-tenant");
        json.Should().NotContain("limit", because:
            "v1 wire shape MUST NOT include a Limit field — additive only");
    }
}
