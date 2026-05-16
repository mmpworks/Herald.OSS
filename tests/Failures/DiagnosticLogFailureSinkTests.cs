#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Failures;
using MMP.Herald.Levels;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Failures;

/// <summary>
/// Direct contract tests for <see cref="DiagnosticLogFailureSink"/>.
/// The 0.2.2 refactor moved the buffer mechanics into the shared
/// <see cref="MMP.Herald.Diagnostics.BoundedNoticeBuffer{T}"/> type,
/// but the file-mirror concern and the failure-record shape stay
/// local. These tests pin the local behaviour so future buffer-side
/// refactors don't silently change the failure-sink contract.
/// </summary>
public sealed class DiagnosticLogFailureSinkTests : IDisposable
{
    private readonly string _tempDir;

    public DiagnosticLogFailureSinkTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            $"herald-diag-sink-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Cleanup is best-effort; a stuck handle should not
            // mask the actual test failure.
        }
    }

    [Fact]
    public void Eviction_count_matches_buffer_state_after_overflow()
    {
        var sink = new DiagnosticLogFailureSink(maxEntries: 5);
        for (var i = 0; i < 12; i++)
        {
            sink.ReportFailure(BuildEvent($"event-{i}"), new InvalidOperationException("test"), source: "test");
        }

        sink.GetEntries().Should().HaveCount(5);
        sink.DroppedEntryCount.Should().Be(7, "12 reported minus 5 retained = 7 dropped");
    }

    [Fact]
    public void File_mirror_creates_parent_directory_when_missing()
    {
        var nested = Path.Combine(_tempDir, "nested", "deeper", "failures.log");
        File.Exists(nested).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(nested)!).Should().BeFalse();

        var sink = new DiagnosticLogFailureSink(maxEntries: 10, path: nested);
        sink.ReportFailure(BuildEvent("creating-dir"), new Exception("boom"), source: "test.dir");

        File.Exists(nested).Should().BeTrue(
            "the sink must auto-create the parent directory of the configured file path");
        var contents = File.ReadAllText(nested);
        contents.Should().Contain("test.dir").And.Contain("creating-dir");
    }

    [Fact]
    public void Concurrent_report_failure_calls_produce_no_torn_file_lines()
    {
        var path = Path.Combine(_tempDir, "concurrent.log");
        var sink = new DiagnosticLogFailureSink(maxEntries: 1000, path: path);

        const int writers = 8;
        const int writesPerWriter = 50;
        Parallel.For(0, writers, writer =>
        {
            for (var i = 0; i < writesPerWriter; i++)
            {
                sink.ReportFailure(
                    BuildEvent($"msg-w{writer}-i{i}"),
                    new Exception($"err-w{writer}-i{i}"),
                    source: $"source-w{writer}");
            }
        });

        // Every line in the produced file must be a well-formed
        // record — no two writers should interleave bytes within a
        // single line. The sink's file lock guards this.
        var lines = File.ReadAllLines(path);
        lines.Length.Should().Be(writers * writesPerWriter);
        foreach (var line in lines)
        {
            line.Should().NotBeNullOrWhiteSpace();
            line.Should().Contain("source=source-w");
            line.Should().Contain("level=");
            line.Should().Contain("exceptionType=");
        }
    }

    [Fact]
    public void GetEntries_returns_records_in_oldest_first_order()
    {
        var sink = new DiagnosticLogFailureSink(maxEntries: 10);
        sink.ReportFailure(BuildEvent("first"),  new Exception(), source: "src-1");
        sink.ReportFailure(BuildEvent("second"), new Exception(), source: "src-2");
        sink.ReportFailure(BuildEvent("third"),  new Exception(), source: "src-3");

        var entries = sink.GetEntries();
        entries.Should().HaveCount(3);
        entries[0].Message.Should().Be("first");
        entries[2].Message.Should().Be("third");
    }

    [Fact]
    public void DroppedEntryCount_starts_at_zero()
    {
        var sink = new DiagnosticLogFailureSink(maxEntries: 10);
        sink.DroppedEntryCount.Should().Be(0);
    }

    private static LogEvent BuildEvent(string message) => new LogEvent(
        TimeUtc: DateTimeOffset.UtcNow,
        Level: KnownLogLevels.Error,
        Category: LogCategory.App,
        MessageTemplate: message,
        Message: message,
        Properties: Array.Empty<LogProperty>(),
        Context: LogEvent.EmptyContext);
}
