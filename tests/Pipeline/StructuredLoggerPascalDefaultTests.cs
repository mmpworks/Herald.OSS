#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Quick;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Phase 4 end-to-end verification: the source-generated typed-args
/// dispatchers honour the active <see cref="IPropertyNamingPolicy"/>.
/// Default policy (Pascal) drives property names from the template
/// tokens. Opt-in policies route through the same code path.
///
/// <para>
/// These are the tests that prove the original goal — a consumer who
/// writes <c>logger.Info("user {UserId} signed in", userId)</c> gets a
/// property named <c>UserId</c>, not <c>userId</c>.
/// </para>
/// </summary>
[Collection(nameof(StructuredLoggerPascalDefaultTests))]
[CollectionDefinition(nameof(StructuredLoggerPascalDefaultTests), DisableParallelization = true)]
public sealed class StructuredLoggerPascalDefaultTests
{
    public StructuredLoggerPascalDefaultTests()
    {
        NameResolverCache.Reset();
    }

    [Fact]
    public void Default_pipeline_emits_PascalCase_property_names_from_template_tokens()
    {
        // The headline behaviour change: variable name says userId, template
        // token says {UserId}, emitted property is "UserId".
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();

        var userId = "alice";
        result.Logger.Info("user {UserId} signed in", userId);

        var ev = sink.Events.Should().ContainSingle().Subject;
        ev.Properties.Should().ContainSingle()
            .Which.Name.Should().Be("UserId");
        ev.Properties[0].Value.Should().Be("alice");
    }

    [Fact]
    public void Default_pipeline_PascalCases_lowercase_first_letter_when_token_is_lowercase()
    {
        // Token-driven; lowercase-first tokens get uppercased.
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();

        var orderId = 42;
        var stage = "captured";
        result.Logger.Info("order {orderId} stage {stage}", orderId, stage);

        var ev = sink.Events.Should().ContainSingle().Subject;
        ev.Properties.Select(p => p.Name).Should().Equal("OrderId", "Stage");
    }

    [Fact]
    public void Default_pipeline_leaves_alreadyPascalCase_tokens_untouched()
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();

        var address = "10.0.0.1";
        result.Logger.Info("ip {IPAddress}", address);

        var ev = sink.Events.Should().ContainSingle().Subject;
        ev.Properties[0].Name.Should().Be("IPAddress",
            "Pascal preserves already-uppercase-start tokens — acronym chains stay readable");
    }

    [Fact]
    public void Snake_policy_emits_snake_case_property_names()
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .WithNamingPolicy(PropertyNamingPolicy.Snake)
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();

        var userId = "alice";
        result.Logger.Info("user {UserId} signed in", userId);

        var ev = sink.Events.Should().ContainSingle().Subject;
        ev.Properties[0].Name.Should().Be("user_id");
    }

    [Fact]
    public void Camel_policy_preserves_caller_argument_expression_names()
    {
        // Opt-in legacy behaviour: variable name at the call site wins.
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .WithNamingPolicy(PropertyNamingPolicy.Camel)
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();

        var userId = "alice";
        result.Logger.Info("user {UserId} signed in", userId);

        var ev = sink.Events.Should().ContainSingle().Subject;
        ev.Properties[0].Name.Should().Be("userId");
    }

    [Fact]
    public void Diagnostics_record_cache_hit_after_repeated_dispatch_on_same_template()
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();

        var v = 7;
        result.Logger.Info("static {Token}", v); // miss
        result.Logger.Info("static {Token}", v); // hit
        result.Logger.Info("static {Token}", v); // hit

        var diag = result.Logger.GetNamingPolicyDiagnostics();
        diag.ResolutionCount.Should().Be(3);
        diag.CacheMisses.Should().Be(1);
        diag.CacheHits.Should().Be(2);
    }

    [Fact]
    public void Distinct_templates_get_independent_cache_entries()
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();

        var v = 7;
        result.Logger.Info("template-a {X}", v);
        result.Logger.Info("template-b {X}", v);
        result.Logger.Info("template-a {X}", v); // second template-a → cache hit

        var diag = result.Logger.GetNamingPolicyDiagnostics();
        diag.ResolutionCount.Should().Be(3);
        diag.CacheMisses.Should().Be(2, "two distinct templates → two misses");
        diag.CacheHits.Should().Be(1);
    }

    [Fact]
    public void Higher_arities_resolve_each_slot_independently()
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();

        var alpha = "a"; var beta = "b"; var gamma = "c"; var delta = "d";
        result.Logger.Info("{Alpha} {Beta} {Gamma} {Delta}", alpha, beta, gamma, delta);

        var ev = sink.Events.Should().ContainSingle().Subject;
        ev.Properties.Select(p => p.Name).Should().Equal("Alpha", "Beta", "Gamma", "Delta");
        ev.Properties.Select(p => p.Value).Should().Equal("a", "b", "c", "d");
    }

    [Fact]
    public void Snake_policy_handles_acronym_coalescing_end_to_end()
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .WithNamingPolicy(PropertyNamingPolicy.Snake)
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();

        var c = "client";
        result.Logger.Info("ctx {HTTPClient}", c);

        var ev = sink.Events.Should().ContainSingle().Subject;
        ev.Properties[0].Name.Should().Be("http_client");
    }

    // Local capturing-bridge so this test class doesn't depend on
    // LogCallShapesTests's internals. Same shape as MultiTenantIsolationTests
    // — implements MMP.Herald.ILogger and stashes everything it sees.
    private sealed class CapturingBridge : MMP.Herald.ILogger
    {
        public ConcurrentBag<LogEvent> Captured { get; } = new();
        public IReadOnlyList<LogEvent> Events => Captured.ToList();
        public void Log(LogEvent logEvent) => Captured.Add(logEvent);
        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Captured.Add(logEvent);
            return ValueTask.CompletedTask;
        }
    }
}

