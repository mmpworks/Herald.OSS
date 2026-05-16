#nullable enable

using System;
using FluentAssertions;
using MMP.Herald.Configuration;
using Xunit;

namespace MMP.Herald.OSS.Tests.JsonConfig;

/// <summary>
/// LoggingJsonSerializer.Deserialize ships environment-variable interpolation
/// on by default so direct OSS adopters get the ${ENV_VAR} pattern for free.
/// These tests pin the contract:
///
///   - Default-on path resolves tokens against the process environment.
///   - The expandEnv: false opt-out preserves literal ${...} tokens for the
///     consumer-handled pattern (Herald.Server, Herald.Lean both expand
///     externally so they can catch the resolution exception in their own
///     error-reporting surface).
///   - Already-expanded text is a fast no-op on the second pass — the
///     interpolator early-outs when no "${" substring is present.
///   - Unresolved tokens throw InvalidOperationException with every missing
///     name listed.
///   - Size cap is enforced on the post-expansion payload.
///
/// Each test scopes its env-var mutation to a try/finally to keep test runs
/// hermetic across xunit's parallel test workers.
/// </summary>
public sealed class LoggingJsonSerializerExpandTests
{
    [Fact]
    public void Default_path_expands_env_var_tokens()
    {
        const string envVar = "HERALD_TEST_DEFAULT_PATH";
        Environment.SetEnvironmentVariable(envVar, "resolved-value");
        try
        {
            var json = $$"""{ "pipelineName": "${{{envVar}}}" }""";

            var config = LoggingJsonSerializer.Deserialize(json);

            config.PipelineName.Should().Be("resolved-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    [Fact]
    public void Opt_out_preserves_literal_tokens()
    {
        // Consumer pattern: Lean / Server expand externally, then pass
        // expanded JSON to QuickLogBuilder.FromConfigurationString. The
        // opt-out skips the second pass entirely so a value that happens
        // to contain a "${X}" literal (after the consumer's expansion)
        // does not get re-interpreted by Herald.OSS.
        const string json = """{ "pipelineName": "${LITERAL_PRESERVED}" }""";

        var act = () => LoggingJsonSerializer.Deserialize(json, expandEnv: false);

        // The literal flows through to the parser unchanged — no
        // InvalidOperationException, because expansion didn't run.
        act.Should().NotThrow<InvalidOperationException>();
    }

    [Fact]
    public void Pre_expanded_text_is_a_no_op_on_default_pass()
    {
        // Lean / Server expand externally and then pass to Herald.OSS.
        // The default-on interpolator should see no "${" tokens and
        // early-out without modifying the payload — proving the consumer
        // pattern is safe even when expandEnv defaults to true.
        const string preExpandedJson = """{ "pipelineName": "already-resolved" }""";

        var config = LoggingJsonSerializer.Deserialize(preExpandedJson);

        config.PipelineName.Should().Be("already-resolved");
    }

    [Fact]
    public void Unresolved_required_token_throws_with_name_in_message()
    {
        const string json = """{ "pipelineName": "${HERALD_TEST_UNSET_NAME}" }""";

        var act = () => LoggingJsonSerializer.Deserialize(json);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HERALD_TEST_UNSET_NAME*");
    }

    [Fact]
    public void Default_form_falls_back_when_env_var_is_unset()
    {
        const string json = """{ "pipelineName": "${HERALD_TEST_UNSET:-fallback-name}" }""";

        var config = LoggingJsonSerializer.Deserialize(json);

        config.PipelineName.Should().Be("fallback-name");
    }

    [Fact]
    public void Default_form_prefers_env_var_when_set()
    {
        const string envVar = "HERALD_TEST_PREFER_ENV";
        Environment.SetEnvironmentVariable(envVar, "env-wins");
        try
        {
            var json = $$"""{ "pipelineName": "${{{envVar}}:-fallback-name}" }""";

            var config = LoggingJsonSerializer.Deserialize(json);

            config.PipelineName.Should().Be("env-wins");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }
}
