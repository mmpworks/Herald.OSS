#nullable enable

namespace MMP.Herald.Services;

/// <summary>
/// Well-known JSON property names written to log output by Herald's JSON formatters.
/// These are the keys that appear in serialized log events (JsonFormatter, Utf8JsonFormatter,
/// ElasticsearchLogSink), distinct from configuration property names in JsonConfigProperties.
/// </summary>
public static class JsonOutputKeys
{
    public const string LevelKey = "level_key";
    public const string LevelRank = "level_rank";
    public const string MessageTemplate = "message_template";
    public const string StackTrace = "stackTrace";
}
