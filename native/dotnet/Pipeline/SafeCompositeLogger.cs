#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Routing;

namespace MMP.Herald.Pipeline;
/// <summary>
/// Fan-out logger that isolates sink failures so one bad destination
/// does not block the others. Children are swappable through
/// <see cref="SwapChildren"/> so a sinks-only hot reload can replace the
/// fan-out targets without rebuilding the whole pipeline. Reads of the
/// children list go through <see cref="Volatile.Read{T}"/> so a swap
/// in flight cannot tear iteration on a concurrent <c>Log</c> call.
/// </summary>
public sealed class SafeCompositeLogger : ILogger, IDescribable, IComponentMetadata, ISinkChainLevelIntrospection
{
    // Volatile-published so a concurrent Log call captures a coherent
    // snapshot at method entry. The list reference itself is treated as
    // immutable once published — SwapChildren replaces it; readers never
    // mutate it. Hot-path cost on x86/x64 is one regular load (the
    // memory model already guarantees release-acquire on reference
    // assignments); on ARM it inserts an acquire barrier on each read.
    // Acceptable because the foreach iterates the captured local, not
    // the field.
    private IReadOnlyList<ILogger> _children;
    private readonly ILogFailureSink _failureSink;

    public SafeCompositeLogger(
        IReadOnlyList<ILogger> children,
        ILogFailureSink? failureSink = null)
    {
        _children = children;
        _failureSink = failureSink ?? NullLogFailureSink.Instance;
    }

    /// <summary>
    /// Atomically replace the fan-out children. Used by the sinks-only
    /// hot-reload path to point the live fan-out at freshly-built sink
    /// writers without rebuilding the rest of the pipeline. Returns the
    /// previous children list so the caller can dispose them through the
    /// hot-reload janitor.
    ///
    /// <para>
    /// Concurrency: the swap is a single reference write through
    /// <see cref="Volatile.Write{T}"/>. Concurrent <c>Log</c> calls
    /// capture either the old or the new list at method entry; no
    /// caller observes a torn state. Events in flight against the old
    /// list complete against the old sinks; events that started after
    /// the swap go to the new sinks.
    /// </para>
    /// </summary>
    public IReadOnlyList<ILogger> SwapChildren(IReadOnlyList<ILogger> newChildren)
    {
        ArgumentNullException.ThrowIfNull(newChildren);
        var old = Volatile.Read(ref _children);
        Volatile.Write(ref _children, newChildren);
        return old;
    }

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var children = Volatile.Read(ref _children);
        AuditLogFailureException? auditFailure = null;

        foreach (var child in children)
        {
            try
            {
                child.Log(logEvent);
            }
            catch (Exception exception)
            {
                HandleChildException(child, exception, logEvent, ref auditFailure);
            }
        }

        if (auditFailure is not null)
        {
            throw auditFailure;
        }
    }

    // Fan-out stays sequential on LogAsync so failure ordering and audit
    // propagation match the sync path exactly. The win is that awaiting a
    // slow network sink yields the worker thread instead of pinning it —
    // which is the whole point of the async path.
    //
    // OperationCanceledException is the one case the async path diverges
    // from the sync path: a caller's CancellationToken must propagate up
    // rather than be reported as a sink failure.
    public async ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var children = Volatile.Read(ref _children);
        AuditLogFailureException? auditFailure = null;

        foreach (var child in children)
        {
            try
            {
                await child.LogAsync(logEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                HandleChildException(child, exception, logEvent, ref auditFailure);
            }
        }

        if (auditFailure is not null)
        {
            throw auditFailure;
        }
    }

    // Centralises the two-branch catch shape (audit vs. general) so the
    // sync and async loops differ only where they must (the call itself
    // and the async-only OperationCanceledException rethrow). Called from
    // a catch block — stays allocation-free by taking the exception by
    // value and the audit slot by ref.
    private void HandleChildException(
        ILogger child, Exception exception, LogEvent logEvent,
        ref AuditLogFailureException? auditFailure)
    {
        if (exception is AuditLogFailureException audit)
        {
            auditFailure ??= audit;
            return;
        }
        _failureSink.ReportFailure(logEvent, exception, child.GetType().Name);
    }

    public string Describe() => $"SafeCompositeLogger({Volatile.Read(ref _children).Count} sinks)";

    // Aggregate by taking the minimum child floor. A single child that is
    // not aware or reports null means "unknown", so we keep the pipeline
    // floor at the policy minimum rather than guess.
    public LogLevel? GetMinimumLevel(ILogLevelRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var children = Volatile.Read(ref _children);
        if (children.Count == 0) return null;

        LogLevel? aggregated = null;
        foreach (var child in children)
        {
            if (child is not ISinkChainLevelIntrospection aware) return null;

            var childMin = aware.GetMinimumLevel(registry);
            if (childMin is null) return null;

            if (aggregated is null || registry.IsAtOrAbove(aggregated, childMin))
            {
                aggregated = childMin;
            }
        }
        return aggregated;
    }

    // -- Inspection --

    /// <summary>The child loggers this composite fans out to.</summary>
    public IReadOnlyList<ILogger> Children => Volatile.Read(ref _children);

    /// <summary>Number of child sinks.</summary>
    public int ChildCount => Volatile.Read(ref _children).Count;

    // -- IComponentMetadata --
    internal static readonly Configuration.PipelineStepRules StepRules = new(
        OptimalPosition: ["last"]);
    string IComponentMetadata.ComponentName => "fanOut";
    string IComponentMetadata.DisplayName => "Fan-Out (Sinks)";
    string IComponentMetadata.Description => "Distributes events to all configured sinks with failure isolation.";
    string IComponentMetadata.Help => "Delivers each event to all child sinks. One sink failing doesn't prevent others from receiving the event. AuditLogFailureException always propagates. Always the terminal step in the pipeline.";
    VendorInfo IComponentMetadata.Vendor => VendorInfo.MMP;
    Configuration.PipelineStepRules IComponentMetadata.Rules => StepRules;
    internal static readonly System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> DefaultSchema =
    [
        Routing.SinkConfigField.Int("sinkCount", 0, "Number of child sinks",
            "How many sinks are registered under this Fan-Out. Each event is delivered to all sinks in parallel. One sink failing does not prevent others from receiving the event."),
    ];
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> IComponentMetadata.ConfigurationSchema =>
    [
        DefaultSchema[0] with { DefaultValue = Volatile.Read(ref _children).Count },
    ];
}