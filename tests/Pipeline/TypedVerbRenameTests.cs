#nullable enable

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Task 4 regression guard: verifies that the Serilog-vocabulary verb methods
/// (Information, Warning, Verbose, Fatal) exist on StructuredLogger and route
/// to the correct <see cref="KnownLogLevels"/> members.
///
/// <para>
/// These tests are deliberately RED until the rename lands. Step 1 of the TDD
/// cycle: write the test asserting the new verb surface.
/// </para>
/// </summary>
[Collection(nameof(TypedVerbRenameTests))]
[CollectionDefinition(nameof(TypedVerbRenameTests), DisableParallelization = true)]
public sealed class TypedVerbRenameTests
{
    private static readonly LogCategory Cat = new("VerbRenameTest");

    // ── Core verb routing ──────────────────────────────────────────────────

    [Fact]
    public void Information_verb_routes_to_information_level()
    {
        var (logger, sink) = BuildPipeline("verbose");

        logger.Information(Cat, "hi {X}", properties: null);

        sink.Events.Should().ContainSingle(e => e.Level.Key == "information",
            "Information() must route to KnownLogLevels.Information (key=\"information\")");
    }

    [Fact]
    public void Warning_verb_routes_to_warning_level()
    {
        var (logger, sink) = BuildPipeline("verbose");

        logger.Warning(Cat, "w {X}", properties: null);

        sink.Events.Should().ContainSingle(e => e.Level.Key == "warning",
            "Warning() must route to KnownLogLevels.Warning (key=\"warning\")");
    }

    [Fact]
    public void Verbose_verb_routes_to_verbose_level()
    {
        var (logger, sink) = BuildPipeline("verbose");

        logger.Verbose(Cat, "v {X}", properties: null);

        sink.Events.Should().ContainSingle(e => e.Level.Key == "verbose",
            "Verbose() must route to KnownLogLevels.Verbose (key=\"verbose\")");
    }

    [Fact]
    public void Fatal_verb_routes_to_fatal_level()
    {
        var (logger, sink) = BuildPipeline("verbose");

        logger.Fatal(Cat, "f {X}", properties: null);

        sink.Events.Should().ContainSingle(e => e.Level.Key == "fatal",
            "Fatal() must route to KnownLogLevels.Fatal (key=\"fatal\")");
    }

    // ── Four calls in sequence (the plan's canonical example) ─────────────

    [Fact]
    public void All_four_renamed_verbs_route_correctly_in_sequence()
    {
        var (logger, sink) = BuildPipeline("verbose");

        logger.Information(Cat, "info msg", properties: null);
        logger.Warning(Cat, "warn msg", properties: null);
        logger.Verbose(Cat, "verbose msg", properties: null);
        logger.Fatal(Cat, "fatal msg", properties: null);

        var events = sink.Events.OrderBy(e => e.Message).ToList();
        events.Should().HaveCount(4);

        events.Should().Contain(e => e.Level.Key == "information");
        events.Should().Contain(e => e.Level.Key == "warning");
        events.Should().Contain(e => e.Level.Key == "verbose");
        events.Should().Contain(e => e.Level.Key == "fatal");
    }

    // ── Acceptance field naming ────────────────────────────────────────────

    [Fact]
    public void IsInformationAcceptable_reflects_minimum_level()
    {
        var (logger, _) = BuildPipeline("warning");

        // With minimum=warning, information is BELOW floor → not acceptable.
        logger.IsInformationAcceptable.Should().BeFalse(
            "IsInformationAcceptable must be false when minimum is 'warning'");
        logger.IsWarningAcceptable.Should().BeTrue(
            "IsWarningAcceptable must be true when minimum is 'warning'");
    }

    [Fact]
    public void IsVerboseAcceptable_reflects_minimum_level()
    {
        var (logger, _) = BuildPipeline("information");

        logger.IsVerboseAcceptable.Should().BeFalse(
            "IsVerboseAcceptable must be false when minimum is 'information'");
    }

    [Fact]
    public void IsFatalAcceptable_is_always_true_when_minimum_is_verbose()
    {
        var (logger, _) = BuildPipeline("verbose");

        logger.IsFatalAcceptable.Should().BeTrue(
            "IsFatalAcceptable must be true when minimum is 'verbose'");
    }

    // ── Fast-path variant (InformationFast) ───────────────────────────────

    [Fact]
    public void InformationFast_routes_to_information_level()
    {
        var (logger, sink) = BuildPipeline("verbose");

        logger.InformationFast(Cat, "fast {X}", ("X", 42));

        sink.Events.Should().ContainSingle(e => e.Level.Key == "information",
            "InformationFast() must route to KnownLogLevels.Information");
    }

    [Fact]
    public void WarningFast_routes_to_warning_level()
    {
        var (logger, sink) = BuildPipeline("verbose");

        logger.WarningFast(Cat, "fast {X}", ("X", 42));

        sink.Events.Should().ContainSingle(e => e.Level.Key == "warning",
            "WarningFast() must route to KnownLogLevels.Warning");
    }

    [Fact]
    public void VerboseFast_routes_to_verbose_level()
    {
        var (logger, sink) = BuildPipeline("verbose");

        logger.VerboseFast(Cat, "fast {X}", ("X", 42));

        sink.Events.Should().ContainSingle(e => e.Level.Key == "verbose",
            "VerboseFast() must route to KnownLogLevels.Verbose");
    }

    // ── ILogger<T> verb routing ───────────────────────────────────────────

    [Fact]
    public void ILoggerT_Information_routes_to_information_level()
    {
        var (result, sink) = BuildPipelineResult("verbose");
        var typedLogger = result.CreateTypedLogger<TypedVerbRenameTests>();

        typedLogger.Information("hi {X}", new MMP.Herald.Templating.LogProperty("X", 1));

        sink.Events.Should().ContainSingle(e => e.Level.Key == "information");
    }

    [Fact]
    public void ILoggerT_Verbose_routes_to_verbose_level()
    {
        var (result, sink) = BuildPipelineResult("verbose");
        var typedLogger = result.CreateTypedLogger<TypedVerbRenameTests>();

        typedLogger.Verbose("v {X}", new MMP.Herald.Templating.LogProperty("X", 1));

        sink.Events.Should().ContainSingle(e => e.Level.Key == "verbose");
    }

    [Fact]
    public void ILoggerT_Fatal_routes_to_fatal_level()
    {
        var (result, sink) = BuildPipelineResult("verbose");
        var typedLogger = result.CreateTypedLogger<TypedVerbRenameTests>();

        typedLogger.Fatal("f {X}", new MMP.Herald.Templating.LogProperty("X", 1));

        sink.Events.Should().ContainSingle(e => e.Level.Key == "fatal");
    }

    // ── Plumbing ──────────────────────────────────────────────────────────

    private static (StructuredLogger logger, CapturingBridge sink) BuildPipeline(
        string minimumLevel = "verbose")
    {
        var (result, sink) = BuildPipelineResult(minimumLevel);
        return (result.Logger, sink);
    }

    private static (MMP.Herald.Quick.QuickLogResult result, CapturingBridge sink) BuildPipelineResult(
        string minimumLevel = "verbose")
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel(minimumLevel)
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();
        return (result, sink);
    }

    private sealed class CapturingBridge : MMP.Herald.ILogger
    {
        public ConcurrentBag<LogEvent> Captured { get; } = new();

        public IReadOnlyList<LogEvent> Events => Captured.ToList();

        public void Log(LogEvent logEvent) => Captured.Add(logEvent);

        public ValueTask LogAsync(LogEvent logEvent,
            CancellationToken cancellationToken = default)
        {
            Captured.Add(logEvent);
            return ValueTask.CompletedTask;
        }
    }
}
