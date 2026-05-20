#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Configuration;
using MMP.Herald.Pipeline;
using MMP.Herald.Routing;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Pins the cap-surface defaults on <see cref="IComponentMetadata"/>:
/// <see cref="IComponentMetadata.RequiredCapabilities"/> empty by default
/// (T-CAP-7 portion), <see cref="IComponentMetadata.LifecycleState"/>
/// defaults to <see cref="ComponentLifecycleState.Active"/> (T-CAP-50),
/// existing implementers stay source-compatible.
/// </summary>
public sealed class ComponentMetadataCapabilityTests
{
    [Fact]
    public void Default_RequiredCapabilities_is_empty()
    {
        IComponentMetadata component = new MinimalComponent();

        component.RequiredCapabilities.Should().BeEmpty();
    }

    [Fact]
    public void Default_LifecycleState_is_Active()
    {
        // T-CAP-50: a component declared without LifecycleState must
        // report Active so existing paid binaries do not surface as
        // Unsupported after the cap-surface upgrade.
        IComponentMetadata component = new MinimalComponent();

        component.LifecycleState.Should().Be(ComponentLifecycleState.Active);
    }

    [Fact]
    public void Component_can_override_RequiredCapabilities_via_collection_expression()
    {
        IComponentMetadata component = new CapAwareComponent();

        component.RequiredCapabilities.Should().HaveCount(2);
        component.RequiredCapabilities[0].Name.Should().Be("multi-tenant");
        component.RequiredCapabilities[1].Name.Should().Be("compliance-overlay");
    }

    [Fact]
    public void Default_MinimumEdition_unchanged_after_cap_surface()
    {
        // Sibling additions must not change the existing default.
        IComponentMetadata component = new MinimalComponent();

        component.MinimumEdition.Should().BeSameAs(HeraldEdition.Community);
    }

    private sealed class MinimalComponent : IComponentMetadata
    {
        public string ComponentName => "minimal";
        public string DisplayName => "Minimal";
        public string Description => "";
        public string Help => "";
        public VendorInfo Vendor => VendorInfo.MMP;
        public IReadOnlyList<SinkConfigField> ConfigurationSchema => [];
    }

    private sealed class CapAwareComponent : IComponentMetadata
    {
        public string ComponentName => "cap-aware";
        public string DisplayName => "Cap Aware";
        public string Description => "";
        public string Help => "";
        public VendorInfo Vendor => VendorInfo.MMP;
        public IReadOnlyList<SinkConfigField> ConfigurationSchema => [];
        public IReadOnlyList<CapabilityRequirement> RequiredCapabilities =>
            ["multi-tenant", "compliance-overlay"];
    }
}
