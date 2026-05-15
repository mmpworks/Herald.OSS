#nullable enable

namespace MMP.Herald.Sinks;

/// <summary>
/// Marker interface declaring that a sink performs network or disk I/O
/// in its delivery path — HTTP POSTs, TCP/UDP writes, file rotation,
/// OTLP exporters, cloud-service SDK calls, anything that can block
/// the producer thread under load.
///
/// <para>
/// <b>What it does.</b> The marker is informational. Herald.OSS uses
/// it at pipeline construction to detect a common adopter mistake:
/// wiring a network sink without <c>WithAsync()</c>, so every emit
/// blocks the calling thread until the I/O completes. The factory
/// surfaces an advisory via
/// <see cref="MMP.Herald.Pipeline.Kernel.KernelDiagnostic.Advisories"/>
/// — there is no runtime gate; the pipeline still builds and runs.
/// </para>
///
/// <para>
/// <b>What it costs at runtime.</b> Nothing. The marker carries no
/// methods; the factory inspects it once at build time. There is no
/// per-event check, no dispatch indirection.
/// </para>
///
/// <para>
/// <b>Who should implement it.</b> Sinks that talk to the network
/// (Datadog, Loki, HTTP/HTTPS POST, OTLP, Elasticsearch, etc.), sinks
/// that talk to remote cloud services (CloudWatch, Application
/// Insights, BigQuery, Azure Tables/Blobs/EventHub, GCP PubSub),
/// sinks that perform potentially-blocking disk I/O at delivery time
/// (rolling file with fsync, NDJSON archive uploads), and any sink
/// whose normal delivery path can take more than ~100 µs.
/// </para>
///
/// <para>
/// <b>Who should NOT implement it.</b> Sinks that consume events
/// in-process synchronously and return quickly: console writers,
/// in-memory buffers, lock-free ring buffers, the null sink,
/// debugger sinks, level filters that decide-and-drop.
/// </para>
/// </summary>
public interface INetworkSink
{
}
