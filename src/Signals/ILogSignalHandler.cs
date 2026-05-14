#nullable enable

using MMP.Herald.Output.Rich;
using MMP.Herald.Responses;

namespace MMP.Herald.Signals;

/// <summary>
/// Handles actionable signals produced by log output transformers.
///
/// Implementations decide what a signal means in their domain:
/// a Sentry alert, a Slack message, a game engine signal,
/// a game event, a metrics counter - anything.
///
/// Handlers are composed via CompositeLogSignalHandler, so each
/// handler stays focused on one concern.
///
/// Handle returns a TupleResponse:
///   - IsOk with a non-null Message: the dispatcher logs the message.
///   - IsOk with a null Message: silent success, nothing logged.
///   - IsError: the dispatcher logs the error. The pipeline is never broken.
/// </summary>
public interface ILogSignalHandler
{
    /// <summary>
    /// Whether this handler accepts the given signal.
    /// Returning false means Handle will not be called for this signal.
    /// </summary>
    bool CanHandle(LogSignal signal);

    /// <summary>
    /// Dispatch the signal. Called only when CanHandle returns true.
    ///
    /// Return TupleResponse.Ok() for silent success.
    /// Return TupleResponse.Ok(message: "...") to have the message logged.
    /// Return TupleResponse.Error(...) to report a failure without breaking the pipeline.
    ///
    /// Implementations should not throw. If they do, the dispatcher catches
    /// the exception and converts it to an error response automatically.
    /// </summary>
    TupleResponse Handle(LogSignal signal);
}
