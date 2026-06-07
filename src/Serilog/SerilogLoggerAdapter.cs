#nullable enable

using System;
using System.Threading.Tasks;
using MMP.Herald.Adapters;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog;

/// <summary>
/// Bridges the Serilog-shaped ILogger surface to a Herald StructuredLogger
/// pipeline. Implements IDisposable and IAsyncDisposable so callers that want
/// explicit scope management can use a using / await using block.
///
/// <para>
/// The slow params-array path is a thin level-and-verb skin over
/// <see cref="HeraldAdapterCore"/> — the shared engine that owns template
/// parsing, property projection, redaction, exception context, ForContext merge,
/// and the FromBuild ownership lifecycle. NLog and log4net have sibling skins
/// over the same core.
/// </para>
///
/// <para>
/// The <c>_herald</c> field is retained because the SerilogArityGenerator emits a
/// partial half (SerilogLoggerAdapter.Holes.Generated.cs) of typed-args 1..16
/// zero-alloc overloads that dispatch through <c>_herald.LogCompact</c> /
/// <c>_herald.Is{Level}Acceptable</c> directly. That generated fast path is the
/// zero-alloc band and must keep its direct field access; the core wraps the same
/// StructuredLogger instance for the slow path. One shared reference, two routes.
/// </para>
/// </summary>
public sealed partial class SerilogLoggerAdapter : ILogger, IDisposable, IAsyncDisposable
{
    // Retained for the SerilogArityGenerator-emitted typed-args fast path
    // (partial half). The generated overloads read _herald.Is{Level}Acceptable
    // and call _herald.LogCompact(...) directly — that direct access is the
    // zero-alloc route and must not be indirected through the core.
    private readonly StructuredLogger _herald;

    // Slow-path engine. Owns template parsing, property projection, redaction,
    // exception context, ForContext merge, and the FromBuild lifecycle. Wraps the
    // same StructuredLogger instance as _herald on the construction path; owns the
    // lifetime on the FromBuild path.
    private readonly HeraldAdapterCore _core;

    /// <summary>
    /// The underlying Herald StructuredLogger wrapped by this adapter. Internal:
    /// used by MMP.Herald.Serilog.AspNetCore to register the raw logger with MEL.
    /// </summary>
    internal StructuredLogger HeraldLogger => _herald;

    /// <summary>
    /// Wrap herald in a Serilog-shaped adapter. The adapter does NOT own the
    /// pipeline lifetime — call Dispose on the owning QuickLogResult instead.
    /// </summary>
    public SerilogLoggerAdapter(StructuredLogger herald)
    {
        ArgumentNullException.ThrowIfNull(herald);
        _herald = herald;
        _core = new HeraldAdapterCore(herald);
    }

    private SerilogLoggerAdapter(HeraldAdapterCore core)
    {
        _core = core;
        _herald = core.HeraldLogger;
    }

    /// <summary>
    /// Create an adapter from a PipelineBuildResult produced by QuickLogBuilder.Build.
    /// The returned adapter OWNS the pipeline lifetime: Dispose / DisposeAsync (or
    /// Log.CloseAndFlush / Log.CloseAndFlushAsync when assigned to Log.Logger)
    /// flushes async buffers and releases all pipeline resources.
    /// </summary>
    public static SerilogLoggerAdapter FromBuild(PipelineBuildResult buildResult)
        => FromBuild(buildResult, redaction: null);

    internal static SerilogLoggerAdapter FromBuild(
        PipelineBuildResult buildResult,
        MMP.Herald.Serilog.Destructuring.SerilogDestructuringApplicator? redaction)
        => new(HeraldAdapterCore.FromBuild(buildResult, redaction));

    /// <inheritdoc/>
    public bool IsEnabled(LogEventLevel level)
        => _core.IsEnabled(SerilogLevelMap.ToHerald(level));

    /// <inheritdoc/>
    public void Write(LogEventLevel level, string messageTemplate, params object?[]? propertyValues)
        => _core.Write(SerilogLevelMap.ToHerald(level), null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _core.Write(SerilogLevelMap.ToHerald(level), exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Verbose(string messageTemplate, params object?[]? propertyValues)
        => _core.Write(KnownLogLevels.Verbose, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Debug(string messageTemplate, params object?[]? propertyValues)
        => _core.Write(KnownLogLevels.Debug, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Information(string messageTemplate, params object?[]? propertyValues)
        => _core.Write(KnownLogLevels.Information, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Warning(string messageTemplate, params object?[]? propertyValues)
        => _core.Write(KnownLogLevels.Warning, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Error(string messageTemplate, params object?[]? propertyValues)
        => _core.Write(KnownLogLevels.Error, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Fatal(string messageTemplate, params object?[]? propertyValues)
        => _core.Write(KnownLogLevels.Fatal, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Verbose(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _core.Write(KnownLogLevels.Verbose, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Debug(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _core.Write(KnownLogLevels.Debug, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Information(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _core.Write(KnownLogLevels.Information, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Warning(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _core.Write(KnownLogLevels.Warning, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Error(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _core.Write(KnownLogLevels.Error, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Fatal(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => _core.Write(KnownLogLevels.Fatal, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public ILogger ForContext(string propertyName, object? value, bool destructureObjects = false)
    {
        if (string.IsNullOrEmpty(propertyName)) return this;
        return new SerilogLoggerAdapter(_core.WithContext(propertyName, value));
    }

    /// <inheritdoc/>
    public ILogger ForContext<TSource>() => ForContext(typeof(TSource));

    /// <inheritdoc/>
    public ILogger ForContext(Type source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new SerilogLoggerAdapter(_core.WithSource(source));
    }

    /// <summary>
    /// Flushes and releases pipeline resources when this adapter was created via
    /// FromBuild (ownership path). No-op on the externally-owned path. Idempotent
    /// and mutually exclusive with DisposeAsync.
    /// </summary>
    public void Dispose() => _core.Dispose();

    /// <summary>
    /// Async counterpart of Dispose — awaits the pipeline async buffer drain
    /// instead of blocking a thread on it. The path Log.CloseAndFlushAsync drives.
    /// Idempotent and mutually exclusive with Dispose via the same guard.
    /// </summary>
    public ValueTask DisposeAsync() => _core.DisposeAsync();
}
