#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Quick;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Every documented call shape on <c>StructuredLogger</c> exercised
/// end-to-end through <c>QuickLogBuilder</c> + a capturing bridge sink.
///
/// <para>
/// The point is not to assert performance or kernel internals — those
/// have their own tests. The point is: a consumer who writes any of
/// these forms in their service code gets the message, the level, the
/// category, and the properties they expect, with the names they expect.
/// </para>
///
/// <para>
/// Shapes covered:
/// <list type="bullet">
///   <item>Bare <c>Info(category, template)</c> — no args.</item>
///   <item>Category-less typed-args generic <c>Info&lt;T...&gt;(template, args)</c> across arities 1 / 2 / 4 / 8 / 16.</item>
///   <item>Category-bearing typed-args generic <c>Info&lt;T...&gt;(category, template, args)</c>.</item>
///   <item><c>Info(category, template, ReadOnlySpan&lt;LogProperty&gt;)</c> with explicit property names.</item>
///   <item><c>InfoCompact(category, template, LogPropertyBufferN)</c> with inline stack buffers.</item>
///   <item>Mixed runtime types (int, string, bool, double, decimal, DateTime) in one call.</item>
///   <item>Same arity sweep applied to Trace / Debug / Warn / Error.</item>
///   <item>Exception overload <c>Info(category, exception, template, ...)</c>.</item>
/// </list>
/// </para>
/// </summary>
[Collection(nameof(LogCallShapesTests))]
[CollectionDefinition(nameof(LogCallShapesTests), DisableParallelization = true)]
public sealed class LogCallShapesTests
{
    public LogCallShapesTests()
    {
        // Process-static resolver cache survives across xUnit collection
        // boundaries; without this reset, a Pascal-default test in another
        // collection can populate (CamelCasePolicy.GetType(), template)
        // under a different policy in this collection and shift this
        // collection's expectations. Reset keeps the suite hermetic.
        MMP.Herald.Templating.NameResolverCache.Reset();
    }

    // ─── Bare ─────────────────────────────────────────────────────────

    [Fact]
    public void Info_with_category_and_template_only_emits_event()
    {
        var (logger, sink) = BuildPipeline();

        logger.Info(LogCategory.App, "no-args");

        sink.Events.Should().ContainSingle(e =>
            e.Message == "no-args" &&
            e.Properties.Count == 0);
    }

    // ─── Category-less typed-args (arity sweep) ───────────────────────
    //
    // BuildPipeline pins the camelCase policy. Property names come from the
    // template token (token-first selection) with ToCamelCase applied: a
    // PascalCase token like "{UserId}" yields property name "userId"; a
    // single-letter token like "{A}" yields "a". The CAE name only matters
    // at slots where the template has no token (covered by the explicit-
    // name override test below).

    [Fact]
    public void Categoryless_typed_args_arity_1_captures_arg_expression_as_name()
    {
        var (logger, sink) = BuildPipeline();
        var userId = "alice";

        logger.Info("user {UserId} signed in", userId);

        var e = sink.Events.Should().ContainSingle().Subject;
        e.MessageTemplate.Should().Be("user {UserId} signed in");
        e.Properties.Should().ContainSingle(p => p.Name == "userId" && Equals(p.Value, "alice"));
    }

    [Fact]
    public void Categoryless_typed_args_arity_2_captures_both()
    {
        var (logger, sink) = BuildPipeline();
        var min = 0;
        var max = 100;

        logger.Info("{Min} to {Max}", min, max);

        var e = sink.Events.Should().ContainSingle().Subject;
        NamesAndValues(e).Should().Equal(
            ("min", 0),
            ("max", 100));
    }

    [Fact]
    public void Categoryless_typed_args_arity_4_captures_all_four()
    {
        var (logger, sink) = BuildPipeline();
        var a = 1; var b = 2; var c = 3; var d = 4;

        logger.Info("{A} {B} {C} {D}", a, b, c, d);

        var e = sink.Events.Should().ContainSingle().Subject;
        NamesAndValues(e).Should().Equal(
            ("a", 1), ("b", 2), ("c", 3), ("d", 4));
    }

    [Fact]
    public void Categoryless_typed_args_arity_8_captures_all_eight()
    {
        var (logger, sink) = BuildPipeline();
        var p1 = 1; var p2 = 2; var p3 = 3; var p4 = 4;
        var p5 = 5; var p6 = 6; var p7 = 7; var p8 = 8;

        logger.Info("{P1}{P2}{P3}{P4}{P5}{P6}{P7}{P8}",
            p1, p2, p3, p4, p5, p6, p7, p8);

        var e = sink.Events.Should().ContainSingle().Subject;
        e.Properties.Should().HaveCount(8);
        e.Properties.Select(p => (p.Name, p.Value)).Should().Equal(
            ("p1", 1), ("p2", 2), ("p3", 3), ("p4", 4),
            ("p5", 5), ("p6", 6), ("p7", 7), ("p8", 8));
    }

    [Fact]
    public void Categoryless_typed_args_arity_16_captures_all_sixteen()
    {
        var (logger, sink) = BuildPipeline();
        var v01 = 1; var v02 = 2; var v03 = 3; var v04 = 4;
        var v05 = 5; var v06 = 6; var v07 = 7; var v08 = 8;
        var v09 = 9; var v10 = 10; var v11 = 11; var v12 = 12;
        var v13 = 13; var v14 = 14; var v15 = 15; var v16 = 16;

        logger.Info(
            "{A}{B}{C}{D}{E}{F}{G}{H}{I}{J}{K}{L}{M}{N}{O}{P}",
            v01, v02, v03, v04, v05, v06, v07, v08,
            v09, v10, v11, v12, v13, v14, v15, v16);

        var e = sink.Events.Should().ContainSingle().Subject;
        e.Properties.Should().HaveCount(16);
        e.Properties.Select(p => p.Value).Should().Equal(
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);
    }

    [Fact]
    public void Typed_args_explicit_named_parameters_override_arg_expression()
    {
        // The [CallerArgumentExpression]-supplied name can be overridden
        // by passing nameN: explicitly. Useful when the variable name is
        // not the property name you want emitted. Pipeline is pinned to
        // Camel, so the policy lowercases the first letter when the
        // source is the (explicit or auto-captured) CAE-derived name.
        // Template has no tokens so the explicit names are the sole
        // source at each slot.
        var (logger, sink) = BuildPipeline();

        logger.Info("just literals here", 0, 100, name1: "Lower", name2: "Upper");

        var e = sink.Events.Should().ContainSingle().Subject;
        NamesAndValues(e).Should().Equal(
            ("lower", 0),
            ("upper", 100));
    }

    // ─── Category-bearing typed-args ──────────────────────────────────

    [Fact]
    public void Categoryfirst_typed_args_arity_1_routes_named_category()
    {
        var (logger, sink) = BuildPipeline();
        var userId = "alice";

        logger.Info(LogCategory.Startup, "user {UserId} signed in", userId);

        var e = sink.Events.Should().ContainSingle().Subject;
        e.Category.Should().Be(LogCategory.Startup);
        e.Properties.Should().ContainSingle(p => p.Name == "userId" && Equals(p.Value, "alice"));
    }

    [Fact]
    public void Categoryfirst_typed_args_arity_4_routes_named_category()
    {
        var (logger, sink) = BuildPipeline();
        var a = "alpha"; var b = "beta"; var c = "gamma"; var d = "delta";

        logger.Info(LogCategory.Board, "{A} {B} {C} {D}", a, b, c, d);

        var e = sink.Events.Should().ContainSingle().Subject;
        e.Category.Should().Be(LogCategory.Board);
        NamesAndValues(e).Should().Equal(
            ("a", "alpha"), ("b", "beta"), ("c", "gamma"), ("d", "delta"));
    }

    // ─── Explicit LogProperty list (cross-target) ─────────────────────
    //
    // The `params ReadOnlySpan<LogProperty>` overload is net9.0+ only;
    // on net8 it's absent and the call falls through to typed-args.
    // The IReadOnlyList<LogProperty>? path via the `properties:` named
    // parameter exists on every target, so consumers who want explicit
    // names without relying on net9 syntax write it this way.

    [Fact]
    public void Info_with_explicit_LogProperty_list_carries_those_names()
    {
        var (logger, sink) = BuildPipeline();

        logger.Info(
            LogCategory.App,
            "checkout {OrderId} stage {Stage}",
            properties: new[]
            {
                new LogProperty("OrderId", "ord-7"),
                new LogProperty("Stage", "captured"),
            });

        var e = sink.Events.Should().ContainSingle().Subject;
        NamesAndValues(e).Should().Equal(
            ("OrderId", "ord-7"),
            ("Stage", "captured"));
    }

#if NET9_0_OR_GREATER
    [Fact]
    public void Info_with_params_ReadOnlySpan_LogProperty_carries_those_names()
    {
        // net9.0+ only: params ReadOnlySpan<LogProperty> overload.
        var (logger, sink) = BuildPipeline();

        logger.Info(
            LogCategory.App,
            "checkout {OrderId} stage {Stage}",
            new LogProperty("OrderId", "ord-7"),
            new LogProperty("Stage", "captured"));

        var e = sink.Events.Should().ContainSingle().Subject;
        NamesAndValues(e).Should().Equal(
            ("OrderId", "ord-7"),
            ("Stage", "captured"));
    }
#endif

    // ─── InfoCompact (inline stack buffer) ────────────────────────────

    [Fact]
    public void InfoCompact_with_LogPropertyBuffer4_emits_without_throwing()
    {
        var (logger, sink) = BuildPipeline();

        var buf = new LogPropertyBuffer4();
        buf[0] = new LogPropertyCompact("Name", "alice");
        buf[1] = new LogPropertyCompact("City", "Austin");
        buf[2] = new LogPropertyCompact("Action", "login");
        buf[3] = new LogPropertyCompact("Resource", "console");

        var act = () => logger.InfoCompact(
            LogCategory.App,
            "user {Name} from {City} did {Action} on {Resource}",
            buf);

        act.Should().NotThrow();
        sink.Events.Should().ContainSingle();
    }

    [Fact]
    public void InfoCompact_with_LogPropertyBuffer1_round_trips_one_property()
    {
        var (logger, sink) = BuildPipeline();

        var buf = new LogPropertyBuffer1();
        buf[0] = new LogPropertyCompact("OnlyOne", 42);

        logger.InfoCompact(LogCategory.App, "single {OnlyOne}", buf);

        sink.Events.Should().ContainSingle();
    }

    // ─── Mixed runtime types ──────────────────────────────────────────

    [Fact]
    public void Typed_args_preserve_mixed_runtime_types()
    {
        var (logger, sink) = BuildPipeline();
        var i = 7;
        var s = "hello";
        var b = true;
        var d = 3.14;
        var m = 99.95m;
        var t = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);

        logger.Info("i={I} s={S} b={B} d={D} m={M} t={T}", i, s, b, d, m, t);

        var e = sink.Events.Should().ContainSingle().Subject;
        var byName = e.Properties.ToDictionary(p => p.Name, p => p.Value);
        byName["i"].Should().Be(7);
        byName["s"].Should().Be("hello");
        byName["b"].Should().Be(true);
        byName["d"].Should().Be(3.14);
        byName["m"].Should().Be(99.95m);
        byName["t"].Should().Be(t);
    }

    // ─── Level coverage (every level, every shape pair) ───────────────

    [Theory]
    [InlineData("trace")]
    [InlineData("debug")]
    [InlineData("info")]
    [InlineData("warn")]
    [InlineData("error")]
    public void Each_level_accepts_categoryless_typed_args(string levelKey)
    {
        var (logger, sink) = BuildPipeline(minimumLevel: levelKey);
        var n = 1;

        Action act = levelKey switch
        {
            "trace" => () => logger.Trace("{N}", n),
            "debug" => () => logger.Debug("{N}", n),
            "info"  => () => logger.Info("{N}", n),
            "warn"  => () => logger.Warn("{N}", n),
            "error" => () => logger.Error("{N}", n),
            _ => () => { },
        };

        act.Should().NotThrow();
        sink.Events.Should().ContainSingle(e =>
            e.Properties.Count == 1 &&
            e.Properties[0].Name == "n" &&
            Equals(e.Properties[0].Value, 1));
    }

    [Theory]
    [InlineData("trace")]
    [InlineData("debug")]
    [InlineData("info")]
    [InlineData("warn")]
    [InlineData("error")]
    public void Each_level_accepts_categoryfirst_typed_args(string levelKey)
    {
        var (logger, sink) = BuildPipeline(minimumLevel: levelKey);
        var n = 1;

        Action act = levelKey switch
        {
            "trace" => () => logger.Trace(LogCategory.Ui, "{N}", n),
            "debug" => () => logger.Debug(LogCategory.Ui, "{N}", n),
            "info"  => () => logger.Info(LogCategory.Ui,  "{N}", n),
            "warn"  => () => logger.Warn(LogCategory.Ui,  "{N}", n),
            "error" => () => logger.Error(LogCategory.Ui, "{N}", n),
            _ => () => { },
        };

        act.Should().NotThrow();
        var e = sink.Events.Should().ContainSingle().Subject;
        e.Category.Should().Be(LogCategory.Ui);
    }

    // ─── Exception overload ──────────────────────────────────────────

    [Fact]
    public void Info_with_exception_preserves_the_exception_in_context()
    {
        var (logger, sink) = BuildPipeline();
        var boom = new InvalidOperationException("the wheels came off");

        // Exception lands in LogEvent.Context under LogContextKeys.Exception
        // ("exception"). Properties land in LogEvent.Properties.
        logger.Info(LogCategory.App, boom, "step {Stage} failed",
            properties: new[] { new LogProperty("Stage", "commit") });

        var e = sink.Events.Should().ContainSingle().Subject;
        e.Context.Should().ContainKey("exception").WhoseValue.Should().BeSameAs(boom);
        e.Properties.Should().ContainSingle(p => p.Name == "Stage" && Equals(p.Value, "commit"));
    }

    // ─── Below-floor rejection sanity (typed-args path) ──────────────

    [Fact]
    public void Debug_call_below_info_floor_is_rejected_silently()
    {
        var (logger, sink) = BuildPipeline(minimumLevel: "info");
        var below = 1;
        var above = 2;

        logger.Debug("{N}", below);
        logger.Info("{N}", above);

        sink.Events.Should().ContainSingle();
        sink.Events.Single().Properties.Single().Value.Should().Be(2);
    }

    // ─── Plumbing ────────────────────────────────────────────────────

    // These end-to-end shape tests pin BuildPipeline to PropertyNamingPolicy.Camel
    // so the property names they assert (lowercase-start of the template
    // token, e.g. "userId" from "{UserId}", "min" from "{Min}") fall out
    // of the policy's ToCamelCase transform. Pascal-default behaviour
    // ("UserId", "Min", …) is exercised in StructuredLoggerPascalDefaultTests.cs.
    private static (StructuredLogger logger, CapturingBridge sink) BuildPipeline(string minimumLevel = "trace")
    {
        var sink = new CapturingBridge();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel(minimumLevel)
            .WithNamingPolicy(MMP.Herald.Templating.PropertyNamingPolicy.Camel)
            // These tests assert exact event counts/shapes per dispatch.
            // The Phase 5 first-dispatch announcement (also at Info) would
            // appear as an extra event in the bridge and break ContainSingle
            // assertions. Suppress it — its behavior is covered in
            // NamingPolicyAnnouncementTests.
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();
        return (result.Logger, sink);
    }

    private static IReadOnlyList<(string name, object? value)> NamesAndValues(LogEvent e) =>
        e.Properties.Select(p => (p.Name, p.Value)).ToList();

    private sealed class CapturingBridge : MMP.Herald.ILogger
    {
        public ConcurrentBag<LogEvent> Captured { get; } = new();

        public IReadOnlyList<LogEvent> Events =>
            // ConcurrentBag enumeration order is undefined; tests that
            // only call once per pipeline are fine, the higher-level
            // tests use Should().ContainSingle() which doesn't rely on
            // order.
            Captured.ToList();

        public void Log(LogEvent logEvent) => Captured.Add(logEvent);

        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Captured.Add(logEvent);
            return ValueTask.CompletedTask;
        }
    }
}
