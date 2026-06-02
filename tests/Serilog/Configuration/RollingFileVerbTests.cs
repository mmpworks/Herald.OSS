#nullable enable

using System;
using System.IO;
using System.Threading;
using FluentAssertions;
using MMP.Herald.OSS.Tests.Serilog.TestSupport;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// W1 — rolling interval + retained-file-count on the compat <c>WriteTo.File</c> verb.
///
/// <para>
/// Two artifact lenses, both reading what the fix actually produces — never the
/// builder/config object:
/// </para>
/// <list type="number">
///   <item><description>
///     The enum→native-token map (<see cref="FileSinkVerbMapper.ToNativeInterval"/>)
///     is pinned value-by-value. The map is the load-bearing translation; a wrong
///     pairing rolls a migrated app's files on the wrong boundary.
///   </description></item>
///   <item><description>
///     A live run: configure a rolling sink, log, flush, and read the bytes off
///     disk. The exported config carries the native rolling token + retention count.
///   </description></item>
/// </list>
/// </summary>
[Collection(SerilogFileIoCollection.Name)]
public sealed class RollingFileVerbTests
{
    // ── Enum → native token map (pin every RollingInterval value) ───────────────

    [Theory]
    [InlineData(RollingInterval.Infinite, "none")]
    [InlineData(RollingInterval.Hour, "hourly")]
    [InlineData(RollingInterval.Day, "daily")]
    [InlineData(RollingInterval.Minute, "custom")]
    public void ToNativeInterval_maps_supported_value_to_native_token(
        RollingInterval interval, string expectedToken)
    {
        var (token, _) = FileSinkVerbMapper.ToNativeInterval(interval);

        token.Should().Be(expectedToken,
            $"RollingInterval.{interval} must map to the native '{expectedToken}' period token");
    }

    [Fact]
    public void ToNativeInterval_Minute_uses_a_one_minute_custom_window()
    {
        // The engine realises per-minute rolling via the custom period with a
        // 1-minute capture window. A wrong duration would roll on a 60-min boundary.
        var (token, captureDurationMinutes) = FileSinkVerbMapper.ToNativeInterval(RollingInterval.Minute);

        token.Should().Be("custom");
        captureDurationMinutes.Should().Be(1,
            "RollingInterval.Minute must request a 1-minute custom window, not the 60-minute default");
    }

    [Theory]
    [InlineData(RollingInterval.Year)]
    [InlineData(RollingInterval.Month)]
    public void ToNativeInterval_throws_for_intervals_with_no_native_period(RollingInterval interval)
    {
        // Year/Month have no engine equivalent. Throwing (rather than silently
        // approximating to daily) keeps the drop-in honest: a migrated config
        // never rolls at an unexpected cadence without notice.
        var act = () => FileSinkVerbMapper.ToNativeInterval(interval);

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*RollingInterval.{interval}*",
                "Year/Month must fail loudly — the engine cannot roll on those boundaries");
    }

    // ── Live run: rolling config reaches the emitted artifact ───────────────────

    [Fact]
    public void File_with_rolling_and_retention_writes_payload_and_emits_native_rolling_config()
    {
        var dir = Path.Combine(Path.GetTempPath(), "herald-w1-rolling-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "rolling.log");

        try
        {
            var config = new LoggerConfiguration()
                .WriteTo.File(
                    path,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7);

            // Artifact #1: the exported config JSON carries the native rolling
            // token + the retention count. This is what the fix produced from the
            // Serilog-shaped args — proof the rich native overload was taken.
            var json = config.Builder.ExportConfigJson();
            json.Should().Contain("daily",
                "RollingInterval.Day must serialise as the native 'daily' interval token");
            json.Should().Contain("7",
                "retainedFileCountLimit:7 must reach the native MaxRetainedFiles slot");

            // Artifact #2: a real log line lands on disk through the rolling sink —
            // proof the rich overload still resolves a working provider. A rolling
            // sink writes to a dated filename (rolling<suffix>.log), not the literal
            // path, so scan the directory for the marker rather than the exact path.
            var logger = config.CreateLogger();
            logger.Information("w1 rolling marker {Marker}", "W1-ROLL-OK");
            (logger as IDisposable)?.Dispose();

            var written = ReadDirectoryWhenAvailable(dir, "W1-ROLL-OK");
            written.Should().Contain("W1-ROLL-OK",
                "a rolling file sink must still write the event body to disk");
        }
        finally
        {
            TryCleanup(dir);
        }
    }

    [Fact]
    public void File_with_all_default_args_still_writes_out_of_the_box()
    {
        // Default args (Infinite + caps) must keep the bare-file behaviour working:
        // the cheap path is taken for no-rolling, and the file is written.
        var dir = Path.Combine(Path.GetTempPath(), "herald-w1-default-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "plain.log");

        try
        {
            var logger = new LoggerConfiguration()
                .WriteTo.File(path)            // all advanced args at default
                .CreateLogger();

            logger.Information("w1 default marker {Marker}", "W1-DEFAULT-OK");
            (logger as IDisposable)?.Dispose();

            var written = ReadWhenAvailable(path, "W1-DEFAULT-OK");
            written.Should().Contain("W1-DEFAULT-OK",
                "the default-arg File verb must behave exactly like the pre-W1 2-arg form");
        }
        finally
        {
            TryCleanup(dir);
        }
    }

    // ── Disk-read helpers (mirror FileSinkDefaultProviderRegressionTests) ───────

    private const int FlushPollMs = 2_000;
    private const int FlushPollStepMs = 25;

    private static string ReadWhenAvailable(string path, string marker)
    {
        var deadline = Environment.TickCount + FlushPollMs;
        while (Environment.TickCount < deadline)
        {
            if (File.Exists(path))
            {
                var text = ReadShared(path);
                if (text.Contains(marker, StringComparison.Ordinal))
                    return text;
            }
            Thread.Sleep(FlushPollStepMs);
        }

        return File.Exists(path) ? ReadShared(path) : string.Empty;
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Rolling sinks pick a dated filename, so poll the whole directory for any
    // file that contains the marker rather than a single known path.
    private static string ReadDirectoryWhenAvailable(string dir, string marker)
    {
        var deadline = Environment.TickCount + FlushPollMs;
        while (Environment.TickCount < deadline)
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var text = ReadShared(file);
                if (text.Contains(marker, StringComparison.Ordinal))
                    return text;
            }
            Thread.Sleep(FlushPollStepMs);
        }

        return string.Empty;
    }

    private static void TryCleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup; a leaked temp dir must never fail the test.
        }
    }
}

