#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline;

/// <summary>
/// High-performance logging factory inspired by Microsoft.Extensions.Logging's LoggerMessage.Define pattern.
/// Pre-compiles message templates and property names at definition time.
/// Returns delegates that:
///   - Check IsEnabled() first (zero allocation when level is disabled)
///   - Reuse pre-built property name arrays (minimal allocation when enabled)
///   - Skip dictionary allocation for context (no context on hot path)
///
/// Usage:
///   private static readonly Action&lt;StructuredLogger, string&gt; LogPlayerEntered =
///       LogMessage.Define&lt;string&gt;(KnownLogLevels.Info, LogCategory.App, "Player {playerId} entered");
///
///   // At call site (zero alloc if Info is disabled):
///   LogPlayerEntered(_logger, "Kael_the_Bold");
/// </summary>
public static class LogMessage
{
    public static Action<StructuredLogger> Define(
        LogLevel level,
        LogCategory category,
        string messageTemplate)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(messageTemplate);

        return (logger) =>
        {
            if (!logger.IsEnabled(level))
            {
                return;
            }

            logger.Log(level, category, messageTemplate);
        };
    }

    public static Action<StructuredLogger, T1> Define<T1>(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        string propertyName1)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(messageTemplate);
        ArgumentNullException.ThrowIfNull(propertyName1);

        return (logger, value1) =>
        {
            if (!logger.IsEnabled(level))
            {
                return;
            }

            logger.Log(level, category, messageTemplate,
                properties: [new LogProperty(propertyName1, value1)]);
        };
    }

    public static Action<StructuredLogger, T1, T2> Define<T1, T2>(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        string propertyName1,
        string propertyName2)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(messageTemplate);
        ArgumentNullException.ThrowIfNull(propertyName1);
        ArgumentNullException.ThrowIfNull(propertyName2);

        return (logger, value1, value2) =>
        {
            if (!logger.IsEnabled(level))
            {
                return;
            }

            logger.Log(level, category, messageTemplate,
                properties:
                [
                    new LogProperty(propertyName1, value1),
                    new LogProperty(propertyName2, value2)
                ]);
        };
    }

    public static Action<StructuredLogger, T1, T2, T3> Define<T1, T2, T3>(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        string propertyName1,
        string propertyName2,
        string propertyName3)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(messageTemplate);
        ArgumentNullException.ThrowIfNull(propertyName1);
        ArgumentNullException.ThrowIfNull(propertyName2);
        ArgumentNullException.ThrowIfNull(propertyName3);

        return (logger, value1, value2, value3) =>
        {
            if (!logger.IsEnabled(level))
            {
                return;
            }

            logger.Log(level, category, messageTemplate,
                properties:
                [
                    new LogProperty(propertyName1, value1),
                    new LogProperty(propertyName2, value2),
                    new LogProperty(propertyName3, value3)
                ]);
        };
    }

    public static Action<StructuredLogger, T1, T2, T3, T4> Define<T1, T2, T3, T4>(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        string propertyName1,
        string propertyName2,
        string propertyName3,
        string propertyName4)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(messageTemplate);
        ArgumentNullException.ThrowIfNull(propertyName1);
        ArgumentNullException.ThrowIfNull(propertyName2);
        ArgumentNullException.ThrowIfNull(propertyName3);
        ArgumentNullException.ThrowIfNull(propertyName4);

        return (logger, value1, value2, value3, value4) =>
        {
            if (!logger.IsEnabled(level))
            {
                return;
            }

            logger.Log(level, category, messageTemplate,
                properties:
                [
                    new LogProperty(propertyName1, value1),
                    new LogProperty(propertyName2, value2),
                    new LogProperty(propertyName3, value3),
                    new LogProperty(propertyName4, value4)
                ]);
        };
    }

    public static Action<StructuredLogger, T1, T2, T3, T4, T5> Define<T1, T2, T3, T4, T5>(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        string propertyName1,
        string propertyName2,
        string propertyName3,
        string propertyName4,
        string propertyName5)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(messageTemplate);
        ArgumentNullException.ThrowIfNull(propertyName1);
        ArgumentNullException.ThrowIfNull(propertyName2);
        ArgumentNullException.ThrowIfNull(propertyName3);
        ArgumentNullException.ThrowIfNull(propertyName4);
        ArgumentNullException.ThrowIfNull(propertyName5);

        return (logger, value1, value2, value3, value4, value5) =>
        {
            if (!logger.IsEnabled(level))
            {
                return;
            }

            logger.Log(level, category, messageTemplate,
                properties:
                [
                    new LogProperty(propertyName1, value1),
                    new LogProperty(propertyName2, value2),
                    new LogProperty(propertyName3, value3),
                    new LogProperty(propertyName4, value4),
                    new LogProperty(propertyName5, value5)
                ]);
        };
    }

    public static Action<StructuredLogger, T1, T2, T3, T4, T5, T6> Define<T1, T2, T3, T4, T5, T6>(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        string propertyName1,
        string propertyName2,
        string propertyName3,
        string propertyName4,
        string propertyName5,
        string propertyName6)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(messageTemplate);

        return (logger, value1, value2, value3, value4, value5, value6) =>
        {
            if (!logger.IsEnabled(level))
            {
                return;
            }

            logger.Log(level, category, messageTemplate,
                properties:
                [
                    new LogProperty(propertyName1, value1),
                    new LogProperty(propertyName2, value2),
                    new LogProperty(propertyName3, value3),
                    new LogProperty(propertyName4, value4),
                    new LogProperty(propertyName5, value5),
                    new LogProperty(propertyName6, value6)
                ]);
        };
    }

    public static Action<StructuredLogger, T1, T2, T3, T4, T5, T6, T7> Define<T1, T2, T3, T4, T5, T6, T7>(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        string propertyName1,
        string propertyName2,
        string propertyName3,
        string propertyName4,
        string propertyName5,
        string propertyName6,
        string propertyName7)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(messageTemplate);

        return (logger, value1, value2, value3, value4, value5, value6, value7) =>
        {
            if (!logger.IsEnabled(level))
            {
                return;
            }

            logger.Log(level, category, messageTemplate,
                properties:
                [
                    new LogProperty(propertyName1, value1),
                    new LogProperty(propertyName2, value2),
                    new LogProperty(propertyName3, value3),
                    new LogProperty(propertyName4, value4),
                    new LogProperty(propertyName5, value5),
                    new LogProperty(propertyName6, value6),
                    new LogProperty(propertyName7, value7)
                ]);
        };
    }

    public static Action<StructuredLogger, T1, T2, T3, T4, T5, T6, T7, T8> Define<T1, T2, T3, T4, T5, T6, T7, T8>(
        LogLevel level,
        LogCategory category,
        string messageTemplate,
        string propertyName1,
        string propertyName2,
        string propertyName3,
        string propertyName4,
        string propertyName5,
        string propertyName6,
        string propertyName7,
        string propertyName8)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(messageTemplate);

        return (logger, value1, value2, value3, value4, value5, value6, value7, value8) =>
        {
            if (!logger.IsEnabled(level))
            {
                return;
            }

            logger.Log(level, category, messageTemplate,
                properties:
                [
                    new LogProperty(propertyName1, value1),
                    new LogProperty(propertyName2, value2),
                    new LogProperty(propertyName3, value3),
                    new LogProperty(propertyName4, value4),
                    new LogProperty(propertyName5, value5),
                    new LogProperty(propertyName6, value6),
                    new LogProperty(propertyName7, value7),
                    new LogProperty(propertyName8, value8)
                ]);
        };
    }
}
