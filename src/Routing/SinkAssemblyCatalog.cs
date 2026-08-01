#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;

namespace MMP.Herald.Routing;

/// <summary>
/// Maps every first-party sink kind to the assembly that provides it, and can
/// force-load that assembly so its generated <c>[ModuleInitializer]</c> fires.
///
/// <para>
/// <b>Why this exists.</b> Herald.Sinks.* packages self-register into
/// <see cref="LogSinkProviderRegistry.Default"/> via a module initializer emitted
/// by <c>SinkAutoRegistrationGenerator</c>. A module initializer only runs when its
/// assembly LOADS — and the CLR loads assemblies lazily, on first type reference.
/// A consumer that references a sink package but never touches one of its types
/// (the common case: <c>WithHttpJsonSink(...)</c> builds a config from KIND STRINGS
/// defined in core, and JSON-config pipelines reference no code at all) never
/// triggers the load, the initializer never fires, and resolution finds nothing.
/// Before this catalog existed the result was a silent no-op sink — the
/// "dotnet add package is the whole workflow" promise was broken for exactly the
/// consumers it was written for.
/// </para>
///
/// <para>
/// <b>The map is explicit, not a naming convention.</b> Nine kinds break the
/// snake_case→PascalCase guess (<c>webhook</c>→GenericWebhook, <c>otlp_json</c>/
/// <c>otlp_protobuf</c>/<c>protobuf_file</c>→Otlp, <c>aws_s3</c>→AmazonS3,
/// <c>mssql</c>→MSSqlServer, <c>splunk_hec</c>→Splunk, <c>gcp_logging</c>→
/// GoogleCloudLogging, <c>google_pubsub</c>→GoogleCloudPubSub, …). The table is
/// generated from the Herald.Sinks monorepo's provider <c>KindKey</c> constants;
/// keep it in sync when a sink package is added or renamed.
/// </para>
///
/// <para>
/// <b>AOT / trimming.</b> Under NativeAOT module initializers run eagerly at
/// startup, so recovery is never needed there. Under aggressive trimming an
/// unreferenced sink assembly may be removed entirely — <see cref="TryRecover"/>
/// catches the load failure and resolution falls through to the actionable error.
/// </para>
/// </summary>
internal static class SinkAssemblyCatalog
{
    /// <summary>
    /// Test seam: replaces the assembly-load step. When set, recovery is attempted
    /// for ANY kind and the hook decides success. Never set in production.
    /// </summary>
    internal static Func<string, bool>? LoadOverride;

    // kind → assembly simple name (package id = "MMP." + assembly name).
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aliyun_sls"] = "Herald.Sinks.Aliyun",
        ["application_insights_http"] = "Herald.Sinks.ApplicationInsightsHttp",
        ["application_insights_sdk"] = "Herald.Sinks.ApplicationInsightsSdk",
        ["aws_cloudwatch"] = "Herald.Sinks.AwsCloudWatch",
        ["aws_s3"] = "Herald.Sinks.AmazonS3",
        ["axiom"] = "Herald.Sinks.Axiom",
        ["azure_analytics"] = "Herald.Sinks.AzureAnalytics",
        ["azure_blob"] = "Herald.Sinks.AzureBlobStorage",
        ["azure_cosmosdb"] = "Herald.Sinks.AzureCosmosDB",
        ["azure_event_hub"] = "Herald.Sinks.AzureEventHub",
        ["azure_log_analytics_dcr"] = "Herald.Sinks.AzureLogAnalyticsDcr",
        ["azure_service_bus"] = "Herald.Sinks.AzureServiceBus",
        ["azure_table_storage"] = "Herald.Sinks.AzureTableStorage",
        ["betterstack"] = "Herald.Sinks.BetterStack",
        ["bigquery"] = "Herald.Sinks.BigQuery",
        ["bugsnag"] = "Herald.Sinks.Bugsnag",
        ["cassandra"] = "Herald.Sinks.Cassandra",
        ["clickhouse"] = "Herald.Sinks.ClickHouse",
        ["coralogix"] = "Herald.Sinks.Coralogix",
        ["couchbase"] = "Herald.Sinks.Couchbase",
        ["datadog"] = "Herald.Sinks.Datadog",
        ["debug"] = "Herald.Sinks.Debug",
        ["discord"] = "Herald.Sinks.Discord",
        ["dynamodb"] = "Herald.Sinks.DynamoDB",
        ["dynatrace"] = "Herald.Sinks.Dynatrace",
        ["elasticsearch"] = "Herald.Sinks.Elasticsearch",
        ["elmahio"] = "Herald.Sinks.ElmahIo",
        ["email"] = "Herald.Sinks.Email",
        ["event_log"] = "Herald.Sinks.EventLog",
        ["exceptionless"] = "Herald.Sinks.Exceptionless",
        ["gcp_logging"] = "Herald.Sinks.GoogleCloudLogging",
        ["godot_console"] = "Herald.Sinks.GodotConsole",
        ["google_pubsub"] = "Herald.Sinks.GoogleCloudPubSub",
        ["graylog"] = "Herald.Sinks.Graylog",
        ["hello_world"] = "Herald.Sinks.HelloWorld",
        ["honeycomb"] = "Herald.Sinks.Honeycomb",
        ["http_json"] = "Herald.Sinks.HttpJson",
        ["in_memory"] = "Herald.Sinks.InMemory",
        ["influxdb"] = "Herald.Sinks.InfluxDB",
        ["kafka"] = "Herald.Sinks.Kafka",
        ["kinesis"] = "Herald.Sinks.Kinesis",
        ["loggly"] = "Herald.Sinks.Loggly",
        ["logzio"] = "Herald.Sinks.LogzIo",
        ["loki"] = "Herald.Sinks.Loki",
        ["mezmo"] = "Herald.Sinks.Mezmo",
        ["mongodb"] = "Herald.Sinks.MongoDB",
        ["mqtt"] = "Herald.Sinks.Mqtt",
        ["ms_teams"] = "Herald.Sinks.MicrosoftTeams",
        ["mssql"] = "Herald.Sinks.MSSqlServer",
        ["mysql"] = "Herald.Sinks.MySQL",
        ["nats"] = "Herald.Sinks.Nats",
        ["newrelic_logs"] = "Herald.Sinks.NewRelicLogs",
        ["opensearch"] = "Herald.Sinks.OpenSearch",
        ["otlp_grpc"] = "Herald.Sinks.OtlpGrpc",
        ["otlp_json"] = "Herald.Sinks.Otlp",
        ["otlp_protobuf"] = "Herald.Sinks.Otlp",
        ["pagerduty"] = "Herald.Sinks.PagerDuty",
        ["parquet"] = "Herald.Sinks.Parquet",
        ["postgresql"] = "Herald.Sinks.PostgreSQL",
        ["protobuf_file"] = "Herald.Sinks.Otlp",
        ["pulsar"] = "Herald.Sinks.Pulsar",
        ["rabbitmq"] = "Herald.Sinks.RabbitMQ",
        ["ravendb"] = "Herald.Sinks.RavenDB",
        ["raygun"] = "Herald.Sinks.Raygun",
        ["redis"] = "Herald.Sinks.Redis",
        ["redis_list"] = "Herald.Sinks.RedisList",
        ["rollbar"] = "Herald.Sinks.Rollbar",
        ["sentry"] = "Herald.Sinks.Sentry",
        ["seq"] = "Herald.Sinks.Seq",
        ["signalfx"] = "Herald.Sinks.SignalFx",
        ["slack"] = "Herald.Sinks.Slack",
        ["splunk_hec"] = "Herald.Sinks.Splunk",
        ["sqlite"] = "Herald.Sinks.SQLite",
        ["sqs"] = "Herald.Sinks.Sqs",
        ["stackify"] = "Herald.Sinks.Stackify",
        ["sumologic"] = "Herald.Sinks.SumoLogic",
        ["syslog"] = "Herald.Sinks.Syslog",
        ["tcp_json_line"] = "Herald.Sinks.TcpJsonLine",
        ["telegram"] = "Herald.Sinks.Telegram",
        ["text_writer"] = "Herald.Sinks.TextWriter",
        ["trace"] = "Herald.Sinks.Trace",
        ["twilio"] = "Herald.Sinks.Twilio",
        ["udp_json_line"] = "Herald.Sinks.UdpJsonLine",
        ["unity_console"] = "Herald.Sinks.UnityConsole",
        ["webhook"] = "Herald.Sinks.GenericWebhook",
        ["xunit"] = "Herald.Sinks.XUnit",
        ["zeromq"] = "Herald.Sinks.ZeroMQ",
    };

    /// <summary>
    /// Attempt to load the assembly that provides <paramref name="sinkKind"/> so
    /// its module initializer registers the provider. Returns true when a load
    /// attempt happened and succeeded (registration is the initializer's job —
    /// the caller re-checks the registry rather than trusting this alone).
    /// </summary>
    internal static bool TryRecover(string sinkKind)
    {
        var hook = LoadOverride;
        if (hook is not null)
        {
            return hook(sinkKind);
        }

        if (!Map.TryGetValue(sinkKind, out var assemblyName))
        {
            return false;
        }

        try
        {
            var assembly = Assembly.Load(new AssemblyName(assemblyName));
            // Assembly.Load alone is NOT enough: the CLR runs a module
            // initializer lazily, at first ACCESS to the module — and nothing
            // here accesses a type in the sink assembly. RunModuleConstructor
            // forces the [ModuleInitializer] (the generated auto-registration)
            // to execute now, deterministically. Idempotent: the runtime runs a
            // module constructor at most once.
            System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
                assembly.ManifestModule.ModuleHandle);
            return true;
        }
        catch (Exception ex) when (ex is System.IO.FileNotFoundException
                                    or System.IO.FileLoadException
                                    or BadImageFormatException)
        {
            // Package not referenced (or trimmed away). Not an error here —
            // the caller produces the actionable message.
            return false;
        }
    }

    /// <summary>
    /// Actionable remedy text for a kind that failed to resolve, used by both the
    /// registry's exception and the build-time no-op warning (one home, two exits).
    /// </summary>
    internal static string RemedyFor(string sinkKind) =>
        Map.TryGetValue(sinkKind, out var assemblyName)
            ? $"Reference the 'MMP.{assemblyName}' NuGet package; if the pipeline still cannot " +
              $"resolve it (isolated registry, aggressive trimming), register explicitly via " +
              $"{assemblyName}.<Name>SinkRegistration.RegisterAll(LogSinkProviderRegistry.Default)."
            : "Register a provider for this kind on the registry before building the pipeline " +
              "(auto-registration covers first-party MMP.Herald.Sinks.* packages only).";
}
