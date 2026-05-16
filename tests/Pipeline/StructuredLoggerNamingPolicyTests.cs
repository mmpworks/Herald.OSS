#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Phase 2 plumbing: <see cref="StructuredLogger"/> carries an
/// <see cref="IPropertyNamingPolicy"/>, exposes it via
/// <see cref="StructuredLogger.NamingPolicy"/>, and snapshots it across
/// <see cref="StructuredLogger.WithContext"/> child loggers. Diagnostic
/// counters bump correctly when <c>ResolveNames</c> runs.
///
/// <para>
/// <c>ResolveNames</c> is <c>internal</c>; tests reach it via
/// <see cref="InternalsVisibleToAttribute"/> on the Herald.OSS assembly.
/// Until Phase 4 wires the generator to call <c>ResolveNames</c>, this is
/// the only exerciser of the path.
/// </para>
/// </summary>
[Collection(nameof(StructuredLoggerNamingPolicyTests))]
[CollectionDefinition(nameof(StructuredLoggerNamingPolicyTests), DisableParallelization = true)]
public sealed class StructuredLoggerNamingPolicyTests
{
    public StructuredLoggerNamingPolicyTests()
    {
        // Cache is process-static; clear it so per-test cache-hit / -miss
        // counts are deterministic.
        NameResolverCache.Reset();
    }

    [Fact]
    public void Default_pipeline_uses_PascalCasePolicy()
    {
        var result = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("info")
            .BuildAndCommit();

        result.Logger.NamingPolicy.Should().BeSameAs(PascalCasePolicy.Instance);
    }

    [Fact]
    public void WithContext_child_inherits_parent_policy_snapshot()
    {
        var result = QuickLogBuilder.Create()
            .WithConsoleSink()
            .BuildAndCommit();

        var child = result.Logger.WithContext(new Dictionary<string, object?> { ["tenant"] = "alpha" });

        child.NamingPolicy.Should().BeSameAs(result.Logger.NamingPolicy);
    }

    [Fact]
    public void Diagnostics_snapshot_reports_active_policy_id()
    {
        var result = QuickLogBuilder.Create().WithConsoleSink().BuildAndCommit();

        var diag = result.Logger.GetNamingPolicyDiagnostics();

        diag.PolicyId.Should().Be("pascal");
    }

    [Fact]
    public void Diagnostics_counters_start_at_zero()
    {
        var result = QuickLogBuilder.Create().WithConsoleSink().BuildAndCommit();

        var diag = result.Logger.GetNamingPolicyDiagnostics();

        diag.ResolutionCount.Should().Be(0);
        diag.CacheHits.Should().Be(0);
        diag.CacheMisses.Should().Be(0);
        diag.FallbackCount.Should().Be(0);
        diag.CompileTimeResolutions.Should().Be(0);
    }

    [Fact]
    public void ResolveNames_returns_PascalCase_names_for_single_arg()
    {
        var result = QuickLogBuilder.Create().WithConsoleSink().BuildAndCommit();
        var tokens = new[]
        {
            new MessageTemplateToken.Property("UserId", LogPropertyCaptureMode.Default, null, "{UserId}"),
        };
        var argExprs = new[] { "userId" };

        var names = result.Logger.ResolveNames("user {UserId} signed in", tokens, argExprs);

        names.Should().ContainSingle().Which.Should().Be("UserId");
    }

    [Fact]
    public void ResolveNames_first_call_is_a_cache_miss_second_is_a_hit()
    {
        var result = QuickLogBuilder.Create().WithConsoleSink().BuildAndCommit();
        var tokens = new[]
        {
            new MessageTemplateToken.Property("UserId", LogPropertyCaptureMode.Default, null, "{UserId}"),
        };
        var argExprs = new[] { "userId" };

        result.Logger.ResolveNames("template-A", tokens, argExprs); // miss
        result.Logger.ResolveNames("template-A", tokens, argExprs); // hit
        result.Logger.ResolveNames("template-A", tokens, argExprs); // hit

        var diag = result.Logger.GetNamingPolicyDiagnostics();
        diag.ResolutionCount.Should().Be(3);
        diag.CacheMisses.Should().Be(1);
        diag.CacheHits.Should().Be(2);
    }

    [Fact]
    public void ResolveNames_returns_same_array_reference_on_cache_hit()
    {
        var result = QuickLogBuilder.Create().WithConsoleSink().BuildAndCommit();
        var tokens = new[]
        {
            new MessageTemplateToken.Property("UserId", LogPropertyCaptureMode.Default, null, "{UserId}"),
        };
        var argExprs = new[] { "userId" };

        var first = result.Logger.ResolveNames("template-B", tokens, argExprs);
        var second = result.Logger.ResolveNames("template-B", tokens, argExprs);

        ReferenceEquals(first, second).Should().BeTrue(
            "cache hit must return the same array reference (zero allocation)");
    }

    [Fact]
    public void Fallback_count_increments_when_args_exceed_token_count()
    {
        var result = QuickLogBuilder.Create().WithConsoleSink().BuildAndCommit();
        var tokens = new[]
        {
            new MessageTemplateToken.Property("A", LogPropertyCaptureMode.Default, null, "{A}"),
        };
        var argExprs = new[] { "first", "second", "third" }; // two extras

        result.Logger.ResolveNames("{A} only", tokens, argExprs);

        var diag = result.Logger.GetNamingPolicyDiagnostics();
        diag.FallbackCount.Should().Be(2, "two args had no corresponding template token");
    }

    [Fact]
    public void RecordCompileTimeResolution_bumps_only_the_compile_time_counter()
    {
        var result = QuickLogBuilder.Create().WithConsoleSink().BuildAndCommit();

        result.Logger.RecordCompileTimeResolution();
        result.Logger.RecordCompileTimeResolution();

        var diag = result.Logger.GetNamingPolicyDiagnostics();
        diag.CompileTimeResolutions.Should().Be(2);
        diag.ResolutionCount.Should().Be(0, "runtime counters must not move under source-gen path");
        diag.CacheHits.Should().Be(0);
        diag.CacheMisses.Should().Be(0);
    }

    [Fact]
    public void WithContext_child_has_independent_diagnostics_from_parent()
    {
        var result = QuickLogBuilder.Create().WithConsoleSink().BuildAndCommit();
        var tokens = new[]
        {
            new MessageTemplateToken.Property("X", LogPropertyCaptureMode.Default, null, "{X}"),
        };
        var argExprs = new[] { "x" };

        result.Logger.ResolveNames("parent-template", tokens, argExprs);

        var child = result.Logger.WithContext(new Dictionary<string, object?> { ["tenant"] = "alpha" });

        // Child starts fresh — counters are per-instance, not shared with the parent.
        child.GetNamingPolicyDiagnostics().ResolutionCount.Should().Be(0);
        result.Logger.GetNamingPolicyDiagnostics().ResolutionCount.Should().Be(1);
    }
}
