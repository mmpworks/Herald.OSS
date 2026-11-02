#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using FluentAssertions;
using MMP.Herald.Serilog;
using Xunit;

namespace MMP.Herald.OSS.Tests.Output;

/// <summary>
/// Log4J-class pin: a log ARGUMENT is data. Its text reaches the output as
/// literal characters and is never parsed as a lookup (<c>${jndi:...}</c>) or
/// as a second message template (<c>{Name}</c>).
///
/// <para>
/// Both shipped storage formatters are covered end to end: <c>.log</c> routes
/// through <c>PlainTextFormatter</c> and <c>.jsonl</c> through
/// <c>JsonFormatter</c>, so the template renderer runs before each formatter.
/// </para>
/// </summary>
public sealed class LogArgumentLiteralRenderingTests
{
    private const string JndiLookup = "${jndi:ldap://x}";
    private const string TemplateHole = "{Name}";
    private const string Template = "lookup {Lookup} hole {Hole}";
    private const string Marker = "LITERAL-ARG-PIN";
    private const int FlushPollMs = 2_000;
    private const int FlushPollStepMs = 25;

    [Fact]
    public void Text_formatter_renders_lookup_and_hole_arguments_as_literal_text()
    {
        var written = WriteOneEvent("app.log");

        written.Should().Contain($"lookup {JndiLookup} hole {TemplateHole} {Marker}",
            "a string argument is rendered verbatim; the text formatter never interprets it.");
    }

    [Fact]
    public void Json_formatter_renders_lookup_and_hole_arguments_as_literal_text()
    {
        var line = WriteOneEvent("app.jsonl")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(l => l.Contains(Marker, StringComparison.Ordinal));

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        var properties = root.GetProperty("properties");

        // JSON output carries the template and the properties; on the kernel path
        // the rendered "message" field is empty by design, so the pin reads these two.
        root.GetProperty("message_template").GetString().Should().Be(
            Template + " " + Marker,
            "an argument never merges into the template.");
        properties.GetProperty("Lookup").GetProperty("value").GetString().Should().Be(JndiLookup,
            "a lookup string is stored as data, never resolved.");
        properties.GetProperty("Hole").GetProperty("value").GetString().Should().Be(TemplateHole,
            "a template hole inside an argument is stored as data, never rendered again.");
    }

    // Bare compat surface -> WriteTo.File -> one event -> flush -> file text.
    private static string WriteOneEvent(string fileName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "herald-literal-arg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);

        try
        {
            var logger = new LoggerConfiguration()
                .WriteTo.File(path)
                .CreateLogger();

            logger.Information(Template + " " + Marker, JndiLookup, TemplateHole);

            (logger as IDisposable)?.Dispose();

            return ReadWhenAvailable(path);
        }
        finally
        {
            TryCleanup(dir);
        }
    }

    private static string ReadWhenAvailable(string path)
    {
        var deadline = Environment.TickCount + FlushPollMs;
        while (Environment.TickCount < deadline)
        {
            if (File.Exists(path))
            {
                var text = ReadShared(path);
                if (text.Contains(Marker, StringComparison.Ordinal))
                    return text;
            }
            Thread.Sleep(FlushPollStepMs);
        }

        return File.Exists(path) ? ReadShared(path) : string.Empty;
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void TryCleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // A sink still holding the file on a slow runner leaves a temp dir behind.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
