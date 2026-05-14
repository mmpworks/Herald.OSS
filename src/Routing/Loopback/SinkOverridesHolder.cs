#nullable enable

using System.Threading;
using MMP.Herald.Levels;

namespace MMP.Herald.Routing.Loopback;

/// <summary>
/// Mutable, atomic holder for one sink's per-sink runtime overrides:
/// the minimum-level gate and the two loopback tee flags. The
/// <see cref="LoopbackInterceptor"/> reads these on every event; the
/// management API's PATCH endpoints write them via the Set methods.
/// Like <see cref="SinkRunStateHolder"/>, this lets the dashboard's
/// header-strip controls take effect without rebuilding the pipeline
/// or even the wrapper itself — flipping the holder's value changes
/// the next event's behaviour.
///
/// <para>Minimum level is stored as the <see cref="LogLevel"/> object
/// rather than a rank because rank lookup lives in the level registry
/// (which caches lookups). The interceptor passes both the event's
/// level and the gate's level to the registry's hot-path
/// <c>IsBelow</c>, which short-circuits on the dictionary cache.</para>
/// </summary>
public sealed class SinkOverridesHolder
{
    private LogLevel? _minLevel;
    private int _teeLiveToFile;
    private int _teeLiveToUrl;

    public SinkOverridesHolder(LogLevel? initialMinLevel, bool initialTeeLiveToFile, bool initialTeeLiveToUrl)
    {
        _minLevel = initialMinLevel;
        _teeLiveToFile = initialTeeLiveToFile ? 1 : 0;
        _teeLiveToUrl = initialTeeLiveToUrl ? 1 : 0;
    }

    /// <summary>
    /// Current minimum-level gate. Null means the gate is open and
    /// every event passes; otherwise events whose level is below this
    /// (per the level registry's rank ordering) are dropped before
    /// the inner sink sees them.
    /// </summary>
    public LogLevel? MinLevel => Volatile.Read(ref _minLevel);

    /// <summary>
    /// In live mode, also tee each event to the loopback file leg
    /// when this is true.
    /// </summary>
    public bool TeeLiveToFile => Volatile.Read(ref _teeLiveToFile) != 0;

    /// <summary>
    /// In live mode, also tee each event to the loopback URL leg
    /// when this is true.
    /// </summary>
    public bool TeeLiveToUrl => Volatile.Read(ref _teeLiveToUrl) != 0;

    /// <summary>Replace the minimum-level gate. Returns the previous value.</summary>
    public LogLevel? SetMinLevel(LogLevel? next) => Interlocked.Exchange(ref _minLevel, next);

    /// <summary>Replace the tee-live-to-file flag. Returns the previous value.</summary>
    public bool SetTeeLiveToFile(bool next) => Interlocked.Exchange(ref _teeLiveToFile, next ? 1 : 0) != 0;

    /// <summary>Replace the tee-live-to-url flag. Returns the previous value.</summary>
    public bool SetTeeLiveToUrl(bool next) => Interlocked.Exchange(ref _teeLiveToUrl, next ? 1 : 0) != 0;
}
