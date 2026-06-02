#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Templating;

// Upgrade note for C# 14: the _onDispose Action? field pattern could be
// replaced by a more expressive lifecycle interface if the language adds
// first-class disposable-delegation sugar; keep current form for C# 12 compat.

namespace MMP.Herald.Serilog;

/// <summary>
/// Bridges the Serilog-shaped ILogger surface to a Herald StructuredLogger
/// pipeline. Implements IDisposable and IAsyncDisposable so callers that want
/// explicit scope management can use a using / await using block.
/// </summary>
public sealed partial class SerilogLoggerAdapter : ILogger, IDisposable, IAsyncDisposable
{
    private readonly StructuredLogger _herald;

    // One parser per adapter (constructed once, reused for every Write call).
    private readonly MessageTemplateParser _parser = new();

    // Lifecycle ownership flag (R-5 / Richard CloseAndFlush gap):
    //   - Null     -> adapter wraps an externally-owned StructuredLogger;
    //                 Dispose() is a no-op (the caller manages the pipeline).
    //   - Non-null -> FromBuild() created this adapter and owns the pipeline;
    //                 Dispose() invokes the captured flush-and-release action.
    private readonly Action? _onDispose;

    // Async flush counterpart of _onDispose (CloseAndFlushAsync support).
    //   - Null     -> externally-owned pipeline; DisposeAsync() is a no-op.
    //   - Non-null -> FromBuild() owns the pipeline; DisposeAsync() awaits the
    //                 async buffer drain WITHOUT a blocking GetResult() — the
    //                 honest async shutdown path Log.CloseAndFlushAsync provides.
    private readonly Func<ValueTask>? _onDisposeAsync;

    // G2.3: double-dispose guard. Governs both Dispose and DisposeAsync — first
    // caller wins, every later call no-ops, so the async buffer is never
    // double-disposed.
    private int _disposed;

    // Capture-time redaction (security fix REG-SERILOG-DESTRUCTURE-NATIVE-SINK).
    // Null when no destructuring policy is registered (common, zero-cost case).
    private readonly MMP.Herald.Serilog.Destructuring.SerilogDestructuringApplicator? _redaction;

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
        _onDispose = null;
        _onDisposeAsync = null;
        _redaction = null;
    }

    private SerilogLoggerAdapter(
        StructuredLogger herald,
        Action onDispose,
        Func<ValueTask> onDisposeAsync,
        MMP.Herald.Serilog.Destructuring.SerilogDestructuringApplicator? redaction)
    {
        _herald = herald;
        _onDispose = onDispose;
        _onDisposeAsync = onDisposeAsync;
        _redaction = redaction;
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
    {
        ArgumentNullException.ThrowIfNull(buildResult);

        var bootstrap = buildResult.BootstrapResult;

        Action onDispose = () =>
        {
            if (bootstrap.AsyncResource is { } asyncResource)
                asyncResource.DisposeAsync().AsTask().GetAwaiter().GetResult();

            DisposeSyncResources(bootstrap);
        };

        Func<ValueTask> onDisposeAsync = async () =>
        {
            if (bootstrap.AsyncResource is { } asyncResource)
                await asyncResource.DisposeAsync().ConfigureAwait(false);

            DisposeSyncResources(bootstrap);
        };

        return new SerilogLoggerAdapter(buildResult.Logger, onDispose, onDisposeAsync, redaction);
    }

    private static void DisposeSyncResources(MMP.Herald.Bootstrap.LoggingBootstrapResult bootstrap)
    {
        if (bootstrap.SyncResources is { } syncResources)
        {
            foreach (var disposable in syncResources)
                disposable.Dispose();
        }
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogEventLevel level)
        => _herald.IsEnabled(SerilogLevelMap.ToHerald(level));

    /// <inheritdoc/>
    public void Write(LogEventLevel level, string messageTemplate, params object?[]? propertyValues)
        => WriteCore(level, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => WriteCore(level, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Verbose(string messageTemplate, params object?[]? propertyValues)
        => WriteCore(LogEventLevel.Verbose, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Debug(string messageTemplate, params object?[]? propertyValues)
        => WriteCore(LogEventLevel.Debug, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Information(string messageTemplate, params object?[]? propertyValues)
        => WriteCore(LogEventLevel.Information, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Warning(string messageTemplate, params object?[]? propertyValues)
        => WriteCore(LogEventLevel.Warning, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Error(string messageTemplate, params object?[]? propertyValues)
        => WriteCore(LogEventLevel.Error, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Fatal(string messageTemplate, params object?[]? propertyValues)
        => WriteCore(LogEventLevel.Fatal, null, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Verbose(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => WriteCore(LogEventLevel.Verbose, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Debug(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => WriteCore(LogEventLevel.Debug, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Information(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => WriteCore(LogEventLevel.Information, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Warning(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => WriteCore(LogEventLevel.Warning, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Error(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => WriteCore(LogEventLevel.Error, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public void Fatal(Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => WriteCore(LogEventLevel.Fatal, exception, messageTemplate, propertyValues);

    /// <inheritdoc/>
    public ILogger ForContext(string propertyName, object? value, bool destructureObjects = false)
    {
        if (string.IsNullOrEmpty(propertyName)) return this;

        var context = new Dictionary<string, object?>(1, StringComparer.Ordinal)
        {
            [propertyName] = value
        };
        return new SerilogLoggerAdapter(_herald.ForContext(context));
    }

    /// <inheritdoc/>
    public ILogger ForContext<TSource>() => ForContext(typeof(TSource));

    /// <inheritdoc/>
    public ILogger ForContext(Type source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var context = new Dictionary<string, object?>(1, StringComparer.Ordinal)
        {
            ["SourceContext"] = source.FullName ?? source.Name
        };
        return new SerilogLoggerAdapter(_herald.ForContext(context));
    }

    /// <summary>
    /// Flushes and releases pipeline resources when this adapter was created via
    /// FromBuild (ownership path). No-op on the externally-owned path. Idempotent
    /// (G2.3) and mutually exclusive with DisposeAsync.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _onDispose?.Invoke();
    }

    /// <summary>
    /// Async counterpart of Dispose — awaits the pipeline async buffer drain
    /// instead of blocking a thread on it. The path Log.CloseAndFlushAsync drives.
    /// Idempotent and mutually exclusive with Dispose via the same guard.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        if (_onDisposeAsync is { } flush)
            await flush().ConfigureAwait(false);
    }

    private void WriteCore(
        LogEventLevel serilogLevel,
        Exception? exception,
        string messageTemplate,
        object?[]? propertyValues)
    {
        var heraldLevel = SerilogLevelMap.ToHerald(serilogLevel);

        if (!_herald.IsEnabled(heraldLevel)) return;

        var parsed = _parser.Parse(messageTemplate);
        var properties = BuildProperties(parsed, propertyValues, _redaction);

        IReadOnlyDictionary<string, object?>? context = null;
        if (exception is not null)
        {
            context = new Dictionary<string, object?>(1, StringComparer.Ordinal)
            {
                [MMP.Herald.Services.LogContextKeys.Exception] = exception
            };
        }

        _herald.Log(heraldLevel, LogCategory.None, messageTemplate, properties, context);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Trimming", "IL2026",
        Justification = "Capture-time redaction runs the user destructuring policy chain, " +
                        "which may reflect over the value graph. Same reflection the mirror " +
                        "projection path already declares.")]
    private static IReadOnlyList<LogProperty>? BuildProperties(
        MessageTemplate parsed,
        object?[]? args,
        MMP.Herald.Serilog.Destructuring.SerilogDestructuringApplicator? redaction)
    {
        var holes = CollectHoles(parsed.Tokens);
        if (holes.Count == 0) return null;

        if (args is null || args.Length == 0) return null;

        var redact = redaction is { HasPolicies: true } ? redaction : null;

        var count = Math.Min(holes.Count, args.Length);
        var props = new LogProperty[count];
        for (var i = 0; i < count; i++)
        {
            var (name, captureMode) = holes[i];
            var value = args[i];

            if (redact is not null &&
                captureMode == LogPropertyCaptureMode.Destructure &&
                redact.TryRedactNative(value, out var redacted))
            {
                value = redacted;
            }

            props[i] = new LogProperty(name, value, captureMode);
        }

        return props;
    }

    private static IReadOnlyList<(string Name, LogPropertyCaptureMode CaptureMode)> CollectHoles(
        IReadOnlyList<MessageTemplateToken> tokens)
    {
        List<(string, LogPropertyCaptureMode)>? holes = null;
        foreach (var token in tokens)
        {
            if (token is MessageTemplateToken.Property prop)
            {
                holes ??= new List<(string, LogPropertyCaptureMode)>();
                holes.Add((prop.Name, prop.CaptureMode));
            }
        }
        return (IReadOnlyList<(string, LogPropertyCaptureMode)>?)holes
            ?? Array.Empty<(string, LogPropertyCaptureMode)>();
    }
}
