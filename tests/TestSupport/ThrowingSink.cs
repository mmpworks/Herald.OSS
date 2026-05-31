#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Events;

namespace MMP.Herald.OSS.Tests.TestSupport;

/// <summary>
/// A throw-on-cue ILogger sink that counts every call.
///
/// Used by tests that verify failure-isolation contracts: a throwing sink must
/// not block peer sinks in a fan-out, and the kernel / safe-composite wrapper
/// must route the exception to a diagnostic sink rather than propagating it.
///
/// ThrowingSink implements the full ILogger interface, including the default-
/// implemented LogAsync. Both paths throw so async-drain tests are covered.
/// </summary>
public sealed class ThrowingSink : MMP.Herald.ILogger
{
    private int _calls;

    /// <summary>Total invocations of Log() and LogAsync() combined.</summary>
    public int Calls => Volatile.Read(ref _calls);

    private readonly Exception _toThrow;

    public ThrowingSink(Exception? toThrow = null)
    {
        // Default exception gives a readable message in test output without
        // hiding the stack that reaches the throw site.
        _toThrow = toThrow ?? new InvalidOperationException("ThrowingSink intentionally threw");
    }

    /// <inheritdoc/>
    public void Log(LogEvent logEvent)
    {
        Interlocked.Increment(ref _calls);
        throw _toThrow;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ILogger.LogAsync has a default implementation that calls Log(), so this
    /// override is technically not required. It is provided explicitly so the
    /// call count increments once per LogAsync invocation rather than twice
    /// (once for LogAsync and once for the default-impl's Log() call).
    /// </remarks>
    public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        throw _toThrow;
    }
}
