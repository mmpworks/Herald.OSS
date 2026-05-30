#nullable enable
// Oracle for output-template and CLEF parity tests (P3 Task 1).
// Gated to net9+ — MMP.Herald.Serilog targets net9/net10 only, and
// Serilog.Formatting.Compact is pulled in solely for oracle use in the test project.
#if NET9_0_OR_GREATER

using System;
using System.IO;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Formatting.Display;

namespace MMP.Herald.OSS.Tests.TestSupport;

/// <summary>
/// Drives real Serilog's formatting APIs to produce trusted reference output for
/// output-template and CLEF parity tests.
///
/// <para>
/// The oracle is the load-bearing fixture for every P3 grammar test — without it,
/// "matches Serilog" is just an assertion against our own guess. All assertions in
/// Tasks 2–8 compare Herald's renderer output to <see cref="RenderOutputTemplate"/>
/// or <see cref="RenderClef"/>, never to a hand-written expected string.
/// </para>
///
/// <para>
/// <b>Pattern.</b> Builds a real Serilog <see cref="ILogger"/> with a capturing
/// sink (same machinery as <see cref="SerilogParityOracle.CaptureSerilog"/>),
/// drives the log call, then feeds the captured <see cref="LogEvent"/> into the
/// formatting API under test.
/// </para>
/// </summary>
public static class SerilogFormattingOracle
{
    // Named group regex used to rewrite message templates so destructure-mode
    // holes get the @ prefix before the oracle logs. Example:
    //   template "Logged in as {@User}" with Destructure=true -> already correct
    //   template "Logged in as {User}"  with Destructure=true -> rewrite {User} -> {@User}
    // The oracle needs the real Serilog template to carry the capture-mode operator;
    // Herald captures the intent through CanonicalEventSpec.Properties[i].Destructure.
    private static readonly Regex HolePattern =
        new Regex(@"\{(?<name>[^@$!:,}][^:,}]*)[^}]*\}", RegexOptions.Compiled);

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders <paramref name="spec"/> through real Serilog's
    /// <see cref="MessageTemplateTextFormatter"/> with the given
    /// <paramref name="outputTemplate"/> and returns the formatted string.
    ///
    /// <para>
    /// This is the primary oracle for P3 Tasks 2–7: every token renderer test
    /// asserts <c>heraldOutput.Should().Be(SerilogFormattingOracle.RenderOutputTemplate(...))</c>.
    /// </para>
    /// </summary>
    public static string RenderOutputTemplate(string outputTemplate, CanonicalEventSpec spec)
    {
        var formatter = new MessageTemplateTextFormatter(outputTemplate);
        var logEvent  = CaptureLogEvent(spec);
        var writer    = new StringWriter();
        formatter.Format(logEvent, writer);
        return writer.ToString();
    }

    /// <summary>
    /// Renders <paramref name="spec"/> as CLEF (Compact Log Event Format) through real
    /// Serilog's <see cref="CompactJsonFormatter"/> and returns the JSON line
    /// with trailing line-endings stripped.
    ///
    /// <para>
    /// Used by P3 Task 8 (CLEF formatter parity).
    /// </para>
    /// </summary>
    public static string RenderClef(CanonicalEventSpec spec)
    {
        var formatter = new CompactJsonFormatter();
        var logEvent  = CaptureLogEvent(spec);
        var writer    = new StringWriter();
        formatter.Format(logEvent, writer);
        return writer.ToString().TrimEnd('\n', '\r');
    }

    // ── Core capture ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a real Serilog <see cref="LogEvent"/> from <paramref name="spec"/>.
    ///
    /// <para>
    /// Drives real Serilog's <c>ILogger</c> through the same capturing-sink
    /// infrastructure used by <see cref="SerilogParityOracle.CaptureSerilog"/>.
    /// The template is rewritten so destructure-mode holes carry the <c>@</c>
    /// prefix before the oracle logs.
    /// </para>
    /// </summary>
    private static LogEvent CaptureLogEvent(CanonicalEventSpec spec)
    {
        // Build the argument array and rewrite the template for destructure holes.
        var (rewrittenTemplate, args) = BuildArgsAndTemplate(spec);
        var level                     = MapLevel(spec.LevelKey);

        return SerilogParityOracle.CaptureSerilog(logger =>
        {
            // Pass the exception as the first argument if present (Serilog convention:
            // exception is a leading parameter on the Write/Error/etc. overloads).
            if (spec.Exception is { } ex)
                logger.Write(level, ex, rewrittenTemplate, args);
            else
                logger.Write(level, rewrittenTemplate, args);
        });
    }

    // ── Template rewriting ─────────────────────────────────────────────────────

    // Build the argument array (object?[]) and rewrite the message template so
    // destructure-mode holes carry the @ prefix.
    //
    // Cognitive complexity note: linear scan over properties; the regex replacement
    // runs once per hole name when destructure is needed.
    private static (string Template, object?[] Args) BuildArgsAndTemplate(CanonicalEventSpec spec)
    {
        var props    = spec.Properties;
        var args     = new object?[props.Count];
        var template = spec.MessageTemplate;

        for (var i = 0; i < props.Count; i++)
        {
            args[i] = props[i].Value;

            // If the hole at this position is destructure-mode and the template
            // does not already carry the @ prefix, rewrite it.
            if (props[i].Destructure && !TemplateHoleHasPrefix(template, props[i].Name, '@'))
                template = RewriteHole(template, props[i].Name, "@");
        }

        return (template, args);
    }

    // Returns true when the named hole in the template already carries the given prefix.
    // Example: template "{@User}" with name "User" and prefix '@' → true.
    private static bool TemplateHoleHasPrefix(string template, string name, char prefix)
    {
        // Serilog hole syntax: {[@ or $]Name[,alignment][:format]}
        var escaped = Regex.Escape(name);
        var prefixed = new Regex($@"\{{\{prefix}{escaped}[,:]?[^}}]*\}}");
        return prefixed.IsMatch(template);
    }

    // Rewrite the first occurrence of {Name...} (without the prefix) to {@Name...}.
    private static string RewriteHole(string template, string name, string prefix)
    {
        var escaped = Regex.Escape(name);
        // Match {Name} or {Name,alignment} or {Name:format} — no existing @ or $.
        var pattern     = new Regex($@"\{{(?![@$!]){escaped}([,:]?[^}}]*)\}}");
        var replacement = $"{{{prefix}{name}$1}}";
        return pattern.Replace(template, replacement, count: 1);
    }

    // ── Level mapping ──────────────────────────────────────────────────────────

    private static LogEventLevel MapLevel(string levelKey) => levelKey switch
    {
        "verbose"     => LogEventLevel.Verbose,
        "debug"       => LogEventLevel.Debug,
        "information" => LogEventLevel.Information,
        "warning"     => LogEventLevel.Warning,
        "error"       => LogEventLevel.Error,
        "fatal"       => LogEventLevel.Fatal,
        _             => LogEventLevel.Information,
    };
}

#endif
