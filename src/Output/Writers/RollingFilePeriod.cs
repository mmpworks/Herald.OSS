#nullable enable

using System;
using MMP.Herald.Configuration.Runtime;

namespace MMP.Herald.Output.Writers;

/// <summary>
/// Pure period-start calculation for rolling file intervals.
/// Stateless - all methods are static. Extracted from RollingFileLineWriter
/// for testability and single responsibility.
/// </summary>
internal static class RollingFilePeriod
{
    public static DateTimeOffset GetPeriodStart(
        DateTimeOffset now, LoggingRuntimeFileRollingPolicy policy) =>
        GetPeriodStart(now, policy.Interval, policy.StartMinute, policy.CaptureDurationMinutes);

    public static DateTimeOffset GetPeriodStart(
        DateTimeOffset now,
        LogFileRollingInterval interval,
        int startMinute = 0,
        int captureDurationMinutes = 60) {
        if (interval == LogFileRollingInterval.Hourly)
            return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);

        if (interval == LogFileRollingInterval.Daily)
            return new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);

        if (interval == LogFileRollingInterval.Custom)
            return GetCustomPeriodStart(now, startMinute, captureDurationMinutes);

        return DateTimeOffset.MinValue;
    }

    private static DateTimeOffset GetCustomPeriodStart(
        DateTimeOffset now, int startMinute, int captureDurationMinutes) {
        if (captureDurationMinutes <= 0) captureDurationMinutes = 60;
        startMinute = Math.Clamp(startMinute, 0, 59);

        var totalMinutes = now.Hour * 60 + now.Minute;
        var minutesSinceStart = totalMinutes - startMinute;
        if (minutesSinceStart < 0) minutesSinceStart += 24 * 60;

        var windowIndex = minutesSinceStart / captureDurationMinutes;
        var windowStartTotalMinutes = startMinute + (windowIndex * captureDurationMinutes);
        windowStartTotalMinutes %= (24 * 60);

        return new DateTimeOffset(
            now.Year, now.Month, now.Day,
            windowStartTotalMinutes / 60,
            windowStartTotalMinutes % 60,
            0, TimeSpan.Zero);
    }
}
