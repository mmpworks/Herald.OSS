#nullable enable

// Layer-4 regression suite (DemoApp empty-feed bug, 2026-06-03).
//
// Root cause: WithCustomLevel(...) + WithLevelOrder(...) with custom keys that
// DIVERGE from the canonical base level keys (the DemoApp registers short keys
// "info"/"warn" via NameToKey, while the canonical base levels are the long
// "information"/"warning") leaves the redundant base levels in the registry.
// Those leftover base entries land at HIGHER ranks than the operator's intended
// levels, so a minimum-level filter resolves the wrong rank and silently drops
// events that should pass. The DemoApp's http_json loopback sink never saw the
// event — the feed stayed empty even though the pipeline reported healthy.
//
// These tests pin the contract at the Herald.OSS boundary:
//  1) the registry an explicit complete order produces carries ONLY the requested
//     levels — no redundant canonical base levels at corrupting ranks;
//  2) an Error event admitted by a "warn" minimum is actually DELIVERED to the
//     wired sink (the real-world symptom: it was silently dropped).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Quick;
using MMP.Herald.Routing;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Quick;

/// <summary>
/// Pins the layer-4 silent-drop: custom levels whose keys diverge from the
/// canonical base keys must not leave redundant base levels that corrupt rank
/// ordering and drop admitted events. The delivery test wires a capture sink
/// through the SAME provider + WithNetworkSink path the http_json loopback sink
/// uses, so it exercises the real dispatch (no console-floor shortcut).
/// </summary>
public sealed class ExternalInjectionCustomLevelDeliveryTests
{
    private const string CaptureKind = "layer4-capture";

    // The DemoApp's exact level shape: short custom keys (info/warn) that diverge
    // from the canonical base (information/warning), an explicit least-severe-first
    // order, and a "warn" minimum. An Error event MUST be delivered.
    [Fact]
    public async Task CustomLevels_with_short_keys_and_explicit_order_deliver_admitted_event()
    {
        var capture = new CaptureSink();

        var builder = QuickLogBuilder.Create("layer4");
        builder.AllowExternalEventInjection();
        builder.WithCustomSinkProvider(new CaptureSinkProvider(capture));
        builder.WithNetworkSink(CaptureKind, "memory://capture");

        string[] keys = { "debug", "info", "warn", "error", "fatal" };
        string[] names = { "Debug", "Information", "Warning", "Error", "Fatal" };
        for (var i = 0; i < keys.Length; i++)
        {
            builder.WithCustomLevel(keys[i], names[i]);
        }
        builder.WithLevelOrder(keys);
        builder.WithMinimumLevel("warn");

        await using var result = builder.BuildAndCommit();

        var errorLevel = result.LevelRegistry.GetByKeyOrNull("error")
            ?? new LogLevel("error", "Error");

        result.Logger.Log(new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: errorLevel,
            Category: new LogCategory("external"),
            MessageTemplate: "layer4 probe",
            Message: "layer4 probe",
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext));

        capture.Received.Should().ContainSingle(
            "an Error event admitted by a 'warn' minimum must reach the sink; " +
            "leftover canonical base levels must not outrank it");
    }

    // The registry an explicit, complete order produces must not carry the
    // redundant base levels the order did not request. The presence of
    // "warning"/"information" alongside "warn"/"info" is the rank-corruption seed.
    [Fact]
    public async Task Explicit_level_order_does_not_leak_redundant_base_levels()
    {
        var builder = QuickLogBuilder.Create("layer4-registry");
        builder.WithCustomSinkProvider(new CaptureSinkProvider(new CaptureSink()));
        builder.WithNetworkSink(CaptureKind, "memory://capture");

        string[] keys = { "debug", "info", "warn", "error", "fatal" };
        string[] names = { "Debug", "Information", "Warning", "Error", "Fatal" };
        for (var i = 0; i < keys.Length; i++)
        {
            builder.WithCustomLevel(keys[i], names[i]);
        }
        builder.WithLevelOrder(keys);

        await using var result = builder.BuildAndCommit();
        var registry = result.LevelRegistry;

        // The operator declared a complete 5-level order. The canonical long-key
        // base levels the order did not mention must NOT survive — their presence
        // at higher ranks is what corrupts severity comparison.
        registry.GetByKeyOrNull("warning").Should().BeNull(
            "the explicit order replaced 'warning' with 'warn'; the stale base level must not linger");
        registry.GetByKeyOrNull("information").Should().BeNull(
            "the explicit order replaced 'information' with 'info'; the stale base level must not linger");

        // The requested levels must rank in the order given: error above warn.
        var error = registry.GetByKeyOrNull("error");
        var warn = registry.GetByKeyOrNull("warn");
        error.Should().NotBeNull();
        warn.Should().NotBeNull();
        registry.IsAtOrAbove(error!, warn!).Should().BeTrue(
            "error is more severe than warn in the requested order");
    }

    // In-memory capture sink: records every LogEvent it is handed. No disk, no
    // network — the "memory://capture" Uri is never dialed.
    private sealed class CaptureSink : MMP.Herald.ILogger
    {
        public readonly List<LogEvent> Received = new();
        public void Log(LogEvent logEvent) => Received.Add(logEvent);
    }

    // Provider that hands back the shared capture sink. Mirrors the http_json
    // provider's registration shape (kind-keyed, resolved at build via
    // WithNetworkSink), minus the batching decorator — the test asserts dispatch
    // reaches the sink, not batching behaviour.
    private sealed class CaptureSinkProvider : ILogSinkProvider
    {
        private readonly CaptureSink _sink;
        public CaptureSinkProvider(CaptureSink sink) => _sink = sink;

        public string SinkKind => CaptureKind;

        public ILogger CreateSink(
            LoggingRuntimeSinkDefinition definition,
            ILogLevelRegistry levelRegistry,
            ILogOutputTransformerRegistry transformerRegistry) => _sink;
    }
}
