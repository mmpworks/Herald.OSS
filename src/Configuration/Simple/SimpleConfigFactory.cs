#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald;
using MMP.Herald.Enrichers;
using MMP.Herald.Quick;
using MMP.Herald.Services;

namespace MMP.Herald.Configuration.Simple;

/// <summary>
/// Turns a <see cref="SimplePipelineConfig"/> (or root <see cref="SimpleLoggingConfig"/>)
/// into a configured <see cref="QuickLogBuilder"/>. Shared by Herald.Lean and
/// Herald.Embed so both facades get the same sink and enricher coverage — one
/// place to land new kinds, one place to fix sink wiring bugs.
///
/// Supported sink kinds:
///   console, text_file (alias: textfile, file), json_file (alias: jsonfile),
///   protobuf_file, http_json, tcp_json_line, elasticsearch, slack, webhook,
///   otlp_json, otlp_protobuf.
///
/// Supported strategies: default, filter-early (alias: filterearly), minimal.
///
/// Supported enrichers: machine, process, thread (no-op — always on), correlation, service.
///
/// Unknown kinds, strategies, or enrichers throw <see cref="InvalidOperationException"/>
/// with a message listing the supported values. A silent drop in a logging daemon
/// is the worst place for a sharp edge.
/// </summary>
public static class SimpleConfigFactory
{
    /// <summary>
    /// Build a <see cref="QuickLogBuilder"/> from the given simple-schema config.
    /// The builder is returned pre-configured but not committed — caller owns
    /// the decision to call <see cref="QuickLogBuilder.Build"/> or
    /// <see cref="QuickLogBuilder.BuildAndCommit"/>.
    /// </summary>
    public static QuickLogBuilder Build(SimplePipelineConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var builder = QuickLogBuilder.Create(config.Name)
            .WithMinimumLevel(config.MinimumLevel);

        builder.WithPipelineStrategy(ResolveStrategy(config.Strategy));

        foreach (var sink in config.Sinks)
            ApplySink(builder, sink);

        foreach (var enricher in config.Enrichers)
            ApplyEnricher(builder, enricher, config);

        return builder;
    }

    /// <summary>
    /// Build and commit every pipeline in <paramref name="config"/>, registering each
    /// under its configured name. Returns the list of (name, builder, result) tuples
    /// so the caller can inspect or dispose them later.
    ///
    /// <para>
    /// <paramref name="configureBeforeBuild"/> runs against each builder after
    /// the config has been applied but before <see cref="QuickLogBuilder.BuildAndCommit"/>.
    /// Sinks that live in sibling NuGet packages (file, http, otlp, …) attach
    /// their <see cref="ILogSinkProvider"/> here. Embed and Lean both call
    /// this with <c>b =&gt; b.WithFileSinkProviders()</c> so JSON configs that
    /// declare a file kind resolve at build time without Core having to depend
    /// on the file-sink package.
    /// </para>
    /// </summary>
    public static List<(string Name, QuickLogBuilder Builder, QuickLogResult Result)> BuildAll(
        SimpleLoggingConfig config,
        Action<QuickLogBuilder>? configureBeforeBuild = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var results = new List<(string, QuickLogBuilder, QuickLogResult)>();

        foreach (var pipeline in config.Pipelines)
        {
            var builder = Build(pipeline);
            configureBeforeBuild?.Invoke(builder);
            var result = builder.BuildAndCommit();

            // Empty tenant string means "use the default tenant" — preserves
            // every existing single-tenant config file's behaviour. A
            // non-default tenant routes the registration through the
            // tenant-aware overload, which the edition gate rejects on
            // Community / Pro builds.
            if (string.IsNullOrWhiteSpace(pipeline.Tenant))
            {
                HeraldRegistry.Register(builder, result);
            }
            else
            {
                HeraldRegistry.Register(pipeline.Tenant, pipeline.Name, builder, result);
            }

            results.Add((pipeline.Name, builder, result));
        }
        return results;
    }

    /// <summary>
    /// Translate a configuration-time strategy name (as it appears in
    /// operator JSON configs) into a <see cref="PipelineStrategy"/>. Public
    /// so facades (<c>HeraldEmbed</c>, <c>Herald.Lean</c>) share one
    /// vocabulary — the three accepted strings here are the canonical set
    /// for the whole Herald ecosystem.
    ///
    /// <para>Accepted values: <c>default</c>, <c>filter-early</c> (or
    /// <c>filterearly</c>), <c>minimal</c>. Matching is case-insensitive.
    /// Unknown names throw <see cref="InvalidOperationException"/> with the
    /// accepted set in the message.</para>
    /// </summary>
    public static PipelineStrategy ResolveStrategy(string name) =>
        name.ToLowerInvariant() switch
        {
            "default" => PipelineStrategy.Default(),
            "filter-early" or "filterearly" => PipelineStrategy.FilterEarly(),
            "minimal" => PipelineStrategy.Minimal(),
            _ => throw new InvalidOperationException(
                $"Unknown pipeline strategy: '{name}'. Supported: default, filter-early, minimal."),
        };

    // Fast vocabulary checks for operator-facing --validate flows. Each
    // predicate mirrors the exact switch / registry that the Build and
    // ApplySink / ApplyEnricher paths use, so --validate and BuildAll
    // cannot disagree about whether a kind is real. Lean's CLI delegates
    // to these instead of hand-maintaining its own HashSets.
    private static readonly HashSet<string> _knownStrategyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "default", "filter-early", "filterearly", "minimal",
    };

    private static readonly HashSet<string> _knownSinkKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "console",
        "null",
        "text_file", "textfile", "file",
        "json_file", "jsonfile",
        "protobuf_file", "protobuffile",
        "http_json", "httpjson",
        "tcp_json_line", "tcpjsonline",
        "elasticsearch",
        "slack",
        "webhook",
        "otlp_json", "otlpjson",
        "otlp_protobuf", "otlpprotobuf",
    };

    /// <summary>
    /// True when <paramref name="name"/> is a strategy the factory's
    /// <see cref="ResolveStrategy"/> would accept. Case-insensitive.
    /// Complements the throw-on-build behaviour by giving callers a way to
    /// pre-screen without catching an exception.
    /// </summary>
    public static bool IsKnownStrategy(string name) =>
        !string.IsNullOrWhiteSpace(name) && _knownStrategyNames.Contains(name);

    /// <summary>
    /// True when <paramref name="kind"/> is a sink kind the factory's
    /// <see cref="ApplySink"/> switch dispatches. Mirrors the switch arms
    /// verbatim, so new kinds added there must be added here too.
    /// Case-insensitive.
    /// </summary>
    public static bool IsKnownSinkKind(string kind) =>
        !string.IsNullOrWhiteSpace(kind) && _knownSinkKinds.Contains(kind);

    /// <summary>
    /// True when <paramref name="name"/> is an enricher kind registered
    /// with <see cref="SimpleConfigEnricherRegistry"/>. Lean / Embed share
    /// this check so --validate stays in sync with the registry state as
    /// plugin enrichers self-register. Case-insensitive.
    /// </summary>
    public static bool IsKnownEnricher(string name) =>
        !string.IsNullOrWhiteSpace(name) && SimpleConfigEnricherRegistry.Get(name) is not null;

    // Cognitive complexity note: flat switch over sink kinds. Every case is a single
    // builder method call, keeping the dispatch obvious. Adding a new sink kind is
    // one case arm plus a matching test.
    private static void ApplySink(QuickLogBuilder builder, SimpleSinkConfig sink)
    {
        switch (sink.Kind.ToLowerInvariant())
        {
            case "console":
                builder.WithConsoleSink(minLevel: sink.MinLevel);
                break;

            case "null":
                // Zero-work leaf sink — useful for honest pipeline benching and
                // for disabling downstream I/O without removing the chain.
                builder.WithNullSink(minLevel: sink.MinLevel);
                break;

            case "text_file" or "textfile" or "file":
                RequirePath(sink, "text_file");
                builder.WithFileSink(sink.Path!, minLevel: sink.MinLevel);
                break;

            case "json_file" or "jsonfile":
                RequirePath(sink, "json_file");
                builder.WithFileSink(sink.Path!, Services.KnownSinkKinds.JsonFile, minLevel: sink.MinLevel);
                break;

            case "protobuf_file" or "protobuffile":
                RequirePath(sink, "protobuf_file");
                builder.WithFileSink(sink.Path!, Services.KnownSinkKinds.ProtobufFile, minLevel: sink.MinLevel);
                break;

            case "http_json" or "httpjson":
                RequireEndpoint(sink, "http_json");
                builder.WithHttpJsonSink(sink.Endpoint!, sink.MinLevel);
                break;

            case "tcp_json_line" or "tcpjsonline":
                if (string.IsNullOrWhiteSpace(sink.Host) || sink.Port is null)
                    throw new InvalidOperationException(
                        "Sink kind 'tcp_json_line' requires 'host' and 'port' properties.");
                builder.WithTcpJsonLineSink(sink.Host, sink.Port.Value, sink.MinLevel);
                break;

            case "elasticsearch":
                RequireEndpoint(sink, "elasticsearch");
                builder.WithElasticsearchSink(sink.Endpoint!, sink.MinLevel);
                break;

            case "slack":
                RequireEndpoint(sink, "slack");
                builder.WithSlackWebhookSink(sink.Endpoint!, sink.MinLevel);
                break;

            case "webhook":
                RequireEndpoint(sink, "webhook");
                builder.WithWebhookSink(sink.Endpoint!, sink.MinLevel);
                break;

            case "otlp_json" or "otlpjson":
                RequireEndpoint(sink, "otlp_json");
                builder.WithOtlpJsonSink(sink.Endpoint!, sink.MinLevel);
                break;

            case "otlp_protobuf" or "otlpprotobuf":
                RequireEndpoint(sink, "otlp_protobuf");
                builder.WithOtlpProtobufSink(sink.Endpoint!, sink.MinLevel);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown sink kind: '{sink.Kind}'. Supported: console, null, text_file, json_file, " +
                    "protobuf_file, http_json, tcp_json_line, elasticsearch, slack, webhook, " +
                    "otlp_json, otlp_protobuf.");
        }
    }

    private static void ApplyEnricher(QuickLogBuilder builder, string enricher, SimplePipelineConfig config)
    {
        // Registry-driven: each enricher kind owns its own factory and
        // self-registers. Unknown names throw with the full supported list
        // pulled from the registry so operators see exactly what works.
        var factory = SimpleConfigEnricherRegistry.Get(enricher);
        if (factory is null)
        {
            throw new InvalidOperationException(
                $"Unknown enricher: '{enricher}'. " +
                $"Supported: {string.Join(", ", SimpleConfigEnricherRegistry.KnownNames)}.");
        }

        var instance = factory.Create(config);
        if (instance is not null)
        {
            builder.WithEnrichers(instance);
        }
    }

    private static void RequirePath(SimpleSinkConfig sink, string kind)
    {
        if (string.IsNullOrWhiteSpace(sink.Path))
            throw new InvalidOperationException($"Sink kind '{kind}' requires a 'path' property.");
    }

    private static void RequireEndpoint(SimpleSinkConfig sink, string kind)
    {
        if (string.IsNullOrWhiteSpace(sink.Endpoint))
            throw new InvalidOperationException($"Sink kind '{kind}' requires an 'endpoint' property.");
    }
}
