// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Herald.OSS.Serilog.Settings;
using Herald.OSS.Serilog.Settings.Parsing;
using Microsoft.Extensions.Configuration;
using MMP.Herald.Routing;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace Herald.OSS.Serilog.Settings.Tests.Parsing;

/// <summary>
/// Covers <see cref="SerilogConfigurationReader"/>:
/// <list type="bullet">
///   <item>MinimumLevel string shorthand</item>
///   <item>MinimumLevel object form with Default</item>
///   <item>MinimumLevel.Override with dotted namespace keys (Risk 10 corollary)</item>
///   <item>WriteTo known sink resolves without throw</item>
///   <item>WriteTo unknown sink throws <see cref="SinkResolutionException"/></item>
///   <item>Enrich string-array shorthand applied (Risk 10 — most common production pattern)</item>
///   <item>Using section silently skipped; unknown WriteTo name still throws</item>
///   <item>Missing Serilog section is a no-op</item>
///   <item>Community NuGet sink name throws with explanatory message</item>
///   <item>WriteTo entry with Args passes section to factory</item>
/// </list>
/// </summary>
public sealed class SerilogConfigurationReaderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    // Build an IConfiguration from a raw JSON string.
    private static IConfiguration BuildConfig(string json)
    {
        var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }

    // Build a reader wired to the pre-seeded default registries.
    private static (SerilogConfigurationReader reader, LoggerConfiguration lc) MakeReader()
    {
        var lc = new LoggerConfiguration();
        var reader = new SerilogConfigurationReader(
            lc,
            LoggerSinkRegistry.Default,
            LoggerEnricherRegistry.Default);
        return (reader, lc);
    }

    // Build a reader wired to custom mutable registries (for sink-tracking tests).
    private static (SerilogConfigurationReader reader, LoggerConfiguration lc,
        LoggerSinkRegistry sinkReg, LoggerEnricherRegistry enrichReg) MakeCustomReader()
    {
        var lc       = new LoggerConfiguration();
        var sinkReg  = LoggerSinkRegistry.CreateDefault();
        var enrichReg = LoggerEnricherRegistry.CreateDefault();
        var reader   = new SerilogConfigurationReader(lc, sinkReg, enrichReg);
        return (reader, lc, sinkReg, enrichReg);
    }

    // ── MinimumLevel — string shorthand ──────────────────────────────────────

    [Fact]
    public void MinimumLevel_string_shorthand_is_parsed()
    {
        // Arrange
        var config = BuildConfig("""{ "Serilog": { "MinimumLevel": "Warning" } }""");
        var (reader, lc) = MakeReader();

        // Act — no exception expected
        reader.Apply(config);

        // Assert — CreateLogger succeeds; the level was applied without crashing.
        // Exact level verification would require Builder inspection (internal API).
        // We verify via the public contract: the call chain completed without throw.
        var logger = lc.WriteTo.Null().CreateLogger();
        logger.Should().NotBeNull("a Warning-floored logger should be created successfully");
    }

    [Theory]
    [InlineData("Verbose")]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Fatal")]
    public void MinimumLevel_string_shorthand_all_valid_levels(string level)
    {
        // Arrange
        var config = BuildConfig($$"""{ "Serilog": { "MinimumLevel": "{{level}}" } }""");
        var (reader, lc) = MakeReader();

        // Act & Assert — each valid level name parses without throwing
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow($"'{level}' is a valid Serilog level name");
    }

    [Fact]
    public void MinimumLevel_unknown_string_throws_ArgumentException()
    {
        // Arrange
        var config = BuildConfig("""{ "Serilog": { "MinimumLevel": "Trace" } }""");
        var (reader, _) = MakeReader();

        // Act & Assert
        reader.Invoking(r => r.Apply(config))
            .Should().Throw<ArgumentException>()
            .WithMessage("*Trace*");
    }

    // ── MinimumLevel — object form ────────────────────────────────────────────

    [Fact]
    public void MinimumLevel_object_form_with_Default_is_parsed()
    {
        // Arrange
        var config = BuildConfig("""{ "Serilog": { "MinimumLevel": { "Default": "Debug" } } }""");
        var (reader, lc) = MakeReader();

        // Act & Assert — no throw; Debug level applied
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("'Debug' is a valid default level in object form");

        lc.WriteTo.Null().CreateLogger().Should().NotBeNull();
    }

    [Fact]
    public void MinimumLevel_object_form_without_Default_applies_no_default_level()
    {
        // Arrange — object form with only Override (no Default key)
        var config = BuildConfig("""
        {
          "Serilog": {
            "MinimumLevel": {
              "Override": { "Microsoft": "Warning" }
            }
          }
        }
        """);
        var (reader, lc) = MakeReader();

        // Act & Assert — Override is processed; no Default means no Is() call
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow();

        lc.WriteTo.Null().CreateLogger().Should().NotBeNull();
    }

    // ── MinimumLevel.Override — dotted namespace keys ─────────────────────────

    [Fact]
    public void MinimumLevel_Override_dotted_key_is_applied()
    {
        // Arrange
        var config = BuildConfig("""
        {
          "Serilog": {
            "MinimumLevel": {
              "Default": "Debug",
              "Override": {
                "Microsoft.Extensions": "Warning"
              }
            }
          }
        }
        """);
        var (reader, lc) = MakeReader();

        // Act — no exception; dotted key must not confuse the parser
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("'Microsoft.Extensions' is a valid dotted namespace override key");

        lc.WriteTo.Null().CreateLogger().Should().NotBeNull();
    }

    [Fact]
    public void MinimumLevel_Override_multiple_namespaces_are_all_applied()
    {
        // Arrange
        var config = BuildConfig("""
        {
          "Serilog": {
            "MinimumLevel": {
              "Default": "Debug",
              "Override": {
                "Microsoft":            "Warning",
                "System":               "Error",
                "Microsoft.Extensions": "Information"
              }
            }
          }
        }
        """);
        var (reader, lc) = MakeReader();

        // Act & Assert — all three overrides parse without error
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("multiple Override entries including dotted keys must all parse");
    }

    // ── WriteTo — known sink ──────────────────────────────────────────────────

    [Fact]
    public void WriteTo_known_sink_Console_applies_without_throw()
    {
        // Arrange
        var config = BuildConfig("""{ "Serilog": { "WriteTo": [{ "Name": "Console" }] } }""");
        var (reader, lc) = MakeReader();

        // Act & Assert
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("'Console' is a pre-seeded built-in sink");

        lc.CreateLogger().Should().NotBeNull();
    }

    [Theory]
    [InlineData("Console")]
    [InlineData("Null")]
    public void WriteTo_all_noarg_builtins_resolve(string sinkName)
    {
        // Arrange
        var config = BuildConfig($$"""{ "Serilog": { "WriteTo": [{ "Name": "{{sinkName}}" }] } }""");
        var (reader, lc) = MakeReader();

        // Act & Assert
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow($"'{sinkName}' is a pre-seeded built-in sink");
    }

    [Fact]
    public void WriteTo_File_with_Args_path_resolves()
    {
        // Arrange
        var config = BuildConfig("""
        {
          "Serilog": {
            "WriteTo": [
              { "Name": "File", "Args": { "path": "log.txt" } }
            ]
          }
        }
        """);
        var (reader, lc) = MakeReader();

        // Act & Assert
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("'File' with Args resolves to the built-in file sink factory");
    }

    [Fact]
    public void WriteTo_multiple_sinks_all_applied()
    {
        // Arrange
        var config = BuildConfig("""
        {
          "Serilog": {
            "WriteTo": [
              { "Name": "Console" },
              { "Name": "Null" }
            ]
          }
        }
        """);
        var (reader, lc) = MakeReader();

        // Act & Assert — both sinks must resolve; no throw
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("multiple known sinks must all resolve");
    }

    // ── WriteTo — unknown sink throws SinkResolutionException ─────────────────

    [Fact]
    public void WriteTo_unknown_name_throws_SinkResolutionException()
    {
        // Arrange
        var config = BuildConfig("""{ "Serilog": { "WriteTo": [{ "Name": "BogusUnknownSink" }] } }""");
        var (reader, _) = MakeReader();

        // Act & Assert — must throw, not silently succeed (Risk 2)
        reader.Invoking(r => r.Apply(config))
            .Should().Throw<SinkResolutionException>()
            .Which.SinkName.Should().Be("BogusUnknownSink");
    }

    [Fact]
    public void WriteTo_community_NuGet_sink_throws_SinkResolutionException()
    {
        // Arrange — "Serilog.Sinks.Seq" is dotted → NuGet community sink path
        var config = BuildConfig("""
        {
          "Serilog": {
            "WriteTo": [{ "Name": "Serilog.Sinks.Seq", "Args": { "serverUrl": "http://localhost:5341" } }]
          }
        }
        """);
        var (reader, _) = MakeReader();

        // Act & Assert
        var ex = reader.Invoking(r => r.Apply(config))
            .Should().Throw<SinkResolutionException>().Which;

        ex.SinkName.Should().Be("Serilog.Sinks.Seq");
        ex.Message.Should().Contain("identity", because: "the error must mention the assembly identity wall");
    }

    [Fact]
    public void WriteTo_entry_with_null_name_throws_SinkResolutionException()
    {
        // Arrange — malformed entry: object but no Name field
        var config = BuildConfig("""{ "Serilog": { "WriteTo": [{ "Args": { "path": "log.txt" } }] } }""");
        var (reader, _) = MakeReader();

        // Act & Assert
        reader.Invoking(r => r.Apply(config))
            .Should().Throw<SinkResolutionException>()
            .WithMessage("*no 'Name' field*");
    }

    // ── Enrich — string-array shorthand (Risk 10) ─────────────────────────────

    [Fact]
    public void Enrich_string_shorthand_array_FromLogContext_is_applied()
    {
        // Arrange — most common production pattern (Risk 10)
        var config = BuildConfig("""{ "Serilog": { "Enrich": ["FromLogContext"] } }""");
        var (reader, lc) = MakeReader();

        // Act & Assert — FromLogContext resolves from the pre-seeded registry
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("'FromLogContext' is a pre-seeded built-in enricher (Risk 10 case)");

        lc.WriteTo.Null().CreateLogger().Should().NotBeNull();
    }

    [Fact]
    public void Enrich_string_shorthand_array_multiple_enrichers()
    {
        // Arrange
        var config = BuildConfig("""{ "Serilog": { "Enrich": ["FromLogContext", "WithProperty"] } }""");
        var (reader, lc) = MakeReader();

        // Act & Assert
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("both 'FromLogContext' and 'WithProperty' are pre-seeded built-ins");
    }

    [Fact]
    public void Enrich_unknown_name_is_silently_skipped()
    {
        // Arrange — enrichers are decorative; unknown names must not crash startup
        var config = BuildConfig("""{ "Serilog": { "Enrich": ["FromLogContext", "SomeUnknownEnricher"] } }""");
        var (reader, lc) = MakeReader();

        // Act & Assert — no throw; unknown enricher is silently skipped
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow(
                "unresolved enricher names must be skipped, not thrown — " +
                "asymmetric with sinks because enrichers are decorative, not delivery-critical");

        lc.WriteTo.Null().CreateLogger().Should().NotBeNull();
    }

    [Fact]
    public void Enrich_object_form_with_Name_and_Args_is_applied()
    {
        // Arrange — object form: [{ "Name": "WithProperty", "Args": { "name": "Env", "value": "prod" } }]
        var config = BuildConfig("""
        {
          "Serilog": {
            "Enrich": [
              { "Name": "WithProperty", "Args": { "name": "Environment", "value": "Production" } }
            ]
          }
        }
        """);
        var (reader, lc) = MakeReader();

        // Act & Assert
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("WithProperty in object form must resolve");
    }

    // ── Using — silently skipped; unknown WriteTo still throws ────────────────

    [Fact]
    public void Using_section_is_silently_skipped()
    {
        // Arrange — Using alone, no WriteTo that would trigger resolution
        var config = BuildConfig("""
        {
          "Serilog": {
            "Using": ["Serilog.Sinks.Seq", "Serilog.Enrichers.Environment"],
            "MinimumLevel": "Information"
          }
        }
        """);
        var (reader, lc) = MakeReader();

        // Act & Assert — Using is skipped; no throw
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("the Using section must be silently skipped (assembly loading not supported)");

        lc.WriteTo.Null().CreateLogger().Should().NotBeNull();
    }

    [Fact]
    public void Using_skipped_then_unresolved_WriteTo_still_throws()
    {
        // Arrange — Using is skipped; Seq in WriteTo cannot be resolved → throw
        var config = BuildConfig("""
        {
          "Serilog": {
            "Using": ["Serilog.Sinks.Seq"],
            "WriteTo": [{ "Name": "Seq", "Args": { "serverUrl": "http://localhost:5341" } }]
          }
        }
        """);
        var (reader, _) = MakeReader();

        // Act & Assert — Using skip does NOT suppress the WriteTo throw
        reader.Invoking(r => r.Apply(config))
            .Should().Throw<SinkResolutionException>(
                "the Using section being skipped must not allow unresolved WriteTo sinks to pass silently")
            .Which.SinkName.Should().Be("Seq");
    }

    // ── Missing Serilog section is a no-op ────────────────────────────────────

    [Fact]
    public void Missing_Serilog_section_is_a_no_op()
    {
        // Arrange — configuration has no Serilog key at all
        var config = BuildConfig("""{ "ConnectionStrings": { "Default": "..." } }""");
        var (reader, lc) = MakeReader();

        // Act & Assert — must not throw
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("a missing Serilog section must be a clean no-op");

        lc.WriteTo.Null().CreateLogger().Should().NotBeNull();
    }

    [Fact]
    public void Empty_Serilog_section_is_a_no_op()
    {
        // Arrange
        var config = BuildConfig("""{ "Serilog": {} }""");
        var (reader, lc) = MakeReader();

        // Act & Assert
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("an empty Serilog section must be a clean no-op");
    }

    // ── Full schema round-trip ────────────────────────────────────────────────

    [Fact]
    public void Full_schema_with_MinimumLevel_WriteTo_Enrich_Using_parses_without_throw()
    {
        // Arrange — the canonical example from the class-level XML doc
        var config = BuildConfig("""
        {
          "Serilog": {
            "MinimumLevel": {
              "Default": "Debug",
              "Override": {
                "Microsoft": "Warning",
                "System":    "Error"
              }
            },
            "WriteTo": [
              { "Name": "Console" },
              { "Name": "File", "Args": { "path": "log.txt" } }
            ],
            "Enrich": ["FromLogContext", "WithProperty"],
            "Using": ["Serilog.Sinks.Seq"]
          }
        }
        """);
        var (reader, lc) = MakeReader();

        // Act & Assert
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("the full canonical schema must parse end-to-end without any exception");

        lc.CreateLogger().Should().NotBeNull();
    }

    // ── Custom sink registration surfaces through the reader ──────────────────

    [Fact]
    public void Custom_registered_sink_resolves_through_reader()
    {
        // Arrange
        var sinkInvoked = false;
        var (reader, lc, sinkReg, _) = MakeCustomReader();
        sinkReg.RegisterSink("MySink", (builder, _) =>
        {
            sinkInvoked = true;
            return builder;
        });

        var config = BuildConfig("""{ "Serilog": { "WriteTo": [{ "Name": "MySink" }] } }""");

        // Act
        reader.Apply(config);

        // Assert — factory was called
        sinkInvoked.Should().BeTrue("a custom-registered sink must be resolved and its factory invoked");
    }

    // ── Registry bridge — native SinkKind names resolve through LogSinkProviderRegistry ──
    //
    // When a WriteTo[].Name is not a Layer-1 verb (Console/File/Http/...), the reader
    // falls through to the native MMP.Herald.Routing.LogSinkProviderRegistry by SinkKind.
    // This is what makes the 86 MMP.Herald.Sinks.* packages addressable by Serilog name
    // in appsettings.json with zero changes to the sink packages.
    //
    // The native registry is INJECTED (defaults to LogSinkProviderRegistry.Default in
    // production) so these tests run against an isolated registry and never touch the
    // process-wide Default — mirrors the engine's own SinkProviderSet.SetFallbackRegistry
    // test seam.

    // Minimal native sink provider: registers a kind, records that CreateSink was reached.
    // CreateSink is never invoked by these tests (the bridge declares the kind into the
    // builder's JSON config; the engine resolves the provider at build time, not here),
    // but the contract requires an implementation.
    private sealed class FakeNativeSinkProvider : ILogSinkProvider
    {
        public FakeNativeSinkProvider(string sinkKind) => SinkKind = sinkKind;

        public string SinkKind { get; }

        public MMP.Herald.ILogger CreateSink(
            MMP.Herald.Configuration.Runtime.LoggingRuntimeSinkDefinition definition,
            MMP.Herald.Levels.ILogLevelRegistry levelRegistry,
            MMP.Herald.Output.Rendering.ILogOutputTransformerRegistry transformerRegistry)
            => new NullNativeSink();

        private sealed class NullNativeSink : MMP.Herald.ILogger
        {
            public void Log(MMP.Herald.Events.LogEvent logEvent) { /* drop */ }
        }
    }

    // Reader wired to an isolated native registry seeded with one fake provider kind.
    private static (SerilogConfigurationReader reader, LoggerConfiguration lc, LogSinkProviderRegistry nativeReg)
        MakeReaderWithNativeKind(string nativeKind)
    {
        var lc = new LoggerConfiguration();
        var nativeReg = new LogSinkProviderRegistry();
        nativeReg.Register(new FakeNativeSinkProvider(nativeKind));

        var reader = new SerilogConfigurationReader(
            lc,
            LoggerSinkRegistry.Default,
            LoggerEnricherRegistry.Default,
            nativeReg);

        return (reader, lc, nativeReg);
    }

    [Fact]
    public void WriteTo_with_native_SinkKind_name_resolves_through_LogSinkProviderRegistry()
    {
        // Arrange — "telemetry_relay" is NOT a Layer-1 verb; it lives only in the
        // native registry. A URI Arg is supplied because native network/integration
        // sinks push to an endpoint (the bridge routes through WithNetworkSink).
        var (reader, _, _) = MakeReaderWithNativeKind("telemetry_relay");
        var config = BuildConfig("""
        {
          "Serilog": {
            "WriteTo": [
              { "Name": "telemetry_relay", "Args": { "endpoint": "https://logs.example.com" } }
            ]
          }
        }
        """);

        // Act + Assert — must NOT throw SinkResolutionException; the native kind
        // resolves through the registry bridge.
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("native sink kinds must resolve through LogSinkProviderRegistry");
    }

    [Fact]
    public void WriteTo_native_SinkKind_is_case_insensitive()
    {
        // Arrange — registry stores lower-case keys; the appsettings Name uses mixed case.
        var (reader, _, _) = MakeReaderWithNativeKind("telemetry_relay");
        var config = BuildConfig("""
        {
          "Serilog": {
            "WriteTo": [
              { "Name": "Telemetry_Relay", "Args": { "endpoint": "https://logs.example.com" } }
            ]
          }
        }
        """);

        // Act + Assert
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("native SinkKind resolution is case-insensitive (LogSinkProviderRegistry uses OrdinalIgnoreCase)");
    }

    [Fact]
    public void WriteTo_name_in_neither_registry_still_throws_SinkResolutionException()
    {
        // Arrange — the native registry knows "telemetry_relay" but NOT "ghost_sink".
        // The bridge must not swallow a genuinely-unknown name (Risk 2 preserved).
        var (reader, _, _) = MakeReaderWithNativeKind("telemetry_relay");
        var config = BuildConfig("""{ "Serilog": { "WriteTo": [{ "Name": "ghost_sink" }] } }""");

        // Act + Assert
        reader.Invoking(r => r.Apply(config))
            .Should().Throw<SinkResolutionException>("a name in neither registry is still a hard error")
            .Which.SinkName.Should().Be("ghost_sink");
    }

    [Fact]
    public void Layer1_verb_takes_precedence_over_native_kind_of_same_name()
    {
        // Arrange — register a native provider whose kind collides with the Layer-1
        // "Console" verb. The Layer-1 registry must win (it is checked first), so the
        // native provider is never consulted for "Console".
        var lc = new LoggerConfiguration();
        var nativeReg = new LogSinkProviderRegistry();
        nativeReg.Register(new FakeNativeSinkProvider("console"));
        var reader = new SerilogConfigurationReader(
            lc, LoggerSinkRegistry.Default, LoggerEnricherRegistry.Default, nativeReg);

        var config = BuildConfig("""{ "Serilog": { "WriteTo": [{ "Name": "Console" }] } }""");

        // Act + Assert — resolves via the Layer-1 Console verb, no throw.
        reader.Invoking(r => r.Apply(config))
            .Should().NotThrow("Layer-1 verbs take precedence; the native registry is only a fall-through");

        lc.CreateLogger().Should().NotBeNull();
    }

    // ── Standing conformance regression ───────────────────────────────────────
    //
    // If someone breaks the bridge, this goes RED and they know exactly why: a
    // native SinkKind registered in the engine's registry is no longer reachable
    // by Serilog Name. Uses an isolated registry so it is deterministic regardless
    // of which Herald.Sinks.* packages the test host happens to load.

    [Fact]
    public void A_registered_native_SinkKind_is_resolvable_by_Serilog_Name()
    {
        // Arrange
        var nativeReg = new LogSinkProviderRegistry();
        nativeReg.Register(new FakeNativeSinkProvider("telemetry_relay"));
        var reader = new SerilogConfigurationReader(
            new LoggerConfiguration(),
            LoggerSinkRegistry.Default,
            LoggerEnricherRegistry.Default,
            nativeReg);

        // Act + Assert — the bridge can see the native kind by name.
        reader.CanResolveSinkName("telemetry_relay")
            .Should().BeTrue(
                "the registry bridge must keep native SinkKinds reachable by Serilog Name — " +
                "this is the cross-registry resolution contract");
    }
}
