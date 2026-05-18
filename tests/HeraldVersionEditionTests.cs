#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald;
using Xunit;

namespace MMP.Herald.OSS.Tests;

/// <summary>
/// Pins the edition install-hook semantics on <see cref="HeraldVersion"/>:
/// default tier, first-write-wins via <c>Interlocked.CompareExchange</c>,
/// and thread-safe concurrent calls.
///
/// <para>
/// These tests mutate process-global state (<c>HeraldVersion.CurrentEdition</c>).
/// Each test resets via the internal <c>ResetForTesting()</c> entrypoint
/// before exercising the hook, and <see cref="IDisposable.Dispose"/> resets
/// after the test runs so a failure in the middle of a test does not leak
/// non-Community state into the next test in the same class. xUnit serialises
/// tests inside a single class instance, so the in-class resets are
/// sufficient without a collection fixture.
/// </para>
/// </summary>
public sealed class HeraldVersionEditionTests : IDisposable
{
    public HeraldVersionEditionTests()
    {
        HeraldVersion.ResetForTesting();
    }

    public void Dispose()
    {
        HeraldVersion.ResetForTesting();
    }

    [Fact]
    public void HeraldVersion_CurrentEdition_DefaultsTo_Community()
    {
        HeraldVersion.CurrentEdition.Should().BeSameAs(HeraldEdition.Community);
    }

    [Fact]
    public void SetEdition_FirstCallWins_SubsequentCalls_NoOp()
    {
        HeraldVersion.SetEdition(HeraldEdition.Pro);
        HeraldVersion.SetEdition(HeraldEdition.Enterprise);

        HeraldVersion.CurrentEdition.Should().BeSameAs(HeraldEdition.Pro);
    }

    [Fact]
    public async Task SetEdition_IsThreadSafe_UnderConcurrentCalls()
    {
        var editions = new[] { HeraldEdition.Pro, HeraldEdition.Enterprise, HeraldEdition.Dev };

        await Task.WhenAll(editions.Select(e => Task.Run(() => HeraldVersion.SetEdition(e))));

        // Exactly one of the three concurrent calls won the CAS — the rest were no-ops.
        HeraldVersion.CurrentEdition.Should().NotBeSameAs(HeraldEdition.Community);
        editions.Should().Contain(HeraldVersion.CurrentEdition);
    }
}
