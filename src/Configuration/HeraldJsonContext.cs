#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using MMP.Herald.Configuration.Json;

namespace MMP.Herald.Configuration;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> that binds every
/// JSON-facing config record under <c>Configuration.Json/</c>. The
/// generator walks <see cref="JsonLoggingConfig"/>'s property graph and
/// emits AOT-safe serializers for every type it reaches, so operators
/// that publish with <c>PublishAot=true</c> no longer see the IL2026 /
/// IL3050 warnings that the reflection-based path triggers.
///
/// <para>
/// Runtime options (such as <c>MaxDepth</c>) that do not map to the
/// <see cref="JsonSourceGenerationOptionsAttribute"/> attribute are
/// applied in <see cref="LoggingJsonSerializer"/> by constructing the
/// context against a custom <c>JsonSerializerOptions</c> instance. The
/// static <see cref="Default"/> instance covers the "just use the
/// attribute-level defaults" case.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(JsonLoggingConfig))]
// FileSinkConfig is the dashboard-facing wire format for file-sink
// updates and is not reachable through JsonLoggingConfig's graph, so
// it needs its own entry. Used by HeraldManagementApi.ApplySinkConfig
// via the typed Deserialize overload — keeps that call site AOT-clean.
[JsonSerializable(typeof(FileSinkConfig))]
internal partial class HeraldJsonContext : JsonSerializerContext
{
}
