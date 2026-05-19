#nullable enable

using System;
using System.Text.Json;
using MMP.Herald.Configuration.Json;

namespace MMP.Herald.Configuration;

/// <summary>
/// Handles JSON serialization / deserialization for logging configuration.
/// AOT-safe: routes every call through the source-generated
/// <see cref="HeraldJsonContext"/> so no runtime reflection is required.
///
/// <para>
/// The deserialize path enforces two defense-in-depth caps: <see cref="MaxInputBytes"/>
/// rejects pathological JSON before the parser sees it, and
/// <see cref="MaxJsonDepth"/> caps recursive nesting inside the parser.
/// Both matter because hot-reload reads from a config file that may be
/// edited mid-flight; a zip-bomb-style deeply-nested array could otherwise
/// consume the process.
/// </para>
/// </summary>
public static class LoggingJsonSerializer
{
    /// <summary>
    /// Defense-in-depth cap on input size. A pathological config file (or a
    /// hostile hot-reload source) cannot balloon memory before we get to the
    /// typed deserializer. 1 MiB is well above any realistic config.
    /// </summary>
    public const int MaxInputBytes = 1_048_576;

    /// <summary>
    /// Defense-in-depth cap on nesting depth. Matches
    /// <see cref="JsonSerializerOptions.MaxDepth"/> used by
    /// <see cref="CreateOptions"/>.
    /// </summary>
    public const int MaxJsonDepth = 32;

    // Source-generated context bound to a JsonSerializerOptions instance
    // that carries the MaxDepth cap and the repo-wide read defaults. The
    // ObjectDictionaryJsonConverter handles JsonPipelineStepConfig.Config
    // (IReadOnlyDictionary<string, object?>) — source gen cannot emit
    // metadata for polymorphic `object` values.
    private static readonly HeraldJsonContext _context = new(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = MaxJsonDepth,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new Json.ObjectDictionaryJsonConverter(), new Json.EnrichersJsonConverter(), new Json.PipelineDecoratorsJsonConverter() },
    });

    private static readonly HeraldJsonContext _indentedContext = new(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new Json.ObjectDictionaryJsonConverter(), new Json.EnrichersJsonConverter(), new Json.PipelineDecoratorsJsonConverter() },
    });

    /// <summary>
    /// Deserialize a logging configuration JSON string.
    ///
    /// <para>
    /// <b>Environment-variable interpolation.</b> When
    /// <paramref name="expandEnv"/> is <c>true</c> (the default), every
    /// <c>${NAME}</c> and <c>${NAME:-default}</c> token in the input is
    /// expanded against the process environment via
    /// <see cref="ConfigEnvInterpolator.Expand"/> before parsing. This lets
    /// operators keep Bearer tokens, connection strings, and API keys out of
    /// committed JSON. Unresolved tokens (the form without <c>:-default</c>)
    /// throw <see cref="InvalidOperationException"/> listing every missing
    /// variable. The interpolator JSON-escapes substituted values, so a
    /// value containing <c>"</c> or control characters does not break the
    /// surrounding JSON.
    /// </para>
    ///
    /// <para>
    /// Consumers that already perform their own <see cref="ConfigEnvInterpolator.Expand"/>
    /// pass at a different layer (Herald.Server, Herald.Lean both do this so
    /// they can catch the resolution exception inside their own error-reporting
    /// surface) can pass <c>expandEnv: false</c> to keep that pattern.
    /// The default-true path is idempotent on already-expanded text — the
    /// interpolator early-outs when no <c>${</c> tokens remain — so leaving
    /// the default in place is also safe for those consumers.
    /// </para>
    ///
    /// <para>
    /// The <see cref="MaxInputBytes"/> cap is enforced on the
    /// <i>post-expansion</i> payload so an env var that balloons the JSON
    /// cannot bypass the limit.
    /// </para>
    /// </summary>
    /// <summary>
    /// Single-arg overload that delegates to
    /// <see cref="Deserialize(string, bool)"/> with <c>expandEnv: true</c>.
    /// ApiCompat treats the optional bool arg as a distinct signature, so
    /// downstream callers using the explicit single-arg call need this
    /// overload as a separate metadata entry. Behaviour identical to the
    /// default of the two-arg form.
    /// </summary>
    public static JsonLoggingConfig Deserialize(string json) => Deserialize(json, expandEnv: true);

    public static JsonLoggingConfig Deserialize(string json, bool expandEnv = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        if (expandEnv)
        {
            // Defaults-on so direct OSS adopters get the ${ENV} pattern for free.
            // Already-expanded input is a fast no-op inside Expand (early-out
            // when no "${" substring is present). See ConfigEnvInterpolator.
            json = ConfigEnvInterpolator.Expand(json);
        }

        if (json.Length > MaxInputBytes)
        {
            throw new InvalidOperationException(
                $"Logging configuration JSON is {json.Length:N0} chars; refusing to deserialize above {MaxInputBytes:N0}.");
        }

        var config = JsonSerializer.Deserialize(json, _context.JsonLoggingConfig);

        if (config is null)
        {
            throw new InvalidOperationException("Failed to deserialize logging configuration.");
        }

        return config;
    }

    /// <summary>
    /// Serialize a logging configuration to JSON.
    /// Used by QuickLogBuilder.ExportConfig() to snapshot programmatic config.
    /// </summary>
    public static string Serialize(JsonLoggingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return JsonSerializer.Serialize(config, _indentedContext.JsonLoggingConfig);
    }

    public static JsonSerializerOptions CreateOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            MaxDepth = MaxJsonDepth,
        };
    }
}
