// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using MMP.Herald.Templating.NamingPolicies;

namespace MMP.Herald.Templating;

/// <summary>
/// Discoverable access points for the three built-in property-naming policies.
/// Consumers reach the policies through this class rather than constructing
/// new instances directly:
///
/// <code>
/// QuickLogBuilder.Create()
///     .WithNamingPolicy(PropertyNamingPolicy.Snake)
///     .WithConsoleSink()
///     .BuildAndCommit();
/// </code>
///
/// <para>
/// The Herald.OSS 1.0 default is <see cref="Pascal"/>. All three built-ins
/// are token-first; they only differ in the casing transform applied to the
/// selected source. OpenTelemetry- and Python-flavored downstreams typically
/// want <see cref="Snake"/>. JavaScript-flavored downstreams typically want
/// <see cref="Camel"/>.
/// </para>
/// </summary>
public static class PropertyNamingPolicy
{
    /// <summary>
    /// Default policy. Template tokens drive property names, first-letter cased
    /// upward. Matches the Serilog / NLog / Microsoft.Extensions.Logging convention.
    /// </summary>
    public static IPropertyNamingPolicy Pascal => PascalCasePolicy.Instance;

    /// <summary>
    /// Template tokens drive property names, first-letter cased downward.
    /// Mirror of <see cref="Pascal"/>'s restraint with the case test inverted —
    /// already-camel and underscored tokens pass through unchanged. Use this
    /// when downstream consumers (JavaScript schemas, JSON APIs) expect
    /// camelCase property keys.
    /// </summary>
    public static IPropertyNamingPolicy Camel => CamelCasePolicy.Instance;

    /// <summary>
    /// Template tokens drive property names, converted to <c>snake_case</c>.
    /// Coalesces uppercase runs so <c>HTTPClient</c> becomes <c>http_client</c>,
    /// not <c>h_t_t_p_client</c>.
    /// </summary>
    public static IPropertyNamingPolicy Snake => SnakeCasePolicy.Instance;
}
