// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using MMP.Herald.Quick;

namespace MMP.Herald.Diagnostics;

/// <summary>
/// Default-host aggregation hub for per-host runtime notices.
///
/// <para>
/// <b>The problem it solves.</b> Each <see cref="HeraldHost"/> owns an isolated
/// <see cref="HeraldRuntimeMessagesInstance"/>. Isolating the per-host BUFFERS is
/// what kills the parallel-test announcement-buffer bleed — but it would also
/// silently weaken operator observability: an operator who has always watched the
/// process-wide <see cref="HeraldRuntimeMessages.OnNotice"/> facade (which is the
/// default host's channel) would stop seeing notices published on a non-default
/// host. For the un-silenceable injection-refusal notice that is a real
/// regression: it becomes silenceable-by-constructing-your-own-host.
/// </para>
///
/// <para>
/// <b>The move.</b> Default observes; it does not collect. When a non-default host
/// is constructed it calls <see cref="Attach"/>, which subscribes the host's
/// channel <c>OnNotice</c> to re-raise each notice on the default host's
/// <c>OnNotice</c> via <see cref="HeraldRuntimeMessagesInstance.RaiseOnNoticeWithoutBuffering"/>.
/// The re-raise fires the default facade's subscribers but does NOT write the
/// default host's buffer. So:
/// </para>
/// <list type="bullet">
///   <item>Per-host BUFFERS stay isolated → the test-bleed dies (tests read buffers).</item>
///   <item>The default OnNotice AGGREGATES → operator observability survives
///         (operators subscribe to the live event).</item>
/// </list>
///
/// <para>
/// <b>Why there is no registry.</b> The subscription <i>is</i> the registration.
/// The host's channel holds a strong reference to the forwarding delegate; the
/// delegate captures only the (process-rooted) default host. Nothing here holds a
/// reference back to the host or its channel, so a short-lived host — every
/// parallel test builds one — is collected together with its channel and the
/// forwarding delegate. A central <c>ConcurrentDictionary</c> of channels would
/// have pinned every test host for the life of the process; we deliberately avoid
/// it. There is no per-host bookkeeping to race, and no leak to manage.
/// </para>
///
/// <para>
/// <b>No cycle.</b> Only non-default hosts attach. The default host never forwards
/// to itself (<see cref="HeraldHost"/> constructs its own channel with
/// aggregation OFF), so a notice re-raised onto the default channel does not loop
/// back into the aggregator. The default channel is the sink, never a source.
/// </para>
///
/// <para>
/// <b>Accepted, documented residuals.</b> (1) An operator who subscribes to BOTH a
/// specific host's <c>OnNotice</c> AND the default facade receives that host's
/// notices twice — once direct, once aggregated. Subscribe to one surface, not
/// both. (2) A default-facade subscriber that itself publishes back into a host
/// can drive subscriber-induced reentrancy on the publishing thread; that is a
/// subscriber bug, identical to any self-publishing event handler, not a hub
/// defect.
/// </para>
/// </summary>
internal static class RuntimeNoticeAggregator
{
    /// <summary>
    /// Wire <paramref name="hostChannel"/> (a non-default host's channel) to
    /// re-raise every notice it publishes onto the default host's
    /// <c>OnNotice</c>, without writing the default host's buffer. Idempotent
    /// per-channel is NOT guaranteed and not needed: each host constructs exactly
    /// one channel and attaches it exactly once at construction.
    /// </summary>
    public static void Attach(HeraldRuntimeMessagesInstance hostChannel)
    {
        ArgumentNullException.ThrowIfNull(hostChannel);

        // The forwarding subscription. Resolves HeraldHost.Default lazily on each
        // notice — by the time a notice can be published on a non-default host,
        // HeraldHost.Default has long since finished its static initialization, so
        // there is no chicken-and-egg here (the default host attaches nothing).
        hostChannel.OnNotice += notice =>
            HeraldHost.Default.RuntimeMessages.RaiseOnNoticeWithoutBuffering(notice);
    }
}
