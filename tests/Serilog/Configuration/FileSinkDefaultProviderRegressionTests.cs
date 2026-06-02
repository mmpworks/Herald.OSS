#nullable enable
#if NET9_0_OR_GREATER

using System;
using System.IO;
using System.Threading;
using FluentAssertions;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// Regression — the Serilog-compat <c>WriteTo.File</c> verb must resolve and write
/// a real file out of the box, with NO extra package and NO explicit registration.
///
/// <para>
/// Bug A (FIXED): before File was bundled into Herald.OSS, the <c>text_file</c> /
/// <c>json_file</c> providers lived only in the external <c>MMP.Herald.Sinks.File</c>
/// package, whose <c>[ModuleInitializer]</c> never fired from a bare PackageReference.
/// <c>WriteTo.File</c> compiled, the pipeline accepted the sink, and at runtime it
/// SILENTLY no-opped — green build, working console, empty/absent log file. Discovered
/// executing a real third-party Serilog→Herald migration (SMZ3Randomizer). Bundling the
/// file sink in-core (registered alongside Console) fixes the resolution.
/// </para>
///
/// <para>
/// These tests deliberately do NOT reference <c>MMP.Herald.Sinks.File</c> and do NOT
/// call <c>FileSinkRegistration.RegisterAll</c> — they prove the in-core default
/// registration works with nothing extra.
/// </para>
/// </summary>
public sealed class FileSinkDefaultProviderRegressionTests
{
    private const int FlushPollMs = 2_000;
    private const int FlushPollStepMs = 25;

    /// <summary>
    /// Bug A guard: a <c>.jsonl</c> file sink resolves and the structured payload
    /// (the property value) lands on disk with nothing extra wired in.
    /// </summary>
    [Fact]
    public void WriteTo_File_json_resolves_and_writes_payload_out_of_the_box()
    {
        RunFileSink("app.jsonl", "JSON-FILE-SINK-OOTB", written =>
            written.Should().Contain("JSON-FILE-SINK-OOTB",
                "the bundled json_file provider must resolve for .jsonl paths with no extra package."));
    }

    /// <summary>
    /// Bug A guard: a <c>.log</c> text file sink resolves and writes a real, non-empty
    /// record (timestamp + level). Proves the provider is registered in-core — the
    /// no-op produced no file / an empty file. (Message-body rendering is Bug B below.)
    /// </summary>
    [Fact]
    public void WriteTo_File_text_resolves_and_writes_a_record()
    {
        RunFileSink("app.log", "INF", written =>
        {
            written.Should().NotBeNullOrWhiteSpace(
                "the bundled text_file provider must resolve and write a record — " +
                "an unregistered provider silently no-ops (empty/absent file).");
            written.Should().Contain("INF",
                "the level marker proves the text writer actually rendered and flushed a line.");
        });
    }

    /// <summary>
    /// Bug B (FIXED): on the kernel fast path the text file formatter
    /// (<c>PlainTextFormatter.Format(in LogEventBuffer)</c>) used to append the empty
    /// <c>buffer.Message</c>, producing a record with no body (<c>[ts] [INF:2] : </c>).
    /// The fix materialises + renders the buffer via <c>KernelBufferAdapter</c> (the
    /// same path the base sink uses), so the message holes are filled. JSON file output
    /// is unaffected. This guards that a migrated Serilog app's text log carries its
    /// actual message, not just timestamp + level.
    /// </summary>
    [Fact]
    public void WriteTo_File_text_renders_the_message_body()
    {
        RunFileSink("app.log", "MESSAGE-BODY-RENDERS", written =>
            written.Should().Contain("MESSAGE-BODY-RENDERS",
                "a migrated Serilog app's text log must contain its message, not just timestamp+level."));
    }

    // Shared harness: bare compat surface → WriteTo.File → log → flush → assert.
    private static void RunFileSink(string fileName, string marker, Action<string> assert)
    {
        var dir = Path.Combine(Path.GetTempPath(), "herald-filesink-regress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);

        try
        {
            var logger = new LoggerConfiguration()
                .WriteTo.File(path)
                .CreateLogger();

            logger.Information("regression marker {Marker}", marker);

            // CreateLogger's adapter flushes on Dispose (CloseAndFlush joins the writer).
            (logger as IDisposable)?.Dispose();

            assert(ReadWhenAvailable(path, marker));
        }
        finally
        {
            TryCleanup(dir);
        }
    }

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

#endif
