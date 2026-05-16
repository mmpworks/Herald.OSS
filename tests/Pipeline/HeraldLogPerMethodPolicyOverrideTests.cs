#nullable enable

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline;

/// <summary>
/// Phase 4c: <c>[HeraldLog(NamingPolicy = "...")]</c> per-method override.
/// The project default in this test assembly is Pascal (no
/// <c>HeraldNamingPolicy</c> MSBuild property is set). Methods with no
/// override emit PascalCase property names; methods with an explicit
/// override emit the named policy's shape instead — without disturbing
/// the project default for any sibling method.
/// </summary>
[Collection(nameof(HeraldLogPerMethodPolicyOverrideTests))]
[CollectionDefinition(nameof(HeraldLogPerMethodPolicyOverrideTests), DisableParallelization = true)]
public static partial class HeraldLogPerMethodPolicyOverrideTests
{
    // Inherits project default (Pascal). Template token says {UserId};
    // emitted property name should be exactly "UserId".
    [HeraldLog(
        Level = "info",
        Category = "App",
        Message = "user {UserId} signed in")]
    public static partial void EmitPascalDefault(
        StructuredLogger logger, string userId);

    // Explicit snake override. Template token {UserId} resolves to
    // "user_id" regardless of the project's Pascal default.
    [HeraldLog(
        Level = "info",
        Category = "App",
        Message = "user {UserId} signed in",
        NamingPolicy = "snake")]
    public static partial void EmitSnakeOverride(
        StructuredLogger logger, string userId);

    // Explicit camel override. Camel's contract is "caller-supplied
    // name wins"; with a template token present the source-gen path
    // emits the token verbatim ({UserId} → "UserId" stays "UserId"
    // because the source is the token, not the parameter name).
    // With NO template token, the parameter name "userId" would land
    // verbatim — covered by the no-template test below.
    [HeraldLog(
        Level = "info",
        Category = "App",
        Message = "user {UserId} signed in",
        NamingPolicy = "camel")]
    public static partial void EmitCamelOverride(
        StructuredLogger logger, string userId);

    // No template tokens — camel override falls back to the C#
    // parameter name "userId" and emits it as-is. The Pascal default
    // would have uppercased the first letter; camel preserves it.
    [HeraldLog(
        Level = "info",
        Category = "App",
        Message = "user signed in",
        NamingPolicy = "camel")]
    public static partial void EmitCamelOverrideNoTemplate(
        StructuredLogger logger, string userId);

    // Mixed arity (3 props) under snake override — confirms the override
    // applies through the compact-buffer path, not just arity-1.
    [HeraldLog(
        Level = "warn",
        Category = "App",
        Message = "order {OrderId} stage {Stage} retry {RetryNumber}",
        NamingPolicy = "snake")]
    public static partial void EmitOrderStageSnake(
        StructuredLogger logger, string orderId, string stage, int retryNumber);

    // 12-arg method under snake override — confirms the override
    // applies through the legacy LogProperty[] path that handles
    // arities above 8.
    [HeraldLog(
        Level = "info",
        Category = "App",
        Message = "wide {A} {B} {C} {D} {E} {F} {G} {H} {I} {J} {K} {L}",
        NamingPolicy = "snake")]
    public static partial void EmitWideSnake(
        StructuredLogger logger,
        string a, string b, string c, string d, string e, string f,
        string g, string h, string i, string j, string k, string l);

    public sealed class TheTests
    {
        public TheTests()
        {
            NameResolverCache.Reset();
        }

        [Fact]
        public void Method_without_override_uses_project_default_Pascal()
        {
            var sink = new CapturingBridge();
            var result = QuickLogBuilder.Create()
                .WithBridge(sink)
                .WithMinimumLevel("trace")
                .SuppressNamingPolicyAnnouncement()
                .BuildAndCommit();

            EmitPascalDefault(result.Logger, "alice");

            var ev = sink.Events.Should().ContainSingle().Subject;
            ev.Properties.Should().ContainSingle()
                .Which.Name.Should().Be("UserId");
        }

        [Fact]
        public void Snake_override_resolves_template_token_through_snake_policy()
        {
            var sink = new CapturingBridge();
            var result = QuickLogBuilder.Create()
                .WithBridge(sink)
                .WithMinimumLevel("trace")
                .SuppressNamingPolicyAnnouncement()
                .BuildAndCommit();

            EmitSnakeOverride(result.Logger, "alice");

            var ev = sink.Events.Should().ContainSingle().Subject;
            ev.Properties.Should().ContainSingle()
                .Which.Name.Should().Be("user_id");
        }

        [Fact]
        public void Camel_override_emits_template_token_verbatim()
        {
            var sink = new CapturingBridge();
            var result = QuickLogBuilder.Create()
                .WithBridge(sink)
                .WithMinimumLevel("trace")
                .SuppressNamingPolicyAnnouncement()
                .BuildAndCommit();

            EmitCamelOverride(result.Logger, "alice");

            var ev = sink.Events.Should().ContainSingle().Subject;
            // Source = template token "UserId"; Camel = no casing transform.
            // So "UserId" stays "UserId" even though the project default
            // would also have produced "UserId" — covered for completeness.
            ev.Properties.Should().ContainSingle()
                .Which.Name.Should().Be("UserId");
        }

        [Fact]
        public void Camel_override_falls_back_to_parameter_name_when_no_template_token()
        {
            // The discriminating case for Camel: with no template token,
            // the source is the C# parameter name "userId" — Camel emits
            // it verbatim, NOT PascalCased.
            var sink = new CapturingBridge();
            var result = QuickLogBuilder.Create()
                .WithBridge(sink)
                .WithMinimumLevel("trace")
                .SuppressNamingPolicyAnnouncement()
                .BuildAndCommit();

            EmitCamelOverrideNoTemplate(result.Logger, "alice");

            var ev = sink.Events.Should().ContainSingle().Subject;
            ev.Properties.Should().ContainSingle()
                .Which.Name.Should().Be("userId");
        }

        [Fact]
        public void Snake_override_applies_through_the_compact_buffer_path_for_mixed_arity()
        {
            var sink = new CapturingBridge();
            var result = QuickLogBuilder.Create()
                .WithBridge(sink)
                .WithMinimumLevel("trace")
                .SuppressNamingPolicyAnnouncement()
                .BuildAndCommit();

            EmitOrderStageSnake(result.Logger, "ord-7", "captured", 2);

            var ev = sink.Events.Should().ContainSingle().Subject;
            ev.Properties.Select(p => p.Name)
                .Should().Equal("order_id", "stage", "retry_number");
            ev.Properties.Select(p => p.Value)
                .Should().Equal("ord-7", "captured", 2);
        }

        [Fact]
        public void Snake_override_applies_through_the_legacy_array_path_for_arity_above_eight()
        {
            var sink = new CapturingBridge();
            var result = QuickLogBuilder.Create()
                .WithBridge(sink)
                .WithMinimumLevel("trace")
                .SuppressNamingPolicyAnnouncement()
                .BuildAndCommit();

            EmitWideSnake(
                result.Logger,
                "1", "2", "3", "4", "5", "6",
                "7", "8", "9", "10", "11", "12");

            var ev = sink.Events.Should().ContainSingle().Subject;
            // Source tokens are single-letter, so snake casing is a no-op
            // on them; the assertion proves the per-method override is
            // wired through the >8-arg path, not that snake transforms
            // single letters. The "_" gap test happens in the mixed-arity
            // case above.
            ev.Properties.Select(p => p.Name).Should().Equal(
                "a", "b", "c", "d", "e", "f",
                "g", "h", "i", "j", "k", "l");
        }

        [Fact]
        public void Override_on_one_method_does_not_leak_into_sibling_method_using_default()
        {
            // The point: in one logger, fire the snake-override method
            // followed by the default-Pascal method. The default method
            // must still see "UserId", not "user_id". Confirms the
            // generator emits per-method baked names rather than
            // touching a shared runtime cache that could persist a
            // policy choice across methods.
            var sink = new CapturingBridge();
            var result = QuickLogBuilder.Create()
                .WithBridge(sink)
                .WithMinimumLevel("trace")
                .SuppressNamingPolicyAnnouncement()
                .BuildAndCommit();

            EmitSnakeOverride(result.Logger, "alice");
            EmitPascalDefault(result.Logger, "bob");

            sink.Events.Should().HaveCount(2);
            sink.Events[0].Properties[0].Name.Should().Be("user_id");
            sink.Events[1].Properties[0].Name.Should().Be("UserId");
        }

        [Fact]
        public void Override_path_bumps_compile_time_resolutions_just_like_default_path()
        {
            var sink = new CapturingBridge();
            var result = QuickLogBuilder.Create()
                .WithBridge(sink)
                .WithMinimumLevel("trace")
                .SuppressNamingPolicyAnnouncement()
                .BuildAndCommit();

            EmitSnakeOverride(result.Logger, "alice");
            EmitPascalDefault(result.Logger, "bob");
            EmitCamelOverrideNoTemplate(result.Logger, "carol");

            var diag = result.Logger.GetNamingPolicyDiagnostics();
            diag.CompileTimeResolutions.Should().Be(3,
                "every source-gen dispatch bumps the counter, regardless of policy");
            diag.ResolutionCount.Should().Be(0,
                "the source-gen path never touches the runtime resolver");
            diag.CacheHits.Should().Be(0);
            diag.CacheMisses.Should().Be(0);
        }

        private sealed class CapturingBridge : MMP.Herald.ILogger
        {
            // Sequenced single-threaded list so insertion order is stable
            // for assertions; tests in this collection run non-parallel.
            private readonly List<LogEvent> _events = new();
            private readonly object _sync = new();

            public IReadOnlyList<LogEvent> Events
            {
                get { lock (_sync) { return _events.ToArray(); } }
            }

            public void Log(LogEvent logEvent)
            {
                lock (_sync) { _events.Add(logEvent); }
            }

            public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
            {
                Log(logEvent);
                return ValueTask.CompletedTask;
            }
        }
    }
}
