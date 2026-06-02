#nullable enable

using FluentAssertions;
// These using directives are the assertion: every type a consumer reaches in real
// Serilog via `using Serilog;` / `using Serilog.Core;` / `using Serilog.Events;` /
// `using Serilog.Formatting;` must resolve through the matching
// `using MMP.Herald.Serilog.*` namespace and nothing deeper. If a type ever moved
// to a non-mirrored namespace, this file would stop compiling.
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Serilog.Formatting;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// Pins Herald's Layer-1 Serilog-compat surface to real Serilog's namespace tree
/// so a from-scratch migration is a single mechanical
/// <c>Serilog</c> -> <c>MMP.Herald.Serilog</c> namespace swap.
///
/// <para>
/// Each row asserts <c>typeof(T).Namespace</c> equals the namespace that real
/// Serilog ships the same type in, prefixed with <c>MMP.Herald.</c>. The root cases
/// (<see cref="LoggerConfiguration"/>, <see cref="RollingInterval"/>) are the ones
/// that previously sat under <c>.Configuration</c> and broke the clean swap.
/// </para>
/// </summary>
public sealed class NamespaceMirrorTests
{
    // Serilog namespace -> expected Herald namespace.
    private const string Root = "MMP.Herald.Serilog";          // Serilog
    private const string CoreNs = "MMP.Herald.Serilog.Core";       // Serilog.Core
    private const string EventsNs = "MMP.Herald.Serilog.Events";     // Serilog.Events
    private const string FormattingNs = "MMP.Herald.Serilog.Formatting"; // Serilog.Formatting

    [Theory]
    // Root namespace (Serilog.*) — the primary flatten target.
    [InlineData(typeof(LoggerConfiguration), Root)]
    [InlineData(typeof(RollingInterval), Root)]
    [InlineData(typeof(Log), Root)]
    // Fully qualified: Herald also ships its own MMP.Herald.ILogger, so an
    // unqualified ILogger under `using MMP.Herald;` would bind to the wrong one.
    // The Serilog-compat ILogger is the one that must sit at the mirrored root.
    [InlineData(typeof(MMP.Herald.Serilog.ILogger), Root)]
    // Serilog.Core.*
    [InlineData(typeof(ILogEventEnricher), CoreNs)]
    [InlineData(typeof(ILogEventSink), CoreNs)]
    [InlineData(typeof(IDestructuringPolicy), CoreNs)]
    [InlineData(typeof(LoggingLevelSwitch), CoreNs)]
    [InlineData(typeof(ILogEventPropertyFactory), CoreNs)]
    [InlineData(typeof(ILogEventPropertyValueFactory), CoreNs)]
    // Serilog.Events.*
    [InlineData(typeof(LogEvent), EventsNs)]
    [InlineData(typeof(LogEventLevel), EventsNs)]
    [InlineData(typeof(LogEventProperty), EventsNs)]
    [InlineData(typeof(LogEventPropertyValue), EventsNs)]
    [InlineData(typeof(ScalarValue), EventsNs)]
    // Serilog.Formatting.*
    [InlineData(typeof(ITextFormatter), FormattingNs)]
    public void Type_resolves_at_its_serilog_mirrored_namespace(System.Type type, string expectedNamespace)
    {
        type.Namespace.Should().Be(
            expectedNamespace,
            $"{type.Name} must live at the Serilog-mirrored namespace so " +
            "`using Serilog...` -> `using MMP.Herald.Serilog...` is a clean find-replace");
    }

    /// <summary>
    /// The clean-swap shape itself: a consumer who imports only the root namespace
    /// can build a logger end to end. This is the exact code a migrated consumer
    /// ends up with after the Serilog -> MMP.Herald.Serilog rename, minus the
    /// WriteTo.Console() sink (covered elsewhere) to keep the test allocation-free
    /// and console-free.
    /// </summary>
    [Fact]
    public void Root_using_alone_resolves_LoggerConfiguration_and_RollingInterval()
    {
        // Resolves LoggerConfiguration (root) with no .Configuration import in scope.
        var config = new LoggerConfiguration();
        config.Should().NotBeNull();

        // Resolves RollingInterval (root) with no .Configuration import in scope.
        var interval = RollingInterval.Day;
        interval.Should().Be(RollingInterval.Day);
    }
}
