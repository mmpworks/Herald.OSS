#nullable enable

using System;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

namespace MMP.Herald.Bootstrap;

/// <summary>
/// Owns the bounded background drain of an evicted-pipeline resource.
/// Hot reload swaps a new <c>IAsyncDisposable</c> in; the old one needs
/// to dispose without blocking the foreground reload, and any failure
/// must surface through the optional failure sink instead of disappearing
/// into a swallowed catch.
///
/// <para>
/// The janitor's whole job is one bounded await + telemetry. Splitting
/// it out of <see cref="HotReloadableLoggingBootstrap"/> keeps the
/// bootstrap focused on reload orchestration and gives the disposal
/// path a single, independently-testable home.
/// </para>
/// </summary>
public sealed class OldResourceJanitor
{
    private readonly ILogFailureSink? _failureSink;
    private readonly TimeSpan _timeout;

    public OldResourceJanitor(ILogFailureSink? failureSink, TimeSpan timeout)
    {
        _failureSink = failureSink;
        _timeout = timeout;
    }

    /// <summary>
    /// Schedule a bounded dispose on the thread pool. Returns immediately —
    /// the new pipeline is already live, so this work must not block. A
    /// stuck dispose surfaces through the failure sink with the
    /// <c>OldPipelineDisposalTimeout</c> reason; an exception surfaces
    /// with <c>OldPipelineDisposalFailed</c>.
    /// </summary>
    public void Schedule(IAsyncDisposable oldResource)
    {
        ArgumentNullException.ThrowIfNull(oldResource);

        var timeout = _timeout;
        Task.Run(async () =>
        {
            try
            {
                var disposeTask = oldResource.DisposeAsync().AsTask();
                var completed = await Task.WhenAny(disposeTask, Task.Delay(timeout))
                    .ConfigureAwait(false);

                if (completed != disposeTask)
                {
                    Report(
                        new TimeoutException(
                            $"Old pipeline DisposeAsync did not complete within {timeout.TotalSeconds} seconds; abandoning."),
                        "OldPipelineDisposalTimeout");
                    return;
                }

                // Surface any captured exception.
                await disposeTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Report(ex, "OldPipelineDisposalFailed");
            }
        });
    }

    /// <summary>
    /// Surface a hot-reload failure on the supplied failure sink, naming
    /// the reason. Used by both this janitor and by the bootstrap for
    /// drain-cap and dispose-with-pending diagnostics so all hot-reload
    /// faults emit through one shape.
    /// </summary>
    public void Report(Exception ex, string reason)
    {
        ArgumentNullException.ThrowIfNull(ex);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        _failureSink?.ReportFailure(
            new LogEvent(
                DateTimeOffset.UtcNow,
                KnownLogLevels.Warning,
                LogCategory.App,
                "Hot reload old-pipeline disposal failed: {Reason}",
                $"Hot reload old-pipeline disposal failed: {reason}",
                LogEvent.EmptyProperties,
                LogEvent.EmptyContext),
            ex,
            reason);
    }
}
