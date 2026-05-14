#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MMP.Herald.Enrichers;

namespace MMP.Herald.Configuration.Simple;

/// <summary>
/// Registry of named enricher factories consumed by
/// <see cref="SimpleConfigFactory"/>. Default entries self-install via the
/// static constructor; plugin enrichers call <see cref="Register"/> at
/// startup to extend the vocabulary without editing the factory.
/// </summary>
public static class SimpleConfigEnricherRegistry
{
    private static readonly ConcurrentDictionary<string, IEnricherFactory> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    static SimpleConfigEnricherRegistry()
    {
        RegisterDefaults();
    }

    /// <summary>
    /// Look up a factory by name. Returns <c>null</c> when the name is
    /// unknown — callers should throw with an actionable list instead of
    /// silently dropping the enricher.
    /// </summary>
    public static IEnricherFactory? Get(string name) =>
        _factories.TryGetValue(name, out var factory) ? factory : null;

    /// <summary>Known enricher names, in registration order.</summary>
    public static IReadOnlyCollection<string> KnownNames => _factories.Keys.ToArray();

    /// <summary>
    /// Register (or replace) a factory. Replacement is by
    /// <see cref="IEnricherFactory.EnricherName"/> (case-insensitive).
    /// </summary>
    public static void Register(IEnricherFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories[factory.EnricherName] = factory;
    }

    private static void RegisterDefaults()
    {
        // Machine / process / thread are always-on defaults on QuickLogBuilder;
        // acknowledging the names here keeps configs portable (writing
        // "machine" in a config never throws) and returning null skips the
        // redundant instance.
        Register(new AckOnlyFactory("machine"));
        Register(new AckOnlyFactory("process"));
        Register(new AckOnlyFactory("thread"));
        Register(new CorrelationEnricherFactory());
        Register(new ServiceEnricherFactory());
    }

    // -- Built-in factories --

    private sealed class AckOnlyFactory : IEnricherFactory
    {
        public AckOnlyFactory(string name) { EnricherName = name; }
        public string EnricherName { get; }
        public ILogEnricher? Create(SimplePipelineConfig pipeline) => null;
    }

    private sealed class CorrelationEnricherFactory : IEnricherFactory
    {
        public string EnricherName => "correlation";
        public ILogEnricher Create(SimplePipelineConfig pipeline) => new CorrelationIdEnricher();
    }

    private sealed class ServiceEnricherFactory : IEnricherFactory
    {
        public string EnricherName => "service";

        public ILogEnricher Create(SimplePipelineConfig pipeline)
        {
            // Properties supply optional identity fields; fallback to the
            // pipeline name keeps the enricher useful when operators do not
            // set properties explicitly.
            var props = pipeline.Properties;
            var serviceName = TryRead(props, "serviceName") ?? pipeline.Name;
            return new ServiceIdentityEnricher(
                serviceName,
                version: TryRead(props, "serviceVersion") ?? HeraldVersion.Version,
                region: TryRead(props, "serviceRegion"),
                environment: TryRead(props, "serviceEnvironment"));
        }

        private static string? TryRead(Dictionary<string, string> props, string key) =>
            props.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }
}
