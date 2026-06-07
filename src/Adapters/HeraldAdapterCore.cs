#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using MMP.Herald.Templating;

namespace MMP.Herald.Adapters;

/// <summary>
/// The level-agnostic engine shared by every migration-compat logger skin
/// (Serilog, NLog, log4net). It owns the parts that are genuinely the same
/// across all three: message-template parsing, template-hole → LogProperty
/// projection, capture-time redaction, exception-to-context plumbing,
/// ForContext dictionary merge, and the FromBuild ownership lifecycle.
///
/// <para>
/// Each shim is a thin <em>verb-and-level skin</em> over this core. The skin
/// translates its framework's verb (Serilog <c>Information</c>, NLog
/// <c>Info</c>, log4net <c>Info</c>) into a Herald <see cref="LogLevel"/> and
/// forwards to <see cref="Write"/>. The vocabulary differs per framework; the
/// engine does not. That is the DRY line — one engine, three surfaces.
/// </para>
///
/// <para>
/// Internal by design: it is an implementation detail of the shims, not a
/// public contract. Consumers see only the framework-shaped surface.
/// </para>
/// </summary>
internal sealed class HeraldAdapterCore : IDisposable, IAsyncDisposable
{
    private readonly StructuredLogger _herald;

    // One parser per core instance (constructed once, reused for every Write call).
    private readonly MessageTemplateParser _parser = new();

    // Lifecycle ownership flag (mirrors the Serilog adapter's R-5 / CloseAndFlush gap):
    //   - Null     -> the core wraps an externally-owned StructuredLogger;
    //                 Dispose() is a no-op (the caller manages the pipeline).
    //   - Non-null -> FromBuild() created this core and owns the pipeline;
    //                 Dispose() invokes the captured flush-and-release action.
    private readonly Action? _onDispose;

    // Async flush counterpart of _onDispose (CloseAndFlushAsync support).
    //   - Null     -> externally-owned pipeline; DisposeAsync() is a no-op.
    //   - Non-null -> FromBuild() owns the pipeline; DisposeAsync() awaits the
    //                 async buffer drain WITHOUT a blocking GetResult().
    private readonly Func<ValueTask>? _onDisposeAsync;

    // Double-dispose guard. Governs both Dispose and DisposeAsync — first caller
    // wins, every later call no-ops, so the async buffer is never double-disposed.
    private int _disposed;

    // Capture-time redaction (security parity with the Serilog adapter's
    // REG-SERILOG-DESTRUCTURE-NATIVE-SINK fix). Null when no destructuring policy
    // is registered (common, zero-cost case). Typed as the engine-side
    // ICaptureRedactor interface — NOT the Serilog mirror applicator — so this
    // engine type never references an MMP.Herald.Serilog.* type (Guard1
    // confinement). NLog/log4net pass null today; the seam stays open.
    private readonly ICaptureRedactor? _redaction;

    /// <summary>
    /// The underlying Herald StructuredLogger. Exposed so a skin's AspNetCore
    /// wiring can register the raw logger with MEL, matching the Serilog adapter.
    /// </summary>
    internal StructuredLogger HeraldLogger => _herald;

    /// <summary>
    /// Wrap <paramref name="herald"/> without owning its lifetime. Dispose is a
    /// no-op on this path — the caller owns the pipeline.
    /// </summary>
    internal HeraldAdapterCore(StructuredLogger herald)
    {
        ArgumentNullException.ThrowIfNull(herald);
        _herald = herald;
        _onDispose = null;
        _onDisposeAsync = null;
        _redaction = null;
    }

    private HeraldAdapterCore(
        StructuredLogger herald,
        Action onDispose,
        Func<ValueTask> onDisposeAsync,
        ICaptureRedactor? redaction)
    {
        _herald = herald;
        _onDispose = onDispose;
        _onDisposeAsync = onDisposeAsync;
        _redaction = redaction;
    }

    /// <summary>
    /// Build a core from a QuickLogBuilder PipelineBuildResult. The returned core
    /// OWNS the pipeline lifetime: Dispose / DisposeAsync flushes async buffers and
    /// releases all pipeline resources.
    /// </summary>
    internal static HeraldAdapterCore FromBuild(
        PipelineBuildResult buildResult,
        ICaptureRedactor? redaction = null)
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

        return new HeraldAdapterCore(buildResult.Logger, onDispose, onDisposeAsync, redaction);
    }

    private static void DisposeSyncResources(MMP.Herald.Bootstrap.LoggingBootstrapResult bootstrap)
    {
        if (bootstrap.SyncResources is { } syncResources)
        {
            foreach (var disposable in syncResources)
                disposable.Dispose();
        }
    }

    /// <summary>True when <paramref name="heraldLevel"/> passes the pipeline floor.</summary>
    internal bool IsEnabled(LogLevel heraldLevel) => _herald.IsEnabled(heraldLevel);

    /// <summary>
    /// The single write path every skin funnels through. Parses the template,
    /// projects holes to properties (with capture-time redaction when a policy is
    /// present), attaches the exception to context, and dispatches to Herald.
    /// </summary>
    internal void Write(
        LogLevel heraldLevel,
        Exception? exception,
        string messageTemplate,
        object?[]? args)
    {
        if (!_herald.IsEnabled(heraldLevel)) return;

        var parsed = _parser.Parse(messageTemplate);
        var properties = BuildProperties(parsed, args, _redaction);

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

    /// <summary>
    /// Return a new core that attaches <paramref name="value"/> under
    /// <paramref name="name"/> to every subsequent event (Serilog ForContext /
    /// NLog WithProperty). Empty name is a no-op — returns this core unchanged.
    /// </summary>
    internal HeraldAdapterCore WithContext(string name, object? value)
    {
        if (string.IsNullOrEmpty(name)) return this;

        var context = new Dictionary<string, object?>(1, StringComparer.Ordinal)
        {
            [name] = value
        };
        return new HeraldAdapterCore(_herald.ForContext(context));
    }

    /// <summary>
    /// Return a new core sourced from <paramref name="source"/> — sets
    /// SourceContext to the full type name, matching Serilog's convention.
    /// </summary>
    internal HeraldAdapterCore WithSource(Type source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return WithContext("SourceContext", source.FullName ?? source.Name);
    }

    /// <summary>
    /// Flushes and releases pipeline resources when this core was created via
    /// FromBuild (ownership path). No-op on the externally-owned path. Idempotent
    /// and mutually exclusive with DisposeAsync.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _onDispose?.Invoke();
    }

    /// <summary>
    /// Async counterpart of Dispose — awaits the pipeline async buffer drain
    /// instead of blocking a thread on it. Idempotent and mutually exclusive with
    /// Dispose via the same guard.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        if (_onDisposeAsync is { } flush)
            await flush().ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Trimming", "IL2026",
        Justification = "Capture-time redaction runs the user destructuring policy chain, " +
                        "which may reflect over the value graph. Same reflection the mirror " +
                        "projection path already declares.")]
    private static IReadOnlyList<LogProperty>? BuildProperties(
        MessageTemplate parsed,
        object?[]? args,
        ICaptureRedactor? redaction)
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
