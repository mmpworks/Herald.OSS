#nullable enable

using FluentAssertions;
using MMP.Herald.Configuration;
using Xunit;

namespace MMP.Herald.OSS.Tests.PipelineStrategyTests;

/// <summary>
/// Pins the public surface of <see cref="ComponentLifecycleState"/>:
/// two values, <c>Active</c> as the implicit default (zero).
/// The orchestrator that drives Active &lt;-&gt; Unsupported transitions
/// (per ADR-220) lives in <c>MMP.Herald.Licensing.Lifecycle</c>; here we
/// only pin the enum's shape.
/// </summary>
public sealed class ComponentLifecycleStateTests
{
    [Fact]
    public void Active_is_the_default_zero_value()
    {
        // Default(enum) == 0; Active must be the zero so a struct or
        // property that never initialises LifecycleState reports Active.
        default(ComponentLifecycleState).Should().Be(ComponentLifecycleState.Active);
        ((int)ComponentLifecycleState.Active).Should().Be(0);
    }

    [Fact]
    public void Unsupported_is_distinct_from_Active()
    {
        ComponentLifecycleState.Unsupported.Should().NotBe(ComponentLifecycleState.Active);
    }
}
