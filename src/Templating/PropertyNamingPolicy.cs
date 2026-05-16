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
/// The Herald.OSS 1.0 default is <see cref="Pascal"/>. Consumers preserving
/// pre-1.0 behavior should opt in to <see cref="Camel"/>. OpenTelemetry- and
/// Python-flavored downstreams typically want <see cref="Snake"/>.
/// </para>
/// </summary>
public static class PropertyNamingPolicy
{
    /// <summary>
    /// Default policy. Template tokens drive property names, first-letter cased.
    /// Matches the Serilog / NLog / Microsoft.Extensions.Logging convention.
    /// </summary>
    public static IPropertyNamingPolicy Pascal => PascalCasePolicy.Instance;

    /// <summary>
    /// Caller-argument-expression name wins. Preserves the pre-1.0 Herald
    /// behavior; use this when downstream sinks were configured against the
    /// old shape and the wire format must not change.
    /// </summary>
    public static IPropertyNamingPolicy Camel => CamelCasePolicy.Instance;

    /// <summary>
    /// Template tokens drive property names, converted to <c>snake_case</c>.
    /// Coalesces uppercase runs so <c>HTTPClient</c> becomes <c>http_client</c>,
    /// not <c>h_t_t_p_client</c>.
    /// </summary>
    public static IPropertyNamingPolicy Snake => SnakeCasePolicy.Instance;
}
