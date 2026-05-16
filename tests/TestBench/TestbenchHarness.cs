#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Herald.Sinks.File;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Tests.TestBench;

/// <summary>
/// Builds and tears down a 3-tenant configuration for functional testing.
/// Each tenant gets its own QuickLogBuilder pipeline. Two output modes
/// are supported:
///
/// <list type="bullet">
///   <item><b>Bridge mode (default):</b> each tenant's pipeline bridges
///   to a per-tenant <see cref="MemoryNdjsonSink"/> that captures every
///   event as a serialised ndjson line in memory. Used by the tenant-
///   isolation tests because the in-memory capturer is deterministic,
///   has no I/O lifecycle, and isolates the test from the file-sink
///   findings recorded in <c>FINDINGS.md</c>.</item>
///   <item><b>File mode:</b> each tenant writes to its own .ndjson file
///   via Herald.Sinks.File. Used by the file-sink exhibit test that
///   documents the disposal-doesn't-flush finding.</item>
/// </list>
///
/// <para>
/// Per-test isolation: every harness instance creates a unique
/// registry-name suffix so concurrent test methods do not collide on
/// the process-global HeraldRegistry. Test classes that touch the
/// registry must also use <c>[Collection(DefaultHostCollection.Name)]</c>
/// to serialise against the process-global static state.
/// </para>
/// </summary>
internal sealed class TestbenchHarness : IAsyncDisposable
{
    public const string TenantAlpha = "alpha";
    public const string TenantBeta  = "beta";
    public const string TenantGamma = "gamma";

    private readonly string _suffix;
    private readonly List<string> _registeredTenants = new();
    private readonly Dictionary<string, MemoryNdjsonSink> _bridgeSinks =
        new(StringComparer.Ordinal);

    /// <summary>Temp directory for file-mode output. Empty in bridge mode.</summary>
    public string TempDir { get; }
    public string AlphaPath => Path.Combine(TempDir, $"{TenantAlpha}.ndjson");
    public string BetaPath  => Path.Combine(TempDir, $"{TenantBeta}.ndjson");
    public string GammaPath => Path.Combine(TempDir, $"{TenantGamma}.ndjson");

    public TestbenchHarness()
    {
        _suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        TempDir = Path.Combine(Path.GetTempPath(), $"herald-testbench-{_suffix}");
        Directory.CreateDirectory(TempDir);
    }

    public string PipelineName(string tenant) => $"testbench-{tenant}-{_suffix}";

    // ---------- bridge-mode registration (in-memory capture) ----------

    /// <summary>
    /// Register three tenants in bridge mode. Each tenant's pipeline
    /// routes events to a per-tenant <see cref="MemoryNdjsonSink"/>.
    /// Read the captures back via <see cref="CapturedFor"/>.
    /// </summary>
    public void RegisterThreeTenantsInMemory()
    {
        RegisterOneInMemory(TenantAlpha);
        RegisterOneInMemory(TenantBeta);
        RegisterOneInMemory(TenantGamma);
    }

    public void RegisterOneInMemory(string tenant)
    {
        var name = PipelineName(tenant);
        var sink = new MemoryNdjsonSink();
        _bridgeSinks[tenant] = sink;

        // SuppressNamingPolicyAnnouncement keeps the testbench counts
        // clean. Without it, the first dispatch through each
        // StructuredLogger emits a one-shot announcement event that
        // routes through the bridge and skews any "events sent == events
        // captured" assertion by +1 per tenant. The testbench is
        // measuring tenant routing, not the announcement behaviour;
        // suppressing here keeps the failure-mode lens focused.
        var builder = QuickLogBuilder.Create(name)
            .WithMinimumLevel("trace")
            .SuppressNamingPolicyAnnouncement()
            .WithBridge(sink);
        var result = builder.BuildAndCommit();
        HeraldRegistry.Register(tenant, name, builder, result);
        _registeredTenants.Add(tenant);
    }

    /// <summary>
    /// Captured ndjson lines for a tenant, in order of capture. Each
    /// line is one event serialised as a JSON object.
    /// </summary>
    public IReadOnlyList<string> CapturedFor(string tenant) =>
        _bridgeSinks.TryGetValue(tenant, out var sink) ? sink.Snapshot() : Array.Empty<string>();

    public IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> CapturedNdjsonFor(string tenant)
    {
        var lines = CapturedFor(tenant);
        return ParseLines(lines);
    }

    // ---------- file-mode registration (real file sink) ----------

    /// <summary>
    /// Register three tenants in file mode. Each tenant writes to its
    /// own .ndjson file via Herald.Sinks.File. Used by the file-sink
    /// exhibit test that documents the disposal-doesn't-flush finding.
    /// </summary>
    public void RegisterThreeTenantsToFile()
    {
        RegisterOneToFile(TenantAlpha, AlphaPath);
        RegisterOneToFile(TenantBeta,  BetaPath);
        RegisterOneToFile(TenantGamma, GammaPath);
    }

    public void RegisterOneToFile(string tenant, string path)
    {
        var name = PipelineName(tenant);
        var builder = QuickLogBuilder.Create(name)
            .WithFileSinkProviders()
            .WithFileSink(path, "trace");
        var result = builder.BuildAndCommit();
        HeraldRegistry.Register(tenant, name, builder, result);
        _registeredTenants.Add(tenant);
    }

    /// <summary>
    /// Read an ndjson file using shared read+write access so we don't
    /// fight the file sink for the handle. Returns whatever has been
    /// flushed to disk at read time.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> ReadNdjsonFile(string path)
    {
        if (!File.Exists(path)) return Array.Empty<IReadOnlyDictionary<string, JsonElement>>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        var lines = new List<string>();
        string? line;
        while ((line = sr.ReadLine()) is not null) lines.Add(line);
        return ParseLines(lines);
    }

    // ---------- shared ----------

    public StructuredLogger LoggerFor(string tenant)
    {
        using (HeraldTenantScope.Push(tenant))
        {
            return HeraldRegistry.Require(PipelineName(tenant)).Result.Logger;
        }
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> ParseLines(
        IEnumerable<string> lines)
    {
        var parsed = new List<IReadOnlyDictionary<string, JsonElement>>();
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            // Defensive: a partially-written ndjson line (the file-sink
            // finding) throws here. Catch and skip so the exhibit test
            // can observe the truncation without crashing the parser.
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.Clone();
                }
                parsed.Add(dict);
            }
            catch (JsonException)
            {
                // Truncated / torn line — surface via a synthetic marker
                // dictionary so tests asserting line integrity can spot
                // the failure mode.
                var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["__torn__"] = JsonDocument.Parse("true").RootElement.Clone(),
                };
                parsed.Add(dict);
            }
        }
        return parsed;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var tenant in _registeredTenants)
        {
            await HeraldRegistry.RemoveAsync(tenant, PipelineName(tenant)).ConfigureAwait(false);
        }
        _registeredTenants.Clear();
        _bridgeSinks.Clear();

        try
        {
            if (Directory.Exists(TempDir))
                Directory.Delete(TempDir, recursive: true);
        }
        catch
        {
            // Temp cleanup is best-effort; a stuck file handle from the
            // sink-lifecycle finding should not mask the actual test
            // failure. The file-sink exhibit test surfaces that finding
            // directly.
        }

        HeraldTenantScope.Current = HeraldTenant.Default;
    }
}

/// <summary>
/// In-memory ndjson capturer. Implements <see cref="ILogger"/> and
/// serialises each event to a JSON line on the spot, appending to an
/// internal buffer. Thread-safe; deterministic; no I/O lifecycle.
///
/// <para>
/// Bypasses the file-sink disposal issue surfaced by the first
/// testbench run (see <c>FINDINGS.md</c>). The serialisation shape
/// approximates what Herald.Sinks.File's JsonFileSinkProvider would
/// produce — enough to assert tenant routing, ordering, and content
/// isolation without needing the real file pipeline.
/// </para>
/// </summary>
internal sealed class MemoryNdjsonSink : ILogger
{
    private readonly ConcurrentQueue<string> _lines = new();

    public void Log(LogEvent logEvent)
    {
        var json = SerializeEvent(logEvent);
        _lines.Enqueue(json);
    }

    public ValueTask LogAsync(LogEvent logEvent, System.Threading.CancellationToken cancellationToken = default)
    {
        Log(logEvent);
        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<string> Snapshot() => _lines.ToArray();

    private static string SerializeEvent(LogEvent e)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"timestamp\":\"").Append(e.TimeUtc.ToString("O")).Append("\",");
        sb.Append("\"level\":\"").Append(JsonEncodedText.Encode(e.Level.ToString()).ToString()).Append("\",");
        sb.Append("\"category\":\"").Append(JsonEncodedText.Encode(e.Category.ToString()).ToString()).Append("\",");
        sb.Append("\"messageTemplate\":\"").Append(JsonEncodedText.Encode(e.MessageTemplate).ToString()).Append("\",");
        sb.Append("\"message\":\"").Append(JsonEncodedText.Encode(e.Message).ToString()).Append('"');
        foreach (var prop in e.Properties)
        {
            sb.Append(',');
            sb.Append('"').Append(JsonEncodedText.Encode(prop.Name).ToString()).Append("\":");
            AppendValue(sb, prop.Value);
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendValue(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null"); break;
            case string s:
                sb.Append('"').Append(JsonEncodedText.Encode(s).ToString()).Append('"'); break;
            case bool b:
                sb.Append(b ? "true" : "false"); break;
            case int or long or short or byte or sbyte or uint or ulong or ushort:
                sb.Append(value); break;
            case double d:
                sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
            case float f:
                sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
            default:
                sb.Append('"').Append(JsonEncodedText.Encode(value.ToString() ?? string.Empty).ToString()).Append('"'); break;
        }
    }
}
