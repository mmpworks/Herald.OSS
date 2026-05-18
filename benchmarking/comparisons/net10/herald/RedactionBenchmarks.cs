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

    // G11: 16-prop emit pipelines with two rules wired (Email mask,
    // Password remove). Realistic compliance shape — two redactable
    // properties scattered through a 16-property telescope event.
    private QuickLogResult _baseline16 = null!;
    private QuickLogResult _fastRedaction16 = null!;
    private QuickLogResult _compiledRedaction16 = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rule = new CompiledRedactionRule(
            PropertyNamePattern: "Email",
            Mode: RedactionMode.Mask,
            MaskChar: '*',
            VisibleChars: 2);

        var maskEmail = new CompiledRedactionRule(
            PropertyNamePattern: "Email",
            Mode: RedactionMode.Mask,
            MaskChar: '*',
            VisibleChars: 2);

        var removePassword = new CompiledRedactionRule(
            PropertyNamePattern: "Password",
            Mode: RedactionMode.Remove);

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

        _baseline16 = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        _fastRedaction16 = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .WithFastRedaction(maskEmail, removePassword)
            .BuildAndCommit();

        _compiledRedaction16 = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .WithCompiledRedaction(maskEmail, removePassword)
            .BuildAndCommit();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        DisposeAsync(_baseline);
        DisposeAsync(_fastRedaction);
        DisposeAsync(_compiledRedaction);
        DisposeAsync(_baseline16);
        DisposeAsync(_fastRedaction16);
        DisposeAsync(_compiledRedaction16);
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

    // ── 16-prop redaction shapes (G11) ───────────────────────────
    // Template scatters Email and Password through 16 placeholders
    // so two rules fire per emit. Pins the rule-execution cost
    // against a matched 16-prop baseline with no redaction wired.

    private const string SixteenPropTemplate =
        "event {A} {B} {Email} {D} {E} {F} {G} {H} {Password} {J} {K} {L} {M} {N} {O} {P}";

    [Benchmark]
    public void Herald_Baseline_NoRedaction_SixteenProps()
    {
        _baseline16.Logger.Info(LogCategory.App, SixteenPropTemplate,
            "a", "b", "alice@example.com", "d", "e", "f", "g", "h",
            "secret123", "j", "k", "l", "m", "n", "o", "p");
    }

    [Benchmark]
    public void Herald_WithFastRedaction_SixteenProps_TwoRulesFire()
    {
        _fastRedaction16.Logger.Info(LogCategory.App, SixteenPropTemplate,
            "a", "b", "alice@example.com", "d", "e", "f", "g", "h",
            "secret123", "j", "k", "l", "m", "n", "o", "p");
    }

    [Benchmark]
    public void Herald_WithCompiledRedaction_SixteenProps_TwoRulesFire()
    {
        _compiledRedaction16.Logger.Info(LogCategory.App, SixteenPropTemplate,
            "a", "b", "alice@example.com", "d", "e", "f", "g", "h",
            "secret123", "j", "k", "l", "m", "n", "o", "p");
    }

    private static void DisposeAsync(QuickLogResult? result)
    {
        if (result?.AsyncResource is { } resource)
        {
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
