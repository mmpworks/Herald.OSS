#nullable enable

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald;
using Xunit;

namespace MMP.Herald.OSS.Tests;

/// <summary>
/// Pins the edition install-hook semantics on <see cref="HeraldVersion"/>:
/// default tier, first-write-wins via <c>Interlocked.CompareExchange</c>,
/// argument validation, and thread-safe concurrent calls.
///
/// <para>
/// These tests mutate process-global state (<c>HeraldVersion.CurrentEdition</c>).
/// Each test resets via the internal <c>ResetForTesting()</c> entrypoint
/// before exercising the hook, and <see cref="IDisposable.Dispose"/> resets
/// after the test runs so a failure in the middle of a test does not leak
/// non-Community state into the next test in the same class. xUnit serialises
/// tests inside a single class instance, so the in-class resets are
/// sufficient within a class.
/// </para>
///
/// <para>
/// 0.7.0 update: joined to <see cref="MMP.Herald.OSS.Tests.Helpers.HeraldVersionCollection"/>
/// because the new capability-composition tests
/// (<c>HeraldCapabilityGateSmokeTests</c>) also mutate <see cref="HeraldVersion"/>
/// state. Without the collection join, the cross-class race manifests as
/// flaky <c>AllowsFeature</c> assertions on net10 (the cap-set gets wiped
/// by this class's <c>Dispose</c> mid-assert).
/// </para>
/// </summary>
[Collection(MMP.Herald.OSS.Tests.Helpers.HeraldVersionCollection.Name)]
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
    public void SetEdition_Null_ThrowsArgumentNullException()
    {
        Action act = () => HeraldVersion.SetEdition(null!);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("edition");
    }

    [Fact]
    public void SetEdition_Community_IsEffectivelyNoOp_AndLeavesSlotResettable()
    {
        // The Interlocked.CompareExchange(ref slot, edition, Community) shape
        // means writing Community when the slot already holds Community is a
        // no-op: the comparand matches, the new value matches, the slot is
        // unchanged. The slot stays at Community, so a subsequent Pro call
        // still wins the CAS. This pins that behaviour so a future refactor
        // that "tightens" SetEdition to reject Community does not silently
        // change the semantics.
        HeraldVersion.SetEdition(HeraldEdition.Community);
        HeraldVersion.SetEdition(HeraldEdition.Pro);

        HeraldVersion.CurrentEdition.Should().BeSameAs(HeraldEdition.Pro);
    }

    [Fact]
    public async Task SetEdition_IsThreadSafe_UnderConcurrentCalls()
    {
        // 32 contending workers behind a barrier × 50 iterations is real
        // contention. If a TOCTOU race exists between the read of
        // _currentEdition and the CompareExchange write, an intermittent
        // failure surfaces within ~10 runs. The previous shape
        // (Task.WhenAll over 3 plain Task.Run calls) passed by cooperative
        // scheduling, not by closing the race window.
        const int workerCount = 32;
        const int iterations = 50;
        var editions = new[] { HeraldEdition.Pro, HeraldEdition.Enterprise, HeraldEdition.Dev };

        for (int i = 0; i < iterations; i++)
        {
            HeraldVersion.ResetForTesting();
            using var barrier = new ManualResetEventSlim(false);
            var tasks = Enumerable.Range(0, workerCount).Select(w => Task.Run(() =>
            {
                barrier.Wait();
                HeraldVersion.SetEdition(editions[w % editions.Length]);
            })).ToArray();
            await Task.Delay(10);
            barrier.Set();
            await Task.WhenAll(tasks);

            HeraldVersion.CurrentEdition.Should().NotBeSameAs(HeraldEdition.Community);
            editions.Should().Contain(HeraldVersion.CurrentEdition);
        }
    }

    [Fact]
    public async Task CurrentEdition_StaleRead_ConvergesAfterSetEdition()
    {
        // Reader/writer convergence pin: a tight loop of readers observes
        // either the initial Community or the post-SetEdition Pro — never
        // any intermediate value. Pins the contract that CurrentEdition is
        // a simple field read with no observable torn state.
        using var readerCancel = new CancellationTokenSource();
        var observed = new ConcurrentBag<HeraldEdition>();
        var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            while (!readerCancel.IsCancellationRequested)
            {
                observed.Add(HeraldVersion.CurrentEdition);
            }
        })).ToArray();

        await Task.Delay(20);
        HeraldVersion.SetEdition(HeraldEdition.Pro);
        await Task.Delay(50);
        readerCancel.Cancel();
        await Task.WhenAll(readers);

        observed.Should().NotBeEmpty();
        observed.Should().OnlyContain(e =>
            ReferenceEquals(e, HeraldEdition.Community) ||
            ReferenceEquals(e, HeraldEdition.Pro));
    }
}
