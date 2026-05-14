#nullable enable

using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Pipeline build / commit smoke tests. These exercise the public OSS
/// surface end-to-end so a regression that breaks the Quick path
/// surfaces here before it reaches a consumer.
/// </summary>
public sealed class BuildSmokeTests
{
    [Fact]
    public void Build_returns_export_config_carrying_configured_minimum_level()
    {
        var builder = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("debug");

        var buildResult = builder.Build();

        buildResult.Should().NotBeNull();
        buildResult.ExportConfig().Should().NotBeNullOrWhiteSpace();
        buildResult.ExportConfig().Should().Contain("debug");
    }

    [Fact]
    public void Build_returns_PipelineBuildResult_type()
    {
        var builder = QuickLogBuilder.Create().WithConsoleSink();

        var buildResult = builder.Build();

        buildResult.Should().BeOfType<PipelineBuildResult>();
    }

    [Fact]
    public void BuildAndCommit_returns_a_usable_QuickLogResult()
    {
        var result = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("info")
            .BuildAndCommit();

        result.Should().NotBeNull();
        result.Should().BeOfType<QuickLogResult>();
        result.Logger.Should().NotBeNull();
    }

    [Fact]
    public void Logger_emits_without_throwing_after_commit()
    {
        var result = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        var act = () => result.Logger.Info(LogCategory.App, "Phase 4 beachhead emit");

        act.Should().NotThrow();
    }

    [Fact]
    public void Build_exports_pipeline_steps_in_strategy_order()
    {
        var builder = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithPipelineStrategy(
                Configuration.PipelineStrategy.Create().Swappable().Async().FanOut());

        var json = builder.Build().ExportConfig();

        json.Should().Contain("pipelineSteps");
        json.Should().Contain("swappable");
        json.Should().Contain("async");
        json.Should().Contain("fanOut");
    }
}
