#nullable enable

using System;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Configuration;

/// <summary>
/// Translates the Serilog-shaped <c>WriteTo.File</c> rolling/retention arguments
/// into the right native <see cref="QuickLogBuilder"/> file-sink call.
///
/// <para>
/// This type exists so the <see cref="LoggerSinkConfiguration.File"/> verb stays a
/// one-line forwarder with no conditionals (the verb-class DRY contract forbids
/// if/dispatch logic in the forwarder). All the enum→token mapping and the
/// default-vs-rich decision live here.
/// </para>
///
/// <para>
/// <b>Dispatch.</b> When every advanced argument is at its Serilog default
/// (<see cref="RollingInterval.Infinite"/>, no retention cap, no size cap) the
/// mapper forwards to the cheap 2-arg <see cref="QuickLogBuilder.WithFileSink(string, string?)"/>
/// — byte-identical to the pre-W1 behaviour. As soon as one advanced argument is
/// set, it forwards to the rich rolling overload.
/// </para>
///
/// <para>
/// <b>Interval coverage.</b> Herald's engine rolls on four periods: none, hourly,
/// daily, and a fixed-minute custom window. <see cref="RollingInterval.Year"/> and
/// <see cref="RollingInterval.Month"/> have no native equivalent and throw
/// <see cref="NotSupportedException"/> rather than silently rolling at a different
/// cadence — a migrated config never writes to an unexpected file layout without
/// notice (Herald's surface-the-anomaly principle).
/// </para>
/// </summary>
internal static class FileSinkVerbMapper
{
    // Native interval tokens the engine understands (Services.JsonConfigProperties):
    //   "none" | "hourly" | "daily" | "custom".
    // Kept as locals at the one use-site below rather than duplicating the engine
    // constants across assemblies — the mapper is the single place the Serilog
    // enum meets the native token, and a unit test pins every pairing.
    private const string IntervalNone = "none";
    private const string IntervalHourly = "hourly";
    private const string IntervalDaily = "daily";
    private const string IntervalCustom = "custom";

    // A 1-minute custom window realises RollingInterval.Minute: the engine's custom
    // period rolls at fixed wall-clock boundaries of captureDurationMinutes length.
    private const int MinuteWindowDurationMinutes = 1;

    /// <summary>
    /// Apply a Serilog-shaped File sink to <paramref name="builder"/>, choosing the
    /// cheap or rich native overload based on whether any advanced argument is set.
    /// </summary>
    /// <param name="builder">The native builder to mutate.</param>
    /// <param name="path">The file path (extension still drives JSON-vs-text inference).</param>
    /// <param name="restrictedToMinimumLevel">Per-sink floor; Verbose means "no restriction".</param>
    /// <param name="rollingInterval">The Serilog rolling cadence.</param>
    /// <param name="retainedFileCountLimit">Max rolled files to keep, or null for unbounded.</param>
    /// <param name="fileSizeLimitBytes">Per-file size cap in bytes, or null for unbounded.</param>
    internal static void Apply(
        QuickLogBuilder builder,
        string path,
        LogEventLevel restrictedToMinimumLevel,
        RollingInterval rollingInterval,
        int? retainedFileCountLimit,
        long? fileSizeLimitBytes)
    {
        var minLevel = ToHeraldFloor(restrictedToMinimumLevel);

        // Cheap path: no rolling. The dispatch keys on the rolling interval alone —
        // retention and size caps are rolled-FILE management and have no effect
        // without rolling (exactly as in Serilog, where retainedFileCountLimit prunes
        // rolled files and there are none when rollingInterval is Infinite). Keeping
        // the cheap path here preserves byte-for-byte parity with the bare native
        // WithFileSink(path) — what File("app.log") produced before W1.
        if (rollingInterval == RollingInterval.Infinite)
        {
            builder.WithFileSink(path, minLevel: minLevel);
            return;
        }

        // Rich path: rolling is active. Translate the interval to a native token (and
        // a custom-window duration for the Minute case) and carry the retention/size
        // caps, which now have rolled files to act on.
        var (intervalToken, captureDurationMinutes) = ToNativeInterval(rollingInterval);

        builder.WithFileSink(
            path,
            interval: intervalToken,
            maxBytes: fileSizeLimitBytes,
            maxRetainedFiles: retainedFileCountLimit,
            captureDurationMinutes: captureDurationMinutes,
            minLevel: minLevel);
    }

    // Verbose → null (inherit pipeline floor), anything else → the Herald key.
    // Mirrors LoggerSinkConfiguration.Floor so the File verb and the mapper agree.
    private static string? ToHeraldFloor(LogEventLevel level)
        => level == LogEventLevel.Verbose ? null : SerilogLevelMap.ToHerald(level).Key;

    /// <summary>
    /// Map a Serilog <see cref="RollingInterval"/> to a native interval token and the
    /// custom-window duration that token needs (60 for non-custom periods, where the
    /// engine ignores it). Exposed internally so the W1 unit test can pin every value.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// <paramref name="interval"/> is <see cref="RollingInterval.Year"/> or
    /// <see cref="RollingInterval.Month"/> — periods the engine cannot roll on.
    /// </exception>
    internal static (string Token, int CaptureDurationMinutes) ToNativeInterval(RollingInterval interval)
        => interval switch
        {
            RollingInterval.Infinite => (IntervalNone, 60),
            RollingInterval.Hour     => (IntervalHourly, 60),
            RollingInterval.Day      => (IntervalDaily, 60),
            RollingInterval.Minute   => (IntervalCustom, MinuteWindowDurationMinutes),
            RollingInterval.Year or RollingInterval.Month => throw new NotSupportedException(
                $"RollingInterval.{interval} has no Herald file-engine equivalent — the engine rolls on " +
                "none/hourly/daily/per-minute boundaries only. Use RollingInterval.Day for the nearest " +
                "calendar cadence, or pre-roll externally. (Silently rolling at a different cadence would " +
                "break the drop-in contract.)"),
            _ => throw new NotSupportedException($"Unknown RollingInterval value '{interval}'.")
        };
}
