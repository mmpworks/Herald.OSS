#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;

namespace MMP.Herald.Enrichers;

/// <summary>
/// Adds service identity context to every log event: name, version, region, environment.
/// Follows OpenTelemetry resource semantic conventions (service.name, service.version).
///
/// Stripe Principle #5 (Four W's: Where?) and #9 (consistent identifiers across services):
/// every event carries the service identity so aggregation tools can group, filter, and
/// alert per-service without relying on external metadata.
///
/// Usage:
///   builder.Enrichers.Add(new ServiceIdentityEnricher("payments-api", version: "2.1.0",
///       region: "us-east-1", environment: "production"));
/// </summary>
public sealed class ServiceIdentityEnricher : ILogEnricher
{
    private readonly string _serviceName;
    private readonly string? _serviceVersion;
    private readonly string? _region;
    private readonly string? _environment;

    public ServiceIdentityEnricher(
        string serviceName,
        string? version = null,
        string? region = null,
        string? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        _serviceName = serviceName;
        _serviceVersion = version;
        _region = region;
        _environment = environment;
    }

    public void Enrich(LogEventEnrichmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.SetContextValue(Services.LogContextKeys.ServiceName, _serviceName);

        if (_serviceVersion is not null)
            context.SetContextValue(Services.LogContextKeys.ServiceVersion, _serviceVersion);

        if (_region is not null)
            context.SetContextValue(Services.LogContextKeys.ServiceRegion, _region);

        if (_environment is not null)
            context.SetContextValue(Services.LogContextKeys.ServiceEnvironment, _environment);
    }

    public Configuration.Json.JsonEnricherConfig ToJsonConfig()
    {
        // Only emit non-null parameters so the JSON shape stays compact and a
        // round-tripped enricher restores the same constructor arguments —
        // including the asymmetry that null version/region/environment skip
        // their context-key writes in Enrich(...).
        var props = new Dictionary<string, object?>(System.StringComparer.Ordinal)
        {
            ["serviceName"] = _serviceName,
        };
        if (_serviceVersion is not null) props["version"] = _serviceVersion;
        if (_region is not null) props["region"] = _region;
        if (_environment is not null) props["environment"] = _environment;

        return new Configuration.Json.JsonEnricherConfig(nameof(ServiceIdentityEnricher), props);
    }
}
