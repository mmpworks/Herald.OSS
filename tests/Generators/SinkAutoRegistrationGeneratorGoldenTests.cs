#nullable enable

using FluentAssertions;
using MMP.Herald.Generators;
using Xunit;

namespace MMP.Herald.OSS.Tests.Generators;

/// <summary>
/// Golden-file regression tests for
/// <see cref="SinkAutoRegistrationGenerator"/>. The generator detects
/// <c>ILogSinkProvider</c> implementations in a sink assembly and emits
/// a <c>[ModuleInitializer]</c> hook that auto-registers them into
/// <c>LogSinkProviderRegistry.Default</c>. Catches the case where a
/// future generator edit narrows detection (e.g., requires a specific
/// attribute) and silently drops registrations — Herald.Sinks.* packages
/// would compile but auto-registration would stop firing.
/// </summary>
public sealed class SinkAutoRegistrationGeneratorGoldenTests
{
    [Fact]
    public void Generator_emits_module_initializer_for_sink_provider_class()
    {
        // A class implementing ILogSinkProvider should trigger the
        // generator's emit. Stub the interface call surface to mirror
        // what the real ILogSinkProvider would look like; the generator
        // detects by interface name, not by full symbol.
        const string source = """
            using MMP.Herald.Routing;

            namespace Sample.SinkPackage.Providers;

            public sealed class TestSinkProvider : ILogSinkProvider
            {
                public string SinkKind => "test";
                public bool Supports(string kind) => kind == "test";
                public MMP.Herald.ILogger CreateSink(
                    MMP.Herald.Configuration.Runtime.LoggingRuntimeSinkDefinition definition,
                    MMP.Herald.Time.IDateTimeProvider dateTimeProvider) =>
                    throw new System.NotImplementedException();
            }
            """;

        var trees = GeneratorGoldenTestHarness.RunGenerator(new SinkAutoRegistrationGenerator(), source);
        var text = GeneratorGoldenTestHarness.AllGeneratedText(trees);

        trees.Should().NotBeEmpty("the generator must emit a __SinkAutoRegistration__ module initializer when ILogSinkProvider implementations are present");

        text.Should().Contain("ModuleInitializer",
            "the registration hook must be a [ModuleInitializer] so it fires at assembly load");
        text.Should().Contain("LogSinkProviderRegistry",
            "the hook body must call into LogSinkProviderRegistry");
        text.Should().Contain("TestSinkProvider",
            "the detected provider class must be referenced by name in the generated registration");
    }

    [Fact]
    public void Generator_emits_nothing_when_assembly_has_no_providers()
    {
        // Compilation with no ILogSinkProvider implementations should
        // produce no generator output — the [ModuleInitializer] hook
        // only makes sense when there's something to register.
        const string source = """
            namespace Sample.Empty;

            public sealed class NotAProvider
            {
                public string Name => "stub";
            }
            """;

        var trees = GeneratorGoldenTestHarness.RunGenerator(new SinkAutoRegistrationGenerator(), source);

        trees.Should().BeEmpty(
            "no ILogSinkProvider implementations means no registration hook should be emitted");
    }
}
