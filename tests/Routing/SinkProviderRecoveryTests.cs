#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Routing;
using Xunit;

namespace MMP.Herald.OSS.Tests.Routing;

/// <summary>
/// Regression suite for the sink auto-registration load gap: a referenced
/// Herald.Sinks.* package whose assembly is never loaded never runs its
/// [ModuleInitializer], so the Default registry misses and (before the fix)
/// the pipeline silently substituted a no-op sink.
///
/// The fix has two halves, tested here:
///  1. Resolve on the DEFAULT registry self-heals: it force-loads the owning
///     assembly (via SinkAssemblyCatalog) and retries.
///  2. When resolution still fails, the exception names the package and the
///     explicit registration call — actionable, never bare.
/// </summary>
[Collection("SinkProviderRecovery")] // serialise: tests mutate a process-wide hook
public sealed class SinkProviderRecoveryTests
{
    private sealed class FakeProvider(string kind) : ILogSinkProvider
    {
        public string SinkKind => kind;
        public MMP.Herald.ILogger CreateSink(
            MMP.Herald.Configuration.Runtime.LoggingRuntimeSinkDefinition definition,
            MMP.Herald.Levels.ILogLevelRegistry levelRegistry,
            MMP.Herald.Output.Rendering.ILogOutputTransformerRegistry transformerRegistry) =>
            throw new NotSupportedException("resolution-only fake");
    }

    [Fact]
    public void DefaultRegistry_recovers_when_assembly_load_registers_the_provider()
    {
        // Unique kind so nothing else in the process can collide with it.
        const string kind = "recovery_test_kind_a";
        var loadAttempts = new List<string>();
        SinkAssemblyCatalog.LoadOverride = requested =>
        {
            loadAttempts.Add(requested);
            if (!string.Equals(requested, kind, StringComparison.OrdinalIgnoreCase))
                return false; // stay inert for any concurrent stranger
            // Simulate exactly what a sink package's [ModuleInitializer] does:
            LogSinkProviderRegistry.Default.Register(new FakeProvider(kind));
            return true;
        };
        try
        {
            var provider = LogSinkProviderRegistry.Default.Resolve(kind);

            Assert.Equal(kind, provider.SinkKind);
            Assert.Contains(kind, loadAttempts);
        }
        finally
        {
            SinkAssemblyCatalog.LoadOverride = null;
            LogSinkProviderRegistry.Default.Unregister(kind);
        }
    }

    [Fact]
    public void DefaultRegistry_failure_message_is_actionable_when_recovery_fails()
    {
        const string kind = "recovery_test_kind_b";
        SinkAssemblyCatalog.LoadOverride = _ => false;
        try
        {
            var ex = Assert.Throws<KeyNotFoundException>(
                () => LogSinkProviderRegistry.Default.Resolve(kind));

            Assert.Contains("No sink provider registered", ex.Message);
            // The generic remedy still tells the operator what registration means.
            Assert.Contains("Register a provider", ex.Message);
        }
        finally
        {
            SinkAssemblyCatalog.LoadOverride = null;
        }
    }

    [Fact]
    public void CustomRegistry_never_attempts_recovery()
    {
        // Isolated registries are deliberately manual (strict tenant isolation);
        // auto-loading into them would defeat their purpose.
        const string kind = "recovery_test_kind_c";
        var attempts = 0;
        SinkAssemblyCatalog.LoadOverride = _ => { attempts++; return false; };
        try
        {
            var isolated = new LogSinkProviderRegistry();
            Assert.Throws<KeyNotFoundException>(() => isolated.Resolve(kind));
            Assert.Equal(0, attempts);
        }
        finally
        {
            SinkAssemblyCatalog.LoadOverride = null;
        }
    }

    [Theory]
    // Convention-following entries.
    [InlineData("http_json", "MMP.Herald.Sinks.HttpJson")]
    [InlineData("loki", "MMP.Herald.Sinks.Loki")]
    [InlineData("seq", "MMP.Herald.Sinks.Seq")]
    // The convention-BREAKERS — the reason the catalog is an explicit map.
    [InlineData("webhook", "MMP.Herald.Sinks.GenericWebhook")]
    [InlineData("aws_s3", "MMP.Herald.Sinks.AmazonS3")]
    [InlineData("mssql", "MMP.Herald.Sinks.MSSqlServer")]
    [InlineData("splunk_hec", "MMP.Herald.Sinks.Splunk")]
    [InlineData("otlp_json", "MMP.Herald.Sinks.Otlp")]
    [InlineData("otlp_protobuf", "MMP.Herald.Sinks.Otlp")]
    [InlineData("protobuf_file", "MMP.Herald.Sinks.Otlp")]
    [InlineData("gcp_logging", "MMP.Herald.Sinks.GoogleCloudLogging")]
    [InlineData("google_pubsub", "MMP.Herald.Sinks.GoogleCloudPubSub")]
    public void Catalog_remedy_names_the_owning_package(string kind, string expectedPackage)
    {
        var remedy = SinkAssemblyCatalog.RemedyFor(kind);
        Assert.Contains(expectedPackage, remedy);
        Assert.Contains("RegisterAll", remedy);
    }

    [Fact]
    public void Catalog_remedy_for_unknown_kind_is_generic_but_actionable()
    {
        var remedy = SinkAssemblyCatalog.RemedyFor("no_such_kind_ever");
        Assert.Contains("Register a provider", remedy);
        // No specific package/registration call is suggested for a kind we don't own.
        Assert.DoesNotContain("RegisterAll", remedy);
    }

    [Fact]
    public void Snapshot_registry_reaches_providers_registered_in_Default_after_the_snapshot()
    {
        // The QuickLog pipeline registry is a build-time COPY of Default
        // (BuildSinkProviderRegistry). A sink assembly loaded AFTER that copy
        // registers into Default only — the fallback chain is what lets the
        // snapshot still resolve it. This is the exact shape of the original
        // field failure: WithHttpJsonSink + package referenced but never loaded.
        const string kind = "recovery_test_kind_chain";
        var snapshot = new LogSinkProviderRegistry(fallback: LogSinkProviderRegistry.Default);

        SinkAssemblyCatalog.LoadOverride = requested =>
        {
            if (!string.Equals(requested, kind, StringComparison.OrdinalIgnoreCase))
                return false;
            LogSinkProviderRegistry.Default.Register(new FakeProvider(kind));
            return true;
        };
        try
        {
            var provider = snapshot.Resolve(kind);
            Assert.Equal(kind, provider.SinkKind);
        }
        finally
        {
            SinkAssemblyCatalog.LoadOverride = null;
            LogSinkProviderRegistry.Default.Unregister(kind);
        }
    }

    [Fact]
    public void Isolated_fallback_chain_never_touches_Default()
    {
        const string kind = "recovery_test_kind_isolated";
        var isolatedSeed = new LogSinkProviderRegistry();
        var snapshot = new LogSinkProviderRegistry(fallback: isolatedSeed);
        var attempts = 0;
        SinkAssemblyCatalog.LoadOverride = _ => { attempts++; return false; };
        try
        {
            Assert.Throws<KeyNotFoundException>(() => snapshot.Resolve(kind));
            // Neither the snapshot nor its isolated seed is Default → recovery
            // is never attempted, preserving strict test isolation.
            Assert.Equal(0, attempts);
        }
        finally
        {
            SinkAssemblyCatalog.LoadOverride = null;
        }
    }

    [Fact]
    public void TryRecover_returns_false_for_unmapped_kind_without_hook()
    {
        Assert.Null(SinkAssemblyCatalog.LoadOverride); // guard: no leakage between tests
        Assert.False(SinkAssemblyCatalog.TryRecover("no_such_kind_ever"));
    }

    [Fact]
    public void TryRecover_survives_a_mapped_kind_whose_assembly_is_absent()
    {
        Assert.Null(SinkAssemblyCatalog.LoadOverride);
        // 'zeromq' maps to Herald.Sinks.ZeroMQ, which this test project does not
        // reference — the load fails and must be swallowed, not thrown.
        Assert.False(SinkAssemblyCatalog.TryRecover("zeromq"));
    }
}
