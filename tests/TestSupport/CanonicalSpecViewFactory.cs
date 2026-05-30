#nullable enable
// Factory that converts a CanonicalEventSpec to a FakeSerilogEventView.
// Kept in a separate file so the linter does not strip it from FakeSerilogEventView.cs.
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MMP.Herald.Serilog.Events;
using MMP.Herald.Serilog.Formatting;

namespace MMP.Herald.OSS.Tests.TestSupport;

/// <summary>
/// Converts a <see cref="CanonicalEventSpec"/> into a <see cref="FakeSerilogEventView"/>
/// whose <see cref="FakeSerilogEventView.Properties"/> and
/// <see cref="FakeSerilogEventView.RenderedMessage"/> mirror what the real projector
/// and oracle produce for the same spec.
///
/// <para>
/// This factory is the bridge between the oracle-driven test inputs and the Herald
/// renderer under test. Token-renderer tests call
/// <see cref="Build"/> to obtain the view, then compare the renderer output against
/// <see cref="SerilogFormattingOracle.RenderOutputTemplate"/>.
/// </para>
/// </summary>
internal static class CanonicalSpecViewFactory
{
    /// <summary>
    /// Build a <see cref="FakeSerilogEventView"/> that matches what the P1 mirror
    /// and oracle would produce for <paramref name="spec"/>.
    ///
    /// <para>
    /// <see cref="LogEventValueProjector.DefaultValueFactory"/> projects each
    /// spec property (respecting the <c>Destructure</c> flag) into a
    /// <see cref="LogEventPropertyValue"/> node. The <c>RenderedMessage</c> is
    /// obtained from the oracle so the pre-rendered string matches real Serilog.
    /// </para>
    /// </summary>
    [RequiresUnreferencedCode(
        "Uses LogEventValueProjector.DefaultValueFactory which calls reflection on arbitrary user types.")]
    internal static FakeSerilogEventView Build(CanonicalEventSpec spec)
    {
        var factory   = LogEventValueProjector.DefaultValueFactory;
        var propDict  = new Dictionary<string, LogEventPropertyValue>(
            spec.Properties.Count, StringComparer.Ordinal);
        var holeNames = new List<string>(spec.Properties.Count);

        foreach (var (name, value, destructure) in spec.Properties)
        {
            propDict[name] = factory.CreatePropertyValue(value, destructure);
            holeNames.Add(name);
        }

        // Pre-render the message using the oracle to get the default (quoted-string) form.
        // This is the string the {Message} token (no specifier) returns via RenderedMessage.
        var renderedMessage = SerilogFormattingOracle
            .RenderOutputTemplate("{Message}", spec)
            .TrimEnd('\n', '\r');

        // Map the level key to the Serilog LogEventLevel enum.
        var level = spec.LevelKey switch
        {
            "verbose"     => LogEventLevel.Verbose,
            "debug"       => LogEventLevel.Debug,
            "warning"     => LogEventLevel.Warning,
            "error"       => LogEventLevel.Error,
            "fatal"       => LogEventLevel.Fatal,
            _             => LogEventLevel.Information,
        };

        return new FakeSerilogEventView
        {
            Timestamp         = spec.Timestamp,
            Level             = level,
            MessageTemplate   = spec.MessageTemplate,
            RenderedMessage   = renderedMessage,
            Properties        = propDict,
            Exception         = spec.Exception,
            TemplateHoleNames = holeNames,
        };
    }
}

#endif
