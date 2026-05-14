#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Output.Rich;
using MMP.Herald.Responses;

namespace MMP.Herald.Signals;

/// <summary>
/// Routes signals to all registered handlers that accept them.
/// Each handler independently decides which signals it cares about.
///
/// Returns an aggregate TupleResponse:
///   - If all handlers succeed silently: Ok with null message.
///   - If any handler returns a message: Ok with combined messages.
///   - If any handler fails: Error with combined error messages.
///     Remaining handlers still execute - one failure never blocks another.
///
/// Thread-safe: the handler list is immutable after construction.
/// Individual handler thread safety is the handler's responsibility.
///
/// Usage:
///   var handler = new CompositeLogSignalHandler(
///   [
///       new SentryAlertHandler(),
///       new SlackNotificationHandler(),
///       new GuardAlertHandler()
///   ]);
///
/// A signal like "guard_alert" reaches GuardAlertHandler.
/// A signal like "sentry_error" reaches SentryAlertHandler.
/// Both fire independently - no coupling between handlers.
/// </summary>
public sealed class CompositeLogSignalHandler : ILogSignalHandler
{
    private readonly IReadOnlyList<ILogSignalHandler> _handlers;

    public CompositeLogSignalHandler(IReadOnlyList<ILogSignalHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers;
    }

    public bool CanHandle(LogSignal signal)
    {
        foreach (var handler in _handlers)
        {
            if (handler.CanHandle(signal)) return true;
        }

        return false;
    }

    public TupleResponse Handle(LogSignal signal)
    {
        List<string>? messages = null;
        bool anyError = false;
        int lastErrorCode = 0;

        foreach (var handler in _handlers)
        {
            if (!handler.CanHandle(signal)) continue;

            TupleResponse response;
            try
            {
                response = handler.Handle(signal);
            }
            catch (Exception ex)
            {
                response = TupleResponse.FromException(ex);
            }

            if (response.IsError)
            {
                anyError = true;
                lastErrorCode = response.Code;
            }

            if (response.Message is not null)
            {
                messages ??= new List<string>();
                messages.Add(response.Message);
            }
        }

        if (anyError)
        {
            var combined = messages is not null ? string.Join("; ", messages) : null;
            return TupleResponse.Error(lastErrorCode, combined);
        }

        if (messages is { Count: > 0 })
        {
            return TupleResponse.Ok(message: string.Join("; ", messages));
        }

        return TupleResponse.Ok();
    }
}
