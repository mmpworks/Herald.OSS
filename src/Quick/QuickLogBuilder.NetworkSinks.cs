#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Quick;

// Network / integration sink convenience methods. Extracted from
// QuickLogBuilder.With.cs (principal-review queue #19, partial). The
// builder remains one type — this is a mechanical move-only split
// along the existing "Network / integration sinks" comment seam so
// future contributors can locate the HTTP / TCP / UDP / Elasticsearch /
// webhook / OTLP entries without scrolling through 1.4k lines of
// generic With* methods.
public sealed partial class QuickLogBuilder
{
    /// <summary>
    /// Add an HTTP JSON sink that POSTs batched events to an endpoint.
    /// <paramref name="headers"/> are sent on every request — typically used
    /// for <c>Authorization: Bearer ...</c> against tenant-aware backends.
    /// Values support <c>${ENV_VAR}</c> interpolation via JSON config so
    /// tokens stay out of committed files.
    /// </summary>
    public QuickLogBuilder WithHttpJsonSink(
        string endpoint,
        string? minLevel = null,
        IReadOnlyDictionary<string, string>? headers = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.HttpJson, Uri: endpoint, Host: null, Port: null, MinLevel: minLevel, Headers: headers));
        return this;
    }

    /// <summary>Add a TCP JSON Line sink that streams events over a TCP connection.</summary>
    public QuickLogBuilder WithTcpJsonLineSink(string host, int port, string? minLevel = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.TcpJsonLine, Uri: null, Host: host, Port: port, MinLevel: minLevel));
        return this;
    }

    /// <summary>
    /// Add a UDP JSON Line sink that sends each event as a fire-and-forget
    /// datagram. No retries, no acknowledgements — pair with a durable sink
    /// if every event must survive.
    /// </summary>
    public QuickLogBuilder WithUdpJsonLineSink(string host, int port, string? minLevel = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.UdpJsonLine, Uri: null, Host: host, Port: port, MinLevel: minLevel));
        return this;
    }

    /// <summary>
    /// Add an Elasticsearch sink. <paramref name="headers"/> ride on every
    /// indexing request — typically <c>Authorization: ApiKey ...</c> for
    /// Elastic Cloud or a Basic header for self-hosted clusters.
    /// </summary>
    public QuickLogBuilder WithElasticsearchSink(
        string clusterUrl,
        string? minLevel = null,
        IReadOnlyDictionary<string, string>? headers = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.Elasticsearch, Uri: clusterUrl, Host: null, Port: null, MinLevel: minLevel, Headers: headers));
        return this;
    }

    /// <summary>
    /// Add a Slack webhook sink. When <paramref name="minLevel"/> is null, the
    /// sink applies a deliberate default floor of <c>"error"</c> — Slack
    /// channels are a poor fit for debug / info chatter. Pass an explicit
    /// level to override (including a lower level for audit channels).
    /// </summary>
    public QuickLogBuilder WithSlackWebhookSink(string webhookUrl, string? minLevel = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.SlackWebhook, Uri: webhookUrl, Host: null, Port: null, MinLevel: minLevel ?? "error"));
        return this;
    }

    /// <summary>
    /// Add a generic webhook sink. When <paramref name="minLevel"/> is null,
    /// the sink applies a deliberate default floor of <c>"warn"</c> — generic
    /// webhooks (PagerDuty, Opsgenie, custom on-call bridges) are alert
    /// surfaces rather than log aggregators. <paramref name="headers"/> ride
    /// on every POST — typically used for service-specific auth tokens
    /// (PagerDuty integration keys, Opsgenie API keys, custom HMAC signatures).
    /// </summary>
    public QuickLogBuilder WithWebhookSink(
        string url,
        string? minLevel = null,
        IReadOnlyDictionary<string, string>? headers = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.GenericWebhook, Uri: url, Host: null, Port: null, MinLevel: minLevel ?? "warn", Headers: headers));
        return this;
    }

    // NOTE: The .WithWebhookSink(url, rules, minLevel) overload was removed
    // when the generic webhook sink moved to MMP.Herald.Sinks.GenericWebhook.
    // Consumers wanting a rules engine now call
    // GenericWebhookSinkRegistration.RegisterWithRules(registry, rules)
    // on the sink-provider registry instead. The URL-only overload stays
    // here because it only needs the "webhook" kind string, not the sink.

    /// <summary>
    /// Add an OTLP JSON sink for OpenTelemetry. <paramref name="headers"/>
    /// ride on every request — required by tenant-aware OTLP backends
    /// (Honeycomb, Grafana Cloud, OpenObserve, Traceway, Datadog OTLP) for
    /// Bearer-token or API-key auth.
    /// </summary>
    public QuickLogBuilder WithOtlpJsonSink(
        string endpoint,
        string? minLevel = null,
        IReadOnlyDictionary<string, string>? headers = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.OtlpJson, Uri: endpoint, Host: null, Port: null, MinLevel: minLevel, Headers: headers));
        return this;
    }

    /// <summary>
    /// Add an OTLP Protobuf sink for OpenTelemetry. <paramref name="headers"/>
    /// ride on every request — same role as on the JSON sink.
    /// </summary>
    public QuickLogBuilder WithOtlpProtobufSink(
        string endpoint,
        string? minLevel = null,
        IReadOnlyDictionary<string, string>? headers = null) {
        _networkSinks.Add(new NetworkSinkConfig(Services.KnownSinkKinds.OtlpProtobuf, Uri: endpoint, Host: null, Port: null, MinLevel: minLevel, Headers: headers));
        return this;
    }
}
