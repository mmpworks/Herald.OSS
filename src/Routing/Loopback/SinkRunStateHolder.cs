#nullable enable

using System.Threading;
using MMP.Herald.Configuration.Runtime;

namespace MMP.Herald.Routing.Loopback;

/// <summary>
/// Mutable, atomic holder for one sink's <see cref="SinkRunState"/>.
/// The interceptor reads the current state on every event; the
/// management API's PATCH endpoint writes the new state via
/// <see cref="Set"/>. Reads and writes go through
/// <see cref="Volatile"/> so a state change is visible to running
/// loops without a memory-barrier surprise.
///
/// <para>The holder is the indirection that lets a runState toggle
/// take effect without rebuilding the pipeline. The interceptor wraps
/// each sink once at construction time; flipping the holder's value
/// changes the next event's behaviour.</para>
/// </summary>
public sealed class SinkRunStateHolder
{
    private int _state;

    public SinkRunStateHolder(SinkRunState initial)
    {
        _state = (int)initial;
    }

    /// <summary>The current state. Cheap to read on the hot path.</summary>
    public SinkRunState Current => (SinkRunState)Volatile.Read(ref _state);

    /// <summary>
    /// Replace the current state. The next call to <see cref="Current"/>
    /// observes the new value. Returns the previous state so the caller
    /// can log the transition.
    /// </summary>
    public SinkRunState Set(SinkRunState next) =>
        (SinkRunState)Interlocked.Exchange(ref _state, (int)next);
}
