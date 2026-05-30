#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Templating;

namespace MMP.Herald.Serilog;

/// <summary>
/// Bridges the Serilog-shaped <see cref="ILogger"/> surface to a Herald
/// <see cref="StructuredLogger"/> pipeline. Implements <see cref="IDisposable"/>
/// so callers that want explicit scope management can use a <c>using</c> block.
///
/// <para>
/// The <c>params object?[]?</c> verb/Write bodies are the <b>fallback path</b>
/// for callers whose arity is unknown or exceeds the typed-args generator limit
/// (16). Task 4's source generator emits zero-alloc typed overloads in front
/// of these — for the P1 scope this path is the only route and is intentionally
/// allocation-permissive.
/// </para>
///
/// <para>
/// Exception handling: Herald's <see cref="LogEvent"/> does not carry an
/// <see cref="Exception"/> field natively. The adapter stores the exception in
/// the event context under the key <c>"exception"</c>, matching the key that
/// <see cref="StructuredLogger"/>'s own exception-bearing verb overloads use.
/// </para>
/// </summary>
public sealed class SerilogLoggerAdapter : ILogger, IDisposable
{
    private readonly StructuredLogger _herald;

    // One parser per adapter (constructed once, reused for every Write call).
    // Uses the default LiteralFirst strategy — good for call-site repetition
    // which is the common case in application logging.
    private readonly MessageTemplateParser _parser = new();

    /// <summary>Wrap <paramref name="herald"/> in a Serilog-shaped adapter.</summary>
    public SerilogLoggerAdapter(StructuredLogger herald)
    {
        ArgumentNullException.ThrowIfNull(herald);
        _herald = herald;
    }

    // -------------------------------------------------------------------------
    // Factory — P1 build-result entry point (R-3, Richard ratified).
    // Requires InternalsVisibleTo("MMP.Herald.Serilog") on Herald.OSS.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Create a <see cref="SerilogLoggerAdapter"/> from a <see cref="PipelineBuildResult"/>
    /// produced by <see cref="QuickLogBuilder.Build"/>. Use this as the bridge
    /// between Herald's builder API and the Serilog-shaped surface.
    /// </summary>
    public static SerilogLoggerAdapter FromBuild(PipelineBuildResult buildResult)
    {
        ArgumentNullException.ThrowIfNull(buildResult);
        return new SerilogLoggerAdapter(buildResult.Logger);
    }

    // -------------------------------------------------------------------------
    // ILogger — gating
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public bool IsEnabled(LogEventLevel level)
        => _herald.IsEnabled(SerilogLevelMap.ToHerald(level));

    // -------------------------------------------------------------------------
    // ILogger — Write (no exception)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Write(LogEventLevel level, string messageTemplate, params object?[]? propertyValues)
        => WriteCore(level, null, messageTemplate, propertyValues);

    // -------------------------------------------------------------------------
    // ILogger — Write (with exception)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues)
        => WriteCore(level, exception, messageTemplate, propertyValues);

    // -------------------------------------------------------------------------
    // ILogger — verb overloads (no exception)
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // ILogger — verb overloads (with exception)
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // ILogger — context
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public ILogger ForContext(string propertyName, object? value, bool destructureObjects = false)
    {
        if (string.IsNullOrEmpty(propertyName)) return this;

        // Capture mode mirrors Serilog: '@' prefix = destructure.
        // destructureObjects parameter is the runtime equivalent of the '@' prefix.
        // Store under the bare name — Herald downstream code is unaware of
        // Serilog's capture-mode conventions at the context level.
        var context = new Dictionary<string, object?>(1, StringComparer.Ordinal)
        {
            [propertyName] = value
        };
        return new SerilogLoggerAdapter(_herald.WithContext(context));
    }

    /// <inheritdoc/>
    public ILogger ForContext<TSource>() => ForContext(typeof(TSource));

    /// <inheritdoc/>
    public ILogger ForContext(Type source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Serilog standard: "SourceContext" key carries the type name.
        var context = new Dictionary<string, object?>(1, StringComparer.Ordinal)
        {
            ["SourceContext"] = source.FullName ?? source.Name
        };
        return new SerilogLoggerAdapter(_herald.WithContext(context));
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    /// <summary>No-op. The underlying Herald pipeline is not owned by this adapter.</summary>
    public void Dispose() { }

    // -------------------------------------------------------------------------
    // Core dispatch — template parse + positional binding + Herald Log call
    // -------------------------------------------------------------------------

    // Cognitive complexity note: the three branches (no args, args shorter than
    // holes, args longer than holes) keep each case linear. Exception context
    // merging adds one conditional at the end. This is the clearest shape for
    // the params-fallback path; the zero-alloc typed path (Task 4) sidesteps
    // this entirely.
    private void WriteCore(
        LogEventLevel serilogLevel,
        Exception? exception,
        string messageTemplate,
        object?[]? propertyValues)
    {
        var heraldLevel = SerilogLevelMap.ToHerald(serilogLevel);

        if (!_herald.IsEnabled(heraldLevel)) return;

        // Parse the template to extract positional hole names.
        // The parser is cached per adapter; no per-call allocation.
        var parsed = _parser.Parse(messageTemplate);

        // Extract holes in order; pair each with the corresponding arg.
        // Extra args are silently ignored (matches Serilog's behaviour).
        var properties = BuildProperties(parsed, propertyValues);

        // Build the optional exception context dictionary.
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

    // Extract hole names in declaration order and pair each with the
    // corresponding positional arg. Returns null when there are no holes
    // (avoids a zero-element array allocation on plain-text messages).
    private static IReadOnlyList<LogProperty>? BuildProperties(
        MessageTemplate parsed,
        object?[]? args)
    {
        // Fast exit: no holes in the template — no properties to build.
        var holes = CollectHoleNames(parsed.Tokens);
        if (holes.Count == 0) return null;

        // Guard: no args supplied — leave holes unbound (same as Serilog's
        // behaviour when args are missing). Return null so callers see an
        // empty-property event rather than a property with a null value
        // for every hole.
        if (args is null || args.Length == 0) return null;

        var count = Math.Min(holes.Count, args.Length);
        var props = new LogProperty[count];
        for (var i = 0; i < count; i++)
            props[i] = new LogProperty(holes[i], args[i]);

        return props;
    }

    // Walk the token list and collect Property hole names in order.
    // Cognitive complexity note: single linear pass, no recursion.
    private static IReadOnlyList<string> CollectHoleNames(
        IReadOnlyList<MessageTemplateToken> tokens)
    {
        List<string>? names = null;
        foreach (var token in tokens)
        {
            if (token is MessageTemplateToken.Property prop)
            {
                names ??= new List<string>();
                names.Add(prop.Name);
            }
        }
        return (IReadOnlyList<string>?)names ?? Array.Empty<string>();
    }
}
