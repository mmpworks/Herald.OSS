#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Quick;

namespace MMP.Herald.Enrichers;

/// <summary>
/// Instance-scoped enricher-kind registry. The static
/// <see cref="EnricherJsonRegistry"/> facade forwards every call to the
/// default host's instance of this class; tests and multi-tenant hosts
/// that need isolation construct their own <see cref="HeraldHost"/> and
/// use <c>host.Enrichers</c> directly.
/// </summary>
public sealed class EnricherKindRegistry : JsonKindRegistry<ILogEnricher>
{
    public EnricherKindRegistry()
    {
        RegisterBuiltins();
    }

    /// <summary>
    /// Reconstruct an enricher from its JSON config. Convenience over the
    /// base <c>Reconstruct(kind, properties)</c> for the common
    /// <see cref="JsonEnricherConfig"/>-shaped call site.
    /// </summary>
    public ILogEnricher Reconstruct(JsonEnricherConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Reconstruct(config.Kind, config.Properties);
    }

    protected override string BuildUnknownKindMessage(string kind) =>
        $"Unknown enricher kind '{kind}'. " +
        "Built-ins (ServiceIdentityEnricher, CorrelationIdEnricher, ActivityEnricher, " +
        "MachineNameLogEnricher, ProcessIdLogEnricher, ThreadIdLogEnricher, " +
        "ExceptionDetailEnricher) are registered automatically. Plugin enrichers must " +
        "call EnricherJsonRegistry.Register(kind, factory) — or, for an isolated host, " +
        "host.Enrichers.Register(kind, factory) — at plugin initialization.";

    private void RegisterBuiltins()
    {
        Register(nameof(ServiceIdentityEnricher), static props =>
        {
            var serviceName = ReadString(props, "serviceName")
                ?? throw new InvalidOperationException(
                    "ServiceIdentityEnricher JSON config missing required 'serviceName' property.");
            return new ServiceIdentityEnricher(
                serviceName: serviceName,
                version: ReadString(props, "version"),
                region: ReadString(props, "region"),
                environment: ReadString(props, "environment"));
        });

        Register(nameof(CorrelationIdEnricher), static _ => new CorrelationIdEnricher());
        Register(nameof(ActivityEnricher), static _ => new ActivityEnricher());
        Register(nameof(MachineNameLogEnricher), static _ => new MachineNameLogEnricher());
        Register(nameof(ProcessIdLogEnricher), static _ => new ProcessIdLogEnricher());
        Register(nameof(ThreadIdLogEnricher), static _ => new ThreadIdLogEnricher());
        Register(nameof(ExceptionDetailEnricher), static _ => new ExceptionDetailEnricher());
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?>? props, string key)
    {
        if (props is null) return null;
        return props.TryGetValue(key, out var value) ? value as string : null;
    }
}
