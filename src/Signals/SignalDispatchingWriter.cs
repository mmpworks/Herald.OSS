#nullable enable

using System;
using MMP.Herald.Output.Rich;
using MMP.Herald.Responses;

namespace MMP.Herald.Signals;

/// <summary>
/// Decorator that intercepts rendered output, dispatches any embedded
/// signals to a handler, then forwards the output to the inner writer
/// for normal rendering.
///
/// This is the bridge between the output pipeline and the signal system.
/// Transformers produce signals in RenderedLogOutput. This decorator
/// extracts and dispatches them. Writers stay pure presentation.
/// Signal handlers stay pure action. Neither knows about the other.
///
/// When a handler returns a TupleResponse with a message, the
/// <see cref="OnSignalResult"/> callback is invoked (if configured).
/// This allows the caller to log handler messages and errors without
/// creating a feedback loop into the pipeline.
///
/// Thread-safe: all mutable state is protected by a lock.
///
/// Usage:
///   var writer = new SignalDispatchingWriter(
///       innerWriter: new DefaultRichConsoleWriter(),
///       signalHandler: new CompositeLogSignalHandler([...]),
///       onSignalResult: response => Console.WriteLine(response.Message)
///   );
/// </summary>
public sealed class SignalDispatchingWriter : IRenderedLogOutputWriter
{
    private readonly IRenderedLogOutputWriter _inner;
    private readonly ILogSignalHandler _signalHandler;
    private readonly Action<LogSignal, TupleResponse>? _onSignalResult;
    private readonly object _sync = new();

    public SignalDispatchingWriter(
        IRenderedLogOutputWriter innerWriter,
        ILogSignalHandler signalHandler,
        Action<LogSignal, TupleResponse>? onSignalResult = null)
    {
        _inner = innerWriter ?? throw new ArgumentNullException(nameof(innerWriter));
        _signalHandler = signalHandler ?? throw new ArgumentNullException(nameof(signalHandler));
        _onSignalResult = onSignalResult;
    }

    public void Write(RenderedLogOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (output.HasSignals)
        {
            DispatchSignals(output);
        }

        _inner.Write(output);
    }

    private void DispatchSignals(RenderedLogOutput output)
    {
        // Signals dispatch before rendering so handlers can react
        // before the user sees the output. Handlers that need
        // post-render timing should queue internally.
        foreach (var signal in output.Signals!)
        {
            if (!_signalHandler.CanHandle(signal)) continue;

            TupleResponse response;
            lock (_sync)
            {
                try
                {
                    response = _signalHandler.Handle(signal);
                }
                catch (Exception ex)
                {
                    response = TupleResponse.FromException(ex);
                }
            }

            // Report results when there is something to say
            if (response.Message is not null)
            {
                _onSignalResult?.Invoke(signal, response);
            }
        }
    }
}
