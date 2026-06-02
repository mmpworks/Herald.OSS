#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using MMP.Herald.Diagnostics;
using MMP.Herald.Events;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline;

/// <summary>
/// External-event-injection consent enforcement for the public
/// <c>StructuredLogger.Log(LogEvent)</c> port. See the ADR at
/// <c>docs/design/external-event-injection-switch.md</c>.
///
/// <para>
/// The consent <i>decision</i> lives in the main partial (the
/// <c>_allowExternalEventInjection</c> field, set at construction and read at
/// the <c>Log(LogEvent)</c> entry). This file holds the OFF-path
/// <i>behaviour</i>: a loud, non-throwing, one-shot refusal notice. Splitting
/// it out keeps the hot core file focused and makes the security-relevant
/// behaviour reviewable in one place.
/// </para>
/// </summary>
public sealed partial class StructuredLogger
{
    // The runtime-notice source token for the OFF-path refusal. Matches the
    // @herald.runtime.<topic> convention the naming-policy and hot-reload
    // notices already use, so an operator subscribing to HeraldRuntimeMessages
    // sees injection refusals on the same channel with a greppable source.
    private const string ExternalInjectionNoticeSource = "@herald.runtime.external-event-injection";

    /// <summary>
    /// Internal forward for a pre-built event that Herald itself routes into
    /// this logger (e.g. a Serilog <c>WriteTo.Logger</c> / <c>WriteTo.Map</c>
    /// sub-pipeline re-feeding an event it already built and enriched). This is
    /// NOT external injection: the event originated inside a Herald pipeline and
    /// is being handed to a sibling Herald pipeline, so it must bypass the
    /// consent gate the public <c>Log(LogEvent)</c> port applies to external
    /// callers. Keeping a distinct internal entry point preserves the ADR's
    /// entry-point discrimination — external callers hit the consent boundary,
    /// Herald's own forwarders do not. See
    /// docs/design/external-event-injection-switch.md (section 7.1).
    /// </summary>
    internal void ForwardPrebuilt(LogEvent logEvent) => _pipeline.Log(logEvent);

    /// <summary>
    /// OFF-path behaviour for <c>Log(LogEvent)</c>: drop the injected event and
    /// emit a mandatory, un-silenceable runtime notice on the FIRST such call
    /// through this logger. Never throws — a log call is not a crash site.
    ///
    /// <para>
    /// The notice names the call site (when the compiler captured it), the
    /// protections the injected event would have skipped (redaction first, then
    /// factory stamping, enrichment, and template rendering), and the
    /// <c>AllowExternalEventInjection()</c> opt-in. One-shot per logger via an
    /// <see cref="Interlocked.Exchange(ref int, int)"/> latch so a hot loop that
    /// injects on every iteration does not flood the channel.
    /// </para>
    /// </summary>
    private void RefuseExternalInjection(string? callerMember, string? callerFile, int callerLine)
    {
        // One-shot: only the first off-path injection through this logger
        // publishes. Interlocked.Exchange returns the PRIOR value; a non-zero
        // prior means another call already fired the notice, so this one is a
        // silent drop. The event is dropped either way — the latch only gates
        // the notice, not the refusal.
        if (Interlocked.Exchange(ref _externalInjectionNoticeFired, 1) != 0)
        {
            return;
        }

        var callSite = DescribeCallSite(callerMember, callerFile, callerLine);

        var message =
            "Herald dropped a hand-built event injected via Log(LogEvent) from " +
            callSite +
            ". External event injection is OFF by default. An injected event bypasses " +
            "Herald's protections: redaction (so an injected event can leak an unredacted " +
            "secret or PII), plus time/scope/tenant stamping, enrichment, and template " +
            "rendering. To allow injection — which moves responsibility for vetting the " +
            "event to your application — call AllowExternalEventInjection() on the builder. " +
            "Otherwise use the typed surface (Information/Warning/... or the typed-args Log) " +
            "so the pipeline builds and protects the event for you.";

        var properties = new List<LogProperty>(4)
        {
            new("callSite", callSite),
            new("bypassedProtections", "redaction,timeScopeTenantStamping,enrichment,templateRendering"),
            new("optIn", "AllowExternalEventInjection()"),
            new("protectedPath", "Information/Warning/Error/Debug/Verbose/Fatal or typed-args Log"),
        };

        // Warning severity: a dropped event is a real problem the operator must
        // see, but the log call itself was honoured as a no-op — not an Error
        // (which a subscriber may page on). The notice is un-silenceable: there
        // is no suppression knob on this path, by design.
        HeraldRuntimeMessages.Publish(
            ExternalInjectionNoticeSource,
            message,
            NoticeSeverity.Warning,
            properties);
    }

    // Build a human-readable call-site string from the optional caller info.
    // Direct call sites supply member/file/line via the compiler; calls made
    // through the ILogger interface reference defaulted them, so we fall back
    // to a clear "(call site unavailable...)" marker that still tells the
    // operator what happened and why the precise location is missing.
    private static string DescribeCallSite(string? callerMember, string? callerFile, int callerLine)
    {
        if (string.IsNullOrEmpty(callerFile) && string.IsNullOrEmpty(callerMember))
        {
            return "(call site unavailable — injected through an ILogger reference)";
        }

        var fileName = string.IsNullOrEmpty(callerFile)
            ? "(unknown file)"
            : System.IO.Path.GetFileName(callerFile);
        var member = string.IsNullOrEmpty(callerMember) ? "(unknown member)" : callerMember;

        return $"{member} at {fileName}:{callerLine}";
    }
}
