#nullable enable

using System;
using MMP.Herald.Configuration;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Regression: a fluent PipelineStrategy.Custom(name) step that resolves to no
/// handler and no custom decorator used to be SILENTLY DROPPED from the
/// assembled pipeline — a consumer who typed .Custom("retry") without loading
/// the Pro plugin shipped without the protection the step names, and nothing
/// said so. The JSON path (PipelineStrategy.FromNames) always threw for the
/// same mistake; the fluent path now matches it at build time.
/// </summary>
public sealed class SilentStepDropRegressionTests
{
    [Fact]
    public void Fluent_custom_step_with_no_handler_and_no_decorator_throws_at_build()
    {
        var builder = QuickLogBuilder.Create("silent-step-drop-regression")
            .WithConsoleSink()
            .WithPipelineStrategy(
                PipelineStrategy.Create().Custom("no_such_step_regression"));

        var ex = Assert.Throws<InvalidOperationException>(() => builder.BuildAndCommit());

        Assert.Contains("no_such_step_regression", ex.Message);
        Assert.Contains("WithPipelineDecorator", ex.Message);
        Assert.Contains("WithPlugin", ex.Message);
    }

    [Fact]
    public void Json_and_fluent_paths_agree_that_unknown_steps_are_loud()
    {
        // The JSON path throws at PARSE time (name not registered)...
        var jsonEx = Assert.Throws<ArgumentException>(
            () => PipelineStrategy.FromNames(["definitely_not_a_step"]));
        Assert.Contains("Unknown pipeline step", jsonEx.Message);

        // ...the fluent path can't (Custom() self-registers unknown names for
        // round-tripping), so its guard fires at build. Either way: loud.
        var buildEx = Assert.Throws<InvalidOperationException>(
            () => QuickLogBuilder.Create("silent-step-drop-parity")
                .WithConsoleSink()
                .WithPipelineStrategy(PipelineStrategy.Create().Custom("definitely_not_a_step2"))
                .BuildAndCommit());
        Assert.Contains("definitely_not_a_step2", buildEx.Message);
    }
}
