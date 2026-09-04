#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Addons.OtlpSinks;
using MMP.Herald.Metrics;
using Xunit;

namespace MMP.Herald.OSS.Tests.OtlpMetricsExporterRetention;

/// <summary>
/// The exporter must not destroy counters it failed to deliver. It used to
/// snapshot with reset before the POST, so a collector outage silently ate one
/// interval of every counter. The counts are the whole product here; losing
/// them is worse than a late send.
/// </summary>
public sealed class OtlpMetricsExporterRetentionTests
{
    /// <summary>Hands back a queued status code per call and records each body.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _script;
        public List<string> Bodies { get; } = [];

        public ScriptedHandler(params HttpStatusCode[] script) {
            _script = new Queue<HttpStatusCode>(script);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var status = _script.Count > 0 ? _script.Dequeue() : HttpStatusCode.OK;
            return new HttpResponseMessage(status);
        }
    }

    // One hour, so the background timer never fires; every export is driven
    // explicitly by the test.
    private const int NoAutoExport = 3_600_000;

    private static OtlpMetricsExporter NewExporter(GameMetrics metrics, ScriptedHandler handler) {
        return new OtlpMetricsExporter(
            metrics,
            endpoint: "http://localhost:4318/v1/metrics",
            exportIntervalMs: NoAutoExport,
            serviceName: "test",
            httpClient: new HttpClient(handler));
    }

    private static long CountIn(string body, string metricName) {
        // The counter arrives as {"name":"<metricName>","sum":{...,"asInt":"<n>",...
        var at = body.IndexOf($"\"name\":\"{metricName}\"", StringComparison.Ordinal);
        if (at < 0) return 0;
        var mark = body.IndexOf("\"asInt\":\"", at, StringComparison.Ordinal);
        var start = mark + "\"asInt\":\"".Length;
        var end = body.IndexOf('"', start);
        return long.Parse(body.AsSpan(start, end - start));
    }

    [Fact]
    public async Task Failed_send_keeps_the_counts_for_the_next_send() {
        var metrics = new GameMetrics();
        var handler = new ScriptedHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        await using var exporter = NewExporter(metrics, handler);

        metrics.Add("physics.collisions", 7);
        await exporter.ExportOnceAsync(CancellationToken.None);   // 500 — nothing delivered

        metrics.Add("physics.collisions", 5);
        await exporter.ExportOnceAsync(CancellationToken.None);   // 200 — carries 7 + 5

        handler.Bodies.Should().HaveCount(2);
        CountIn(handler.Bodies[1], "physics.collisions").Should().Be(12);
    }

    [Fact]
    public async Task Successful_send_clears_the_counts() {
        var metrics = new GameMetrics();
        var handler = new ScriptedHandler(HttpStatusCode.OK, HttpStatusCode.OK);
        await using var exporter = NewExporter(metrics, handler);

        metrics.Add("render.draws", 4);
        await exporter.ExportOnceAsync(CancellationToken.None);

        metrics.Add("render.draws", 3);
        await exporter.ExportOnceAsync(CancellationToken.None);

        CountIn(handler.Bodies[0], "render.draws").Should().Be(4);
        CountIn(handler.Bodies[1], "render.draws").Should().Be(3);
    }

    /// <summary>
    /// Fuzz a random success/failure script. Whatever the outcome sequence, the
    /// sum of the counts the collector accepted plus the counts still held must
    /// equal every count recorded. No path may drop an event.
    /// </summary>
    [Fact]
    public async Task Fuzz_no_count_is_lost_across_any_outcome_sequence() {
        const int seed = 20260904;
        var random = new Random(seed);

        for (var run = 0; run < 200; run++)
        {
            var rounds = random.Next(1, 12);
            var script = new HttpStatusCode[rounds];
            for (var i = 0; i < rounds; i++)
            {
                script[i] = random.Next(2) == 0 ? HttpStatusCode.OK : HttpStatusCode.InternalServerError;
            }

            var metrics = new GameMetrics();
            var handler = new ScriptedHandler(script);
            await using var exporter = NewExporter(metrics, handler);

            long recorded = 0;
            for (var i = 0; i < rounds; i++)
            {
                var amount = random.Next(1, 50);
                recorded += amount;
                metrics.Add("fuzz.counter", amount);
                await exporter.ExportOnceAsync(CancellationToken.None);
            }

            long accepted = 0;
            for (var i = 0; i < handler.Bodies.Count; i++)
            {
                if (script[i] == HttpStatusCode.OK)
                {
                    accepted += CountIn(handler.Bodies[i], "fuzz.counter");
                }
            }

            var held = metrics.Snapshot().Metrics.Single(m => m.Name == "fuzz.counter").Value;

            (accepted + held).Should().Be(
                recorded,
                "run {0} with script [{1}] must lose nothing (seed {2})",
                run, string.Join(",", script.Select(s => (int)s)), seed);
        }
    }
}
