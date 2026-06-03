#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.OSS.Tests.Infrastructure;

/// <summary>
/// Serilog-side <see cref="ILogEventSink"/> that throws on cue and records every call.
///
/// <para>
/// Used by S9 tests (G-SEC.2/3) to verify that <c>WriteTo.Sink</c> swallows failures
/// while <c>AuditTo.Sink</c> propagates them. Also used by S7-SelfLog (G-GAP.4) and
/// hot-path failure isolation tests.
/// </para>
///
/// <para>
/// <see cref="ThrowOnEmit"/> defaults to <see langword="false"/> so the sink can record
/// events without throwing. Set it to <see langword="true"/> before the call under test
/// to engage the throw path. Events are always appended to <see cref="Received"/> before
/// any exception is thrown so the test can assert "the sink was reached."
/// </para>
/// </summary>
public sealed class ThrowingSink : ILogEventSink
{
    private int _calls;

    /// <summary>
    /// When <see langword="true"/>, <see cref="Emit"/> throws after recording the event.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnEmit { get; set; }

    /// <summary>Optional custom exception. Defaults to <see cref="InvalidOperationException"/>.</summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>All events received by <see cref="Emit"/>, regardless of whether a throw followed.</summary>
    public List<LogEvent> Received { get; } = new();

    /// <summary>Total number of <see cref="Emit"/> invocations.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <inheritdoc/>
    public void Emit(LogEvent logEvent)
    {
        Interlocked.Increment(ref _calls);
        Received.Add(logEvent);

        if (ThrowOnEmit)
        {
            throw ExceptionToThrow
                  ?? new InvalidOperationException("ThrowingSink intentionally threw");
        }
    }
}

