#nullable enable
using System;
using System.IO;
using FluentAssertions;
using global::Serilog;
using global::Serilog.Events;
using global::Serilog.Formatting.Display;
using Xunit;
using Xunit.Abstractions;

namespace MMP.Herald.OSS.Tests.Output.Serilog;

/// <summary>
/// Oracle probe tests to discover the exact {Properties} output format Serilog uses.
/// These tests are read-only probes — they log oracle output to the test runner
/// so we can pin the exact format before implementing SerilogPropertiesRenderer.
/// Not part of the permanent test suite.
/// </summary>
public sealed class SerilogPropertiesOracleProbeTests
{
    private readonly ITestOutputHelper _output;
    public SerilogPropertiesOracleProbeTests(ITestOutputHelper output) => _output = output;

    private static global::Serilog.Events.LogEvent Capture(
        Action<global::Serilog.ILogger> log)
    {
        global::Serilog.Events.LogEvent? captured = null;
        using var logger = new global::Serilog.LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new CaptureSink(e => captured = e))
            .CreateLogger();
        log(logger);
        return captured ?? throw new InvalidOperationException("No event captured");
    }

    private static string Render(string template, global::Serilog.Events.LogEvent evt)
    {
        var formatter = new MessageTemplateTextFormatter(template);
        var writer = new StringWriter();
        formatter.Format(evt, writer);
        return writer.ToString();
    }

    private sealed class CaptureSink : global::Serilog.Core.ILogEventSink
    {
        private readonly Action<global::Serilog.Events.LogEvent> _capture;
        public CaptureSink(Action<global::Serilog.Events.LogEvent> capture) => _capture = capture;
        public void Emit(global::Serilog.Events.LogEvent logEvent) => _capture(logEvent);
    }

    [Fact]
    public void Probe_mixed_extra_property()
    {
        var evt = Capture(l => l
            .ForContext("RequestId", "abc123")
            .Write(LogEventLevel.Information, "User {UserId} did {Action}", 42, "purchase"));
        var output = Render("{Properties}", evt);
        _output.WriteLine($"Case1 mixed [{output}]");
        true.Should().BeTrue();
    }

    [Fact]
    public void Probe_all_in_template()
    {
        var evt = Capture(l => l.Write(LogEventLevel.Information,
            "User {UserId} did {Action}", 42, "purchase"));
        var output = Render("{Properties}", evt);
        _output.WriteLine($"Case2 all-in-template [{output}]");
        true.Should().BeTrue();
    }

    [Fact]
    public void Probe_none_in_template()
    {
        var evt = Capture(l => l
            .ForContext("X", 1)
            .ForContext("Y", 2)
            .Write(LogEventLevel.Information, "Something happened"));
        var output = Render("{Properties}", evt);
        _output.WriteLine($"Case3 none-in-template [{output}]");
        true.Should().BeTrue();
    }

    [Fact]
    public void Probe_empty_props()
    {
        var evt = Capture(l => l.Write(LogEventLevel.Information, "ping"));
        var output = Render("{Properties}", evt);
        _output.WriteLine($"Case4 empty [{output}]");
        true.Should().BeTrue();
    }

    [Fact]
    public void Probe_integer_extra()
    {
        var evt = Capture(l => l
            .ForContext("Count", 5)
            .Write(LogEventLevel.Information, "User {UserId} logged in", 42));
        var output = Render("{Properties}", evt);
        _output.WriteLine($"Case5 integer extra [{output}]");
        true.Should().BeTrue();
    }

    [Fact]
    public void Probe_null_value()
    {
        var evt = Capture(l => l
            .ForContext("NullProp", (object?)null)
            .Write(LogEventLevel.Information, "Something {X}", 1));
        var output = Render("{Properties}", evt);
        _output.WriteLine($"Case6 null value [{output}]");
        true.Should().BeTrue();
    }

    [Fact]
    public void Probe_format_specifier_j()
    {
        var evt = Capture(l => l
            .ForContext("RequestId", "abc123")
            .Write(LogEventLevel.Information, "User {UserId} did {Action}", 42, "purchase"));
        var withJ = Render("{Properties:j}", evt);
        var noFormat = Render("{Properties}", evt);
        _output.WriteLine($"Case7 {{Properties}} [{noFormat}]");
        _output.WriteLine($"Case7 {{Properties:j}} [{withJ}]");
        true.Should().BeTrue();
    }

    [Fact]
    public void Probe_string_value_quoting()
    {
        // Key question: are string scalars quoted in {Properties}?
        var evt = Capture(l => l
            .ForContext("Name", "Alice")
            .ForContext("Count", 42)
            .Write(LogEventLevel.Information, "Hello"));
        var output = Render("{Properties}", evt);
        _output.WriteLine($"Case8 string+int quoting [{output}]");
        true.Should().BeTrue();
    }

    [Fact]
    public void Probe_multiple_extra_properties()
    {
        // Check separator used between multiple extra properties
        var evt = Capture(l => l
            .ForContext("A", "x")
            .ForContext("B", "y")
            .ForContext("C", 3)
            .Write(LogEventLevel.Information, "User {UserId}", 1));
        var output = Render("{Properties}", evt);
        _output.WriteLine($"Case9 multi-extra separator [{output}]");
        true.Should().BeTrue();
    }

    [Fact]
    public void Probe_format_l_specifier()
    {
        var evt = Capture(l => l
            .ForContext("Name", "Alice")
            .ForContext("Count", 42)
            .Write(LogEventLevel.Information, "Hello"));
        var withL = Render("{Properties:l}", evt);
        var noFmt = Render("{Properties}", evt);
        _output.WriteLine($"Case10 {{Properties:l}} [{withL}]");
        _output.WriteLine($"Case10 {{Properties}} [{noFmt}]");
        true.Should().BeTrue();
    }

    [Fact]
    public void Probe_exact_empty_bytes()
    {
        // Check exactly what chars are in the empty case
        var evt = Capture(l => l.Write(LogEventLevel.Information, "ping"));
        var output = Render("{Properties}", evt);
        _output.WriteLine($"Case11 empty len={output.Length} chars=[{string.Join(",", System.Linq.Enumerable.Select(output, c => (int)c))}]");
        true.Should().BeTrue();
    }
}
