#nullable enable

namespace MMP.Herald.Services;

/// <summary>
/// Well-known sink kind identifiers used by built-in sink providers.
/// Use these instead of raw strings when referencing sink types in configuration
/// or when building custom providers that need to match a built-in kind.
/// </summary>
public static class KnownSinkKinds
{
    public const string Console = "console";
    public const string Null = "null";
    public const string TextFile = "text_file";
    public const string JsonFile = "json_file";
    public const string Channel = "channel";
    public const string PipelineBridge = "pipeline_bridge";
    // HTTP / TCP / UDP JSON kind strings stay here even though the providers
    // ship in the Herald.Sinks monorepo — QuickLogBuilder.WithHttpJson /
    // WithTcpJsonLine / WithUdpJsonLine build a sink config with these kinds
    // so consumers discover them through the builder API. Sink registration
    // is a separate step via <Name>SinkRegistration.RegisterAll.
    public const string HttpJson = "http_json";
    public const string TcpJsonLine = "tcp_json_line";
    public const string UdpJsonLine = "udp_json_line";
    public const string Elasticsearch = "elasticsearch";
    public const string SlackWebhook = "slack";
    public const string GenericWebhook = "webhook";
    public const string OtlpJson = "otlp_json";
    public const string OtlpProtobuf = "otlp_protobuf";
    public const string ProtobufFile = "protobuf_file";
    public const string ApplicationInsightsSdk = "application_insights_sdk";
    public const string ApplicationInsightsHttp = "application_insights_http";
    public const string AzureTableStorage = "azure_table_storage";
}
