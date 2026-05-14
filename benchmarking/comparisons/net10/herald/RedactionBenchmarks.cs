#nullable enable

using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline.Processors;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Redaction cost. Three Herald shapes on the same one-property emit:
///
/// <list type="bullet">
///   <item><b>Baseline</b>: no redaction wired. Reference point.</item>
///   <item><b>WithFastRedaction</b>: kernel-eligible fast-path redactor.
///       Rule runs on the property span before the
///       <see cref="MMP.Herald.Pipeline.Kernel.LogEventBuffer"/> is
///       constructed. Stays on the kernel fast path.</item>
///   <item><b>WithCompiledRedaction</b>: event-processor redactor.
///       Runs after LogEvent materialization. Heavier per call but
///       supports the full rule DSL (glob, regex, when-predicates,
///       value patterns, drop-event actions).</item>
/// </list>
///
/// <para>
/// The rule itself is the same shape in all three (an exact-name
/// match on <c>Email</c> with mask mode + 2 visible chars), so the
/// per-call delta is the rule-execution cost, not configuration
/// shape.
/// </para>
///
/// <para>
/// Competitor rows for redaction come in a later iteration. Serilog
/// has destructuring policies; NLog has property filters; ZLogger
/// has format-time redaction; log4net has nothing built-in. Each is
/// shaped differently from Herald's two redaction paths, so the
/// comparison narrative carries asymmetry that the doc must
/// acknowledge.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class RedactionBenchmarks
{
    private QuickLogResult _baseline = null!;
    private QuickLogResult _fastRedaction = null!;
    private QuickLogResult _compiledRedaction = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rule = new CompiledRedactionRule(
            PropertyNamePattern: "Email",
            Mode: RedactionMode.Mask,
            MaskChar: '*',
            VisibleChars: 2);

        _baseline = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        _fastRedaction = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .WithFastRedaction(rule)
            .BuildAndCommit();

        _compiledRedaction = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .WithCompiledRedaction(rule)
            .BuildAndCommit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        DisposeAsync(_baseline);
        DisposeAsync(_fastRedaction);
        DisposeAsync(_compiledRedaction);
    }

    [Benchmark(Baseline = true)]
    public void Herald_Baseline_NoRedaction()
    {
        _baseline.Logger.Info(LogCategory.App, "user {Email} logged in", "alice@example.com");
    }

    [Benchmark]
    public void Herald_WithFastRedaction()
    {
        _fastRedaction.Logger.Info(LogCategory.App, "user {Email} logged in", "alice@example.com");
    }

    [Benchmark]
    public void Herald_WithCompiledRedaction()
    {
        _compiledRedaction.Logger.Info(LogCategory.App, "user {Email} logged in", "alice@example.com");
    }

    private static void DisposeAsync(QuickLogResult? result)
    {
        if (result?.AsyncResource is { } resource)
        {
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
