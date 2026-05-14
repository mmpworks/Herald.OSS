#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Output.Rich;

namespace MMP.Herald.Expansions;
/// <summary>
/// Appends stack trace details from context when available.
/// Expected context keys:
/// - exception
/// - stackTrace
/// </summary>
public sealed class StandardStackTraceExpansion : ILogLevelOutputExpansion
{
    public RenderedLogOutput Transform(LogRenderContext context, RenderedLogOutput current)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(current);

        if (!TryGetStackTraceText(context.Event.Context, out var stackTraceText))
        {
            return current;
        }

        var fragments = current.Fragments.ToList();
        fragments.Add(new RenderedLogFragment(Environment.NewLine + stackTraceText));

        return new RenderedLogOutput(fragments);
    }

    private static bool TryGetStackTraceText(
        IReadOnlyDictionary<string, object?> contextValues,
        out string stackTraceText)
    {
        if (contextValues.TryGetValue(Services.KnownPropertyNames.StackTrace, out var rawStackTrace) &&
            rawStackTrace is string directStackTrace &&
            !string.IsNullOrWhiteSpace(directStackTrace))
        {
            stackTraceText = "StackTrace:" + Environment.NewLine + directStackTrace;
            return true;
        }

        if (contextValues.TryGetValue(Services.LogContextKeys.Exception, out var rawException) &&
            rawException is Exception exception)
        {
            stackTraceText = "Exception: " + exception.Message +
                             Environment.NewLine +
                             "StackTrace:" +
                             Environment.NewLine +
                             (exception.StackTrace ?? "<no stack trace>");
            return true;
        }

        stackTraceText = string.Empty;
        return false;
    }
}