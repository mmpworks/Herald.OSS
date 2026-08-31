#nullable enable

using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using MMP.Herald.Addons.Query;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.Query;

public sealed class LogFileSearcherTests : IDisposable
{
    // Each test uses its own temp file so the suite can run in parallel.
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"herald-logfile-test-{Guid.NewGuid():N}.log");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private void WriteNdjson(params string[] lines) =>
        File.WriteAllLines(_path, lines);

    private static string NdjsonEvent(
        string time, string levelKey, string category, string message,
        string? messageTemplate = null, object? properties = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("time", time);
            writer.WriteString("level", levelKey.ToUpperInvariant());
            writer.WriteString("levelKey", levelKey);
            writer.WriteString("category", category);
            writer.WriteString("message", message);
            if (messageTemplate is not null) writer.WriteString("messageTemplate", messageTemplate);
            if (properties is not null)
            {
                writer.WritePropertyName("properties");
                writer.WriteRawValue(JsonSerializer.Serialize(properties));
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void Search_returns_every_event_when_no_filter_applies()
    {
        WriteNdjson(
            NdjsonEvent("2026-04-22T10:00:00Z", "info", "App", "one"),
            NdjsonEvent("2026-04-22T10:00:01Z", "warn", "App", "two"),
            NdjsonEvent("2026-04-22T10:00:02Z", "error", "App", "three"));

        var result = LogFileSearcher.Search(
            _path, null, null, null, null, null, null, null, skip: 0, take: 100);

        result.TotalMatched.Should().Be(3);
        result.TotalLines.Should().Be(3);
        result.Entries.Should().HaveCount(3);
    }

    [Fact]
    public void Search_filters_by_level_exact_match_case_insensitive()
    {
        WriteNdjson(
            NdjsonEvent("2026-04-22T10:00:00Z", "info", "App", "one"),
            NdjsonEvent("2026-04-22T10:00:01Z", "warn", "App", "two"),
            NdjsonEvent("2026-04-22T10:00:02Z", "error", "App", "three"),
            NdjsonEvent("2026-04-22T10:00:03Z", "warn", "App", "four"));

        var result = LogFileSearcher.Search(
            _path, "WARN", null, null, null, null, null, null, 0, 100);

        result.TotalMatched.Should().Be(2);
        result.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void Search_filters_by_category_substring_case_insensitive()
    {
        WriteNdjson(
            NdjsonEvent("2026-04-22T10:00:00Z", "info", "Auth.Login", "x"),
            NdjsonEvent("2026-04-22T10:00:01Z", "info", "Auth.Logout", "y"),
            NdjsonEvent("2026-04-22T10:00:02Z", "info", "Combat", "z"));

        var result = LogFileSearcher.Search(
            _path, null, "auth", null, null, null, null, null, 0, 100);

        result.TotalMatched.Should().Be(2);
    }

    [Fact]
    public void Search_text_filter_matches_message_or_template()
    {
        WriteNdjson(
            NdjsonEvent("2026-04-22T10:00:00Z", "info", "App", "login succeeded"),
            NdjsonEvent("2026-04-22T10:00:01Z", "info", "App", "rendered msg",
                messageTemplate: "login attempt for {User}"),
            NdjsonEvent("2026-04-22T10:00:02Z", "info", "App", "unrelated event"));

        var result = LogFileSearcher.Search(
            _path, null, null, "login", null, null, null, null, 0, 100);

        result.TotalMatched.Should().Be(2,
            "filter hits both the rendered message and the template");
    }

    [Fact]
    public void Search_property_key_requires_presence()
    {
        WriteNdjson(
            NdjsonEvent("2026-04-22T10:00:00Z", "info", "App", "a",
                properties: new { UserId = "alice" }),
            NdjsonEvent("2026-04-22T10:00:01Z", "info", "App", "b",
                properties: new { OtherId = "bob" }),
            NdjsonEvent("2026-04-22T10:00:02Z", "info", "App", "c"));

        var result = LogFileSearcher.Search(
            _path, null, null, null, "UserId", null, null, null, 0, 100);

        result.TotalMatched.Should().Be(1);
    }

    [Fact]
    public void Search_property_value_substring_case_insensitive()
    {
        WriteNdjson(
            NdjsonEvent("2026-04-22T10:00:00Z", "info", "App", "a",
                properties: new { UserId = "alice" }),
            NdjsonEvent("2026-04-22T10:00:01Z", "info", "App", "b",
                properties: new { UserId = "bob" }),
            NdjsonEvent("2026-04-22T10:00:02Z", "info", "App", "c",
                properties: new { UserId = "ALICEsan" }));

        var result = LogFileSearcher.Search(
            _path, null, null, null, "UserId", "alice", null, null, 0, 100);

        result.TotalMatched.Should().Be(2,
            "substring match hits both 'alice' and 'ALICEsan'");
    }

    [Fact]
    public void Search_date_range_is_inclusive_and_parses_iso8601()
    {
        WriteNdjson(
            NdjsonEvent("2026-04-22T09:59:59Z", "info", "App", "too early"),
            NdjsonEvent("2026-04-22T10:00:00Z", "info", "App", "on lower bound"),
            NdjsonEvent("2026-04-22T10:00:30Z", "info", "App", "in range"),
            NdjsonEvent("2026-04-22T10:01:00Z", "info", "App", "on upper bound"),
            NdjsonEvent("2026-04-22T10:01:01Z", "info", "App", "too late"));

        var result = LogFileSearcher.Search(
            _path, null, null, null, null, null,
            from: "2026-04-22T10:00:00Z", to: "2026-04-22T10:01:00Z",
            skip: 0, take: 100);

        result.TotalMatched.Should().Be(3);
    }

    [Fact]
    public void Search_pagination_reports_total_but_only_takes_requested_page()
    {
        var lines = new string[20];
        for (var i = 0; i < 20; i++)
        {
            lines[i] = NdjsonEvent(
                $"2026-04-22T10:00:{i:D2}Z", "info", "App", $"message {i}");
        }
        WriteNdjson(lines);

        var page1 = LogFileSearcher.Search(
            _path, null, null, null, null, null, null, null, skip: 0, take: 5);
        var page2 = LogFileSearcher.Search(
            _path, null, null, null, null, null, null, null, skip: 5, take: 5);

        page1.TotalMatched.Should().Be(20);
        page1.Entries.Should().HaveCount(5);
        page2.TotalMatched.Should().Be(20);
        page2.Entries.Should().HaveCount(5);

        var firstMessage = page1.Entries[0].GetProperty("message").GetString();
        var sixthMessage = page2.Entries[0].GetProperty("message").GetString();
        firstMessage.Should().Be("message 0");
        sixthMessage.Should().Be("message 5");
    }

    [Fact]
    public void Search_skips_blank_and_garbled_lines()
    {
        File.WriteAllLines(_path, new[]
        {
            "",
            "not-json-not-plain-text",
            NdjsonEvent("2026-04-22T10:00:00Z", "info", "App", "real event"),
            "   ",
            "{ truncated",
        });

        var result = LogFileSearcher.Search(
            _path, null, null, null, null, null, null, null, 0, 100);

        result.TotalLines.Should().Be(5);
        result.TotalMatched.Should().Be(1,
            "only the one valid NDJSON event should match");
    }

    [Fact]
    public void Search_parses_plain_text_format_when_line_has_no_leading_brace()
    {
        File.WriteAllLines(_path, new[]
        {
            "[2026-04-22 10:00:00.123] [INF:300] App: service started",
            "[2026-04-22 10:00:01.456] [ERR:500] Combat: shot missed",
            "[2026-04-22 10:00:02.789] [WRN:400] Auth: token near expiry"
        });

        var all = LogFileSearcher.Search(
            _path, null, null, null, null, null, null, null, 0, 100);
        var errorsOnly = LogFileSearcher.Search(
            _path, "error", null, null, null, null, null, null, 0, 100);

        all.TotalMatched.Should().Be(3);
        errorsOnly.TotalMatched.Should().Be(1);
    }

    // ---- Reader overload (fork (a) — caller-owned reader) -----------------

    private static readonly string[] KnownCorpus =
    {
        NdjsonEvent("2026-04-22T10:00:00Z", "info", "App", "one"),
        NdjsonEvent("2026-04-22T10:00:01Z", "warn", "Auth", "login failed"),
        NdjsonEvent("2026-04-22T10:00:02Z", "error", "Combat", "shot missed"),
        NdjsonEvent("2026-04-22T10:00:03Z", "warn", "Auth", "token expired"),
    };

    [Fact]
    public void Search_reader_overload_matches_path_overload_on_identical_content()
    {
        // The reader is fed the byte-for-byte content the file holds, so any
        // difference in results would be a difference in the two code paths.
        File.WriteAllLines(_path, KnownCorpus);
        var content = string.Join(Environment.NewLine, KnownCorpus);

        var viaPath = LogFileSearcher.Search(
            _path, "warn", "auth", null, null, null, null, null, 0, 100);

        using var reader = new StringReader(content);
        var viaReader = LogFileSearcher.Search(
            reader, "warn", "auth", null, null, null, null, null, 0, 100);

        viaReader.TotalLines.Should().Be(viaPath.TotalLines);
        viaReader.TotalMatched.Should().Be(viaPath.TotalMatched);
        viaReader.Entries.Should().HaveCount(viaPath.Entries.Count);
        viaReader.TotalMatched.Should().Be(2, "two warn/Auth events are planted");
    }

    [Fact]
    public void Search_reader_overload_sees_only_what_the_reader_yields()
    {
        // No file exists for this test. A bounded reader refuses the oversized
        // second line. If the searcher had ANY fallback to the filesystem it
        // would throw (no path) or read the refused line — it must do neither.
        var oversized = NdjsonEvent(
            "2026-04-22T10:00:01Z", "error", "Combat", new string('x', 5000));
        var content = string.Join(Environment.NewLine, new[]
        {
            NdjsonEvent("2026-04-22T10:00:00Z", "error", "Combat", "kept"),
            oversized,
            NdjsonEvent("2026-04-22T10:00:02Z", "error", "Combat", "also kept"),
        });

        using var reader = new BoundedLineReader(new StringReader(content), maxLineLength: 512);
        var result = LogFileSearcher.Search(
            reader, "error", null, null, null, null, null, null, 0, 100);

        result.TotalLines.Should().Be(2, "the reader refused the oversized line");
        result.TotalMatched.Should().Be(2);
    }

    [Fact]
    public void Search_reader_overload_does_not_dispose_the_callers_reader()
    {
        var content = string.Join(Environment.NewLine, KnownCorpus);
        using var reader = new DisposeTrackingReader(new StringReader(content));

        _ = LogFileSearcher.Search(
            reader, null, null, null, null, null, null, null, 0, 100);

        reader.Disposed.Should().BeFalse(
            "the caller owns the reader's lifetime; the searcher must not dispose it");
    }

    [Fact]
    public void Search_counts_non_blank_unparseable_lines_as_skipped()
    {
        // Two non-blank lines cannot be parsed: one malformed JSON, one that
        // is neither JSON nor the plain-text shape. Blank lines and valid
        // events do not count. Surfacing the count stops an attacker hiding a
        // line by malforming it.
        var content = string.Join(Environment.NewLine, new[]
        {
            NdjsonEvent("2026-04-22T10:00:00Z", "info", "App", "real event"),
            "{ truncated",
            "",
            "not-json-not-plain-text",
            "   ",
        });

        using var reader = new StringReader(content);
        var result = LogFileSearcher.Search(
            reader, null, null, null, null, null, null, null, 0, 100);

        result.SkippedLines.Should().Be(2);
        result.TotalMatched.Should().Be(1);
    }

    // A reader that drops any line longer than the bound — the shape an MCP
    // adapter uses to cap line length before the searcher ever sees a line.
    private sealed class BoundedLineReader : TextReader
    {
        private readonly TextReader _inner;
        private readonly int _maxLineLength;

        public BoundedLineReader(TextReader inner, int maxLineLength)
        {
            _inner = inner;
            _maxLineLength = maxLineLength;
        }

        public override string? ReadLine()
        {
            while (true)
            {
                var line = _inner.ReadLine();
                if (line is null) return null;
                if (line.Length > _maxLineLength) continue;
                return line;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class DisposeTrackingReader : TextReader
    {
        private readonly TextReader _inner;

        public DisposeTrackingReader(TextReader inner) => _inner = inner;

        public bool Disposed { get; private set; }

        public override string? ReadLine() => _inner.ReadLine();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
