#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MMP.Herald.Addons.Query;

/// <summary>
/// Searches NDJSON and plain-text Herald log files with filtering.
/// Supports level, category, text search, property key/value, date
/// range, and pagination.
/// </summary>
/// <remarks>
/// <para>
/// Moved here from <c>Herald.Server</c> so every Herald tier — not
/// just Server-hosted deployments — can query its own on-disk logs
/// in-process. <c>Herald.Embed</c> users (games, desktop apps) can
/// wire this into a crash-reporter or "show my last N errors" UI
/// without spinning up an HTTP surface. <c>Herald.Lean</c> can
/// expose a <c>--query</c> CLI for ops workflows. Batch tooling and
/// offline audit jobs link directly against Core.
/// </para>
/// <para>
/// Community-tier: the searcher is pure BCL (<see cref="File.ReadLines(string)"/>,
/// <see cref="JsonDocument"/>, <see cref="Regex"/>) and does not
/// reach into anything gated. A Community build of Herald can use
/// it without touching the edition-gate registry.
/// </para>
/// <para>
/// Thread-safety: stateless static class. Multiple callers can
/// search the same file concurrently; each gets its own lazy
/// file enumerator.
/// </para>
/// <para>
/// Input shapes: two per-line formats are recognised.
/// </para>
/// <list type="bullet">
///   <item><b>NDJSON</b> — one JSON event per line (the output of
///     <c>JsonFileSink</c>). Properties are read directly.</item>
///   <item><b>Plain text</b> — the console-rendered format
///     <c>[timestamp] [LEVEL:rank] Category: message</c>. Parsed
///     via regex into a projected JSON shape so the same filter
///     code applies to both inputs. Properties are not available
///     from the plain-text form — <c>propKey</c>/<c>propValue</c>
///     filters will not match plain-text lines.</item>
/// </list>
/// </remarks>
public static class LogFileSearcher
{
    // Plain text log line: [timestamp] [LEVEL:rank] Category: message.
    // Named captures are mandatory per the repo-wide regex rule.
    private static readonly Regex PlainTextPattern = new(
        @"^\[(?<time>[^\]]+)\]\s+\[(?<level>[A-Z]+):(?<rank>\d+)\]\s+(?<category>\w+):\s+(?<message>.+)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Scan <paramref name="path"/> line by line and return the
    /// window of events matching every non-null filter. The scan
    /// is streaming; memory use tracks <paramref name="take"/>,
    /// not file size.
    /// </summary>
    /// <param name="path">Absolute or relative path to the log
    ///   file. The caller is responsible for path validation and
    ///   access control — this method does not re-check either.</param>
    /// <param name="level">Exact match on the event's levelKey,
    ///   case-insensitive. Null or empty disables the filter.</param>
    /// <param name="category">Substring match on the event's
    ///   category, case-insensitive.</param>
    /// <param name="search">Substring match on the rendered
    ///   message or the template, case-insensitive.</param>
    /// <param name="propKey">Require the event to carry this
    ///   property name.</param>
    /// <param name="propValue">When <paramref name="propKey"/> is
    ///   set, require the property's value to contain this
    ///   substring (case-insensitive).</param>
    /// <param name="from">Inclusive lower bound on the event's
    ///   timestamp. ISO-8601 or any string
    ///   <see cref="DateTimeOffset.TryParse(string?, out DateTimeOffset)"/>
    ///   accepts.</param>
    /// <param name="to">Inclusive upper bound on the event's
    ///   timestamp.</param>
    /// <param name="skip">Number of matches to skip before
    ///   collecting into the return page.</param>
    /// <param name="take">Maximum number of matches to return in
    ///   the current page.</param>
    public static LogFileSearchResult Search(
        string path, string? level, string? category, string? search,
        string? propKey, string? propValue, string? from, string? to,
        int skip, int take)
    {
        var matched = new List<JsonElement>();
        var totalMatched = 0;
        var totalLines = 0;

        DateTimeOffset? fromDate = !string.IsNullOrEmpty(from) && DateTimeOffset.TryParse(from, out var fd) ? fd : null;
        DateTimeOffset? toDate = !string.IsNullOrEmpty(to) && DateTimeOffset.TryParse(to, out var td) ? td : null;

        foreach (var line in File.ReadLines(path))
        {
            totalLines++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonElement doc;
            if (line.StartsWith('{'))
            {
                try { doc = JsonDocument.Parse(line).RootElement; }
                catch { continue; }
            }
            else
            {
                doc = ParsePlainTextLine(line);
                if (doc.ValueKind == JsonValueKind.Undefined) continue;
            }

            if (!MatchesFilters(doc, level, category, search, propKey, propValue, fromDate, toDate))
                continue;

            totalMatched++;
            if (totalMatched <= skip) continue;
            if (matched.Count < take) matched.Add(doc.Clone());
        }

        return new LogFileSearchResult(matched, totalMatched, totalLines, skip, take);
    }

    private static JsonElement ParsePlainTextLine(string line)
    {
        var match = PlainTextPattern.Match(line);
        if (!match.Success) return default;

        var levelAbbr = match.Groups["level"].Value;
        var levelKey = levelAbbr switch
        {
            "TRC" => "verbose",
            "DBG" => "debug",
            "INF" => "information",
            "WRN" => "warning",
            "ERR" => "error",
            "CRT" => "fatal",
            _ => levelAbbr.ToLowerInvariant()
        };

        // Written against Utf8JsonWriter directly — reflection-based
        // JsonSerializer.Serialize<TValue> is RequiresUnreferencedCode /
        // RequiresDynamicCode and would pollute Core's empty AOT
        // inventory, failing the CI gate at .github/workflows/aot-publish.yml.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("time", match.Groups["time"].Value);
            writer.WriteString("level", match.Groups["level"].Value);
            writer.WriteString("levelKey", levelKey);
            writer.WriteString("levelRank", match.Groups["rank"].Value);
            writer.WriteString("category", match.Groups["category"].Value);
            writer.WriteString("message", match.Groups["message"].Value.Trim());
            writer.WriteEndObject();
        }
        stream.Position = 0;
        return JsonDocument.Parse(stream).RootElement;
    }

    // Cognitive Complexity note: each filter criterion is evaluated
    // independently with early-exit on first mismatch. Keeping the
    // chain flat (no nested branching) is what makes a seven-filter
    // predicate readable.
    private static bool MatchesFilters(
        JsonElement doc, string? level, string? category, string? search,
        string? propKey, string? propValue,
        DateTimeOffset? from, DateTimeOffset? to)
    {
        if (!string.IsNullOrEmpty(level))
        {
            var docLevel = doc.TryGetProperty("levelKey", out var lk) ? lk.GetString() : null;
            if (!string.Equals(docLevel, level, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrEmpty(category))
        {
            var docCat = doc.TryGetProperty("category", out var cat) ? cat.GetString() : null;
            if (docCat is null || !docCat.Contains(category, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrEmpty(search))
        {
            var msg = doc.TryGetProperty("message", out var m) ? m.GetString() : null;
            var tmpl = doc.TryGetProperty("messageTemplate", out var mt) ? mt.GetString() : null;
            var inMsg = msg is not null && msg.Contains(search, StringComparison.OrdinalIgnoreCase);
            var inTmpl = tmpl is not null && tmpl.Contains(search, StringComparison.OrdinalIgnoreCase);
            if (!inMsg && !inTmpl)
                return false;
        }

        if (!string.IsNullOrEmpty(propKey) && doc.TryGetProperty("properties", out var props))
        {
            if (!props.TryGetProperty(propKey, out var propObj))
                return false;

            if (!string.IsNullOrEmpty(propValue))
            {
                var actual = propObj.ValueKind == JsonValueKind.Object && propObj.TryGetProperty("value", out var v)
                    ? v.GetString()
                    : propObj.ToString();

                if (actual is null || !actual.Contains(propValue, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        else if (!string.IsNullOrEmpty(propKey))
        {
            return false;
        }

        if (from is not null || to is not null)
        {
            var timeStr = doc.TryGetProperty("time", out var ts) ? ts.GetString() : null;
            if (timeStr is null || !DateTimeOffset.TryParse(timeStr, out var eventTime))
                return false;

            if (from is not null && eventTime < from.Value)
                return false;
            if (to is not null && eventTime > to.Value)
                return false;
        }

        return true;
    }
}

/// <summary>
/// Page of matches returned by <see cref="LogFileSearcher.Search"/>.
/// <see cref="TotalMatched"/> reports the full match count across
/// the whole file (not the current page) so callers can paginate
/// without a second pass. <see cref="TotalLines"/> is the raw line
/// count including non-event lines (blanks, garbled entries).
/// </summary>
public sealed record LogFileSearchResult(
    List<JsonElement> Entries,
    int TotalMatched,
    int TotalLines,
    int Skip,
    int Take);
