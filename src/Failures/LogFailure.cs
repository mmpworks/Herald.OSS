// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using MMP.Herald.Events;

namespace MMP.Herald.Failures;

/// <summary>
/// A failure raised while the logging pipeline emits an event, with the
/// decision already made.
///
/// <para>
/// The original contract handed a consumer an exception and left it to work out
/// whether the write was worth repeating. Every consumer wrote its own version
/// of that logic and they disagreed. <see cref="Code"/> and
/// <see cref="Retryable"/> carry one answer, taken from
/// <see cref="FailureClassifier"/>, so a retry policy and a dashboard read the
/// same failure the same way.
/// </para>
///
/// <para>
/// Callers branch on <see cref="Code"/>, never on <see cref="Message"/>.
/// </para>
///
/// <para>
/// A LogFailure is built only on the failure branch. The emit path allocates
/// nothing for it.
/// </para>
/// </summary>
/// <param name="Code">Stable machine code. See the Code constants on this type.</param>
/// <param name="Message">Human-readable text. Never branch on this.</param>
/// <param name="Retryable">True when repeating the write may succeed.</param>
/// <param name="CorrelationId">Unique per failure, so an operator can find this one.</param>
/// <param name="Exception">The exception that caused the failure.</param>
/// <param name="Source">The component that reported it.</param>
/// <param name="LogEvent">The event that was being emitted.</param>
public sealed record LogFailure(
    string Code,
    string Message,
    bool Retryable,
    string CorrelationId,
    Exception Exception,
    string Source,
    LogEvent LogEvent)
{
    /// <summary>The write may succeed if repeated (timeout, 5xx, 408, 429, socket reset).</summary>
    public const string TransientCode = "HERALD_SINK_TRANSIENT";

    /// <summary>The write will fail the same way if repeated (auth, bad format, 4xx).</summary>
    public const string PermanentCode = "HERALD_SINK_PERMANENT";

    /// <summary>The exception is unrecognized. Treated as retryable, matching the classifier.</summary>
    public const string UnknownCode = "HERALD_SINK_UNKNOWN";

    /// <summary>
    /// Build a failure from what a report site already holds. The code and the
    /// retryable flag come from <see cref="FailureClassifier"/>, so a report
    /// site never has to classify the exception itself.
    /// </summary>
    public static LogFailure From(LogEvent logEvent, Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var category = FailureClassifier.Classify(exception);

        return new LogFailure(
            Code: CodeFor(category),
            Message: exception.Message,
            // Unknown is retryable: the classifier already documents that an
            // unrecognized exception is treated as transient for safety.
            Retryable: category != FailureCategory.Permanent,
            CorrelationId: Guid.NewGuid().ToString("N"),
            Exception: exception,
            Source: source,
            LogEvent: logEvent);
    }

    private static string CodeFor(FailureCategory category)
    {
        if (category == FailureCategory.Transient) return TransientCode;
        if (category == FailureCategory.Permanent) return PermanentCode;
        return UnknownCode;
    }
}
