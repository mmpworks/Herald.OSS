#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using MMP.Herald.Configuration;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.NetworkSinks;

/// <summary>
/// HTTP / OTLP sink builder methods accept an optional headers map so
/// adopters can authenticate against tenant-aware backends (Traceway,
/// OpenObserve, Honeycomb, Grafana Cloud, Datadog OTLP, etc.). Headers
/// must round-trip through JSON config exactly so a hot-reload cycle
/// doesn't silently drop them. These tests pin the contract end-to-end:
/// builder → JSON → builder again.
/// </summary>
public sealed class NetworkSinkHeadersTests
{
    [Fact]
    public void Otlp_json_sink_carries_headers_to_export()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer test-token",
            ["X-Tenant-Id"] = "acme",
        };

        var builder = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithOtlpJsonSink("https://example.com/v1/logs", "info", headers);

        var json = builder.Build().ExportConfig();

        var sink = FindSinkByKind(json, "otlp_json");
        var emitted = sink.GetProperty("properties").GetProperty("headers");
        emitted.GetProperty("Authorization").GetString().Should().Be("Bearer test-token");
        emitted.GetProperty("X-Tenant-Id").GetString().Should().Be("acme");
    }

    [Fact]
    public void Http_json_sink_carries_headers_to_export()
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Api-Key"] = "secret-value",
        };

        var builder = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithHttpJsonSink("https://example.com/ingest", "warn", headers);

        var json = builder.Build().ExportConfig();

        var sink = FindSinkByKind(json, "http_json");
        sink.GetProperty("properties").GetProperty("headers")
            .GetProperty("X-Api-Key").GetString().Should().Be("secret-value");
    }

    [Fact]
    public void Webhook_sink_carries_headers_to_export()
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Routing-Key"] = "pagerduty-integration-key",
        };

        var builder = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithWebhookSink("https://events.pagerduty.com/...", "warn", headers);

        var json = builder.Build().ExportConfig();

        var sink = FindSinkByKind(json, "webhook");
        sink.GetProperty("properties").GetProperty("headers")
            .GetProperty("X-Routing-Key").GetString().Should().Be("pagerduty-integration-key");
    }

    [Fact]
    public void No_headers_means_properties_is_absent()
    {
        // When headers are not supplied, the sink's properties bag stays
        // null so adopters using the OTLP path without auth see exactly
        // the pre-existing JSON shape.
        var builder = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithOtlpJsonSink("https://example.com/v1/logs", "info");

        var json = builder.Build().ExportConfig();

        var sink = FindSinkByKind(json, "otlp_json");
        var hasProperties = sink.TryGetProperty("properties", out var props);
        if (hasProperties)
        {
            (props.ValueKind == JsonValueKind.Null || props.ValueKind == JsonValueKind.Undefined)
                .Should().BeTrue("absent headers should not synthesise a properties bag");
        }
    }

    [Fact]
    public void Env_var_interpolation_resolves_header_values_on_deserialize()
    {
        // Operator writes ${TOKEN_VAR} in the JSON config; the deserializer
        // expands it against the environment before parse. The substituted
        // value lands in the sink's headers map for the runtime sink to use.
        const string envVar = "HERALD_TEST_HEADER_TOKEN";
        Environment.SetEnvironmentVariable(envVar, "real-bearer-token");
        try
        {
            var json = $$"""
            {
              "pipelineName": "test",
              "sinks": [
                {
                  "name": "otlp_json",
                  "kind": "otlp_json",
                  "uri": "https://example.com/v1/logs",
                  "minLevel": "info",
                  "properties": {
                    "headers": {
                      "Authorization": "Bearer ${{{envVar}}}"
                    }
                  }
                }
              ]
            }
            """;

            var config = LoggingJsonSerializer.Deserialize(json);

            config.Sinks.Should().HaveCount(1);
            var sink = config.Sinks.First();
            sink.Kind.Should().Be("otlp_json");
            sink.Properties.Should().NotBeNull();
            var headersBag = sink.Properties!["headers"];
            headersBag.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>();
            var headers = (IReadOnlyDictionary<string, object?>)headersBag!;
            headers["Authorization"].Should().Be("Bearer real-bearer-token");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    private static JsonElement FindSinkByKind(string json, string kind)
    {
        using var doc = JsonDocument.Parse(json);
        var clonedRoot = doc.RootElement.Clone();
        var sinks = clonedRoot.GetProperty("sinks");
        foreach (var sink in sinks.EnumerateArray())
        {
            if (sink.GetProperty("kind").GetString() == kind) return sink;
        }
        throw new InvalidOperationException(
            $"No sink with kind '{kind}' in exported config: {json}");
    }
}
