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
/// Phase 4b: the <c>[HeraldLog]</c> source generator pre-resolves
/// property names against the project's <c>HeraldNamingPolicy</c> MSBuild
/// property at compile time. This test assembly leaves the property
/// unset, so the spec default (<c>"pascal"</c>) is what the generator
/// uses. Property names land PascalCased; no runtime cache hit.
/// </summary>
public static partial class HeraldLogGeneratorPolicyTests
{
    [MMP.Herald.Pipeline.HeraldLog(
        Level = "information",
        Category = "App",
        Message = "user {UserId} signed in")]
    public static partial void EmitUserSignIn(
        StructuredLogger logger, string userId);

    [MMP.Herald.Pipeline.HeraldLog(
        Level = "warning",
        Category = "App",
        Message = "order {OrderId} stage {Stage} retry {RetryNumber}")]
    public static partial void EmitOrderStage(
        StructuredLogger logger, string orderId, string stage, int retryNumber);

    // The [Fact]s live here, not on the outer static partial class, so the
    // collection association must sit on THIS class for xUnit to serialise it.
    // Serialised against every other NameResolverCache mutator/asserter via the
    // shared NameResolverCacheCollection. (The former private single-class
    // collection was attached to the outer static class, which carries no facts
    // — so it never actually serialised these tests; they raced the cache.
    // Cache-serialization is the only concern that guard was meant to carry.)
    [Collection(MMP.Herald.OSS.Tests.Helpers.NameResolverCacheCollection.Name)]
    public sealed class TheTests
    {
        public TheTests()
        {
            NameResolverCache.Reset();
        }

        [Fact]
        public void HeraldLog_method_emits_PascalCase_property_names()
        {
            var sink = new CapturingBridge();
            var result = QuickLogBuilder.Create()
                .WithBridge(sink)
                .WithMinimumLevel("trace")
                .SuppressNamingPolicyAnnouncement()
                .BuildAndCommit();

            EmitUserSignIn(result.Logger, userId: "alice");

            var ev = sink.Events.Should().ContainSingle().Subject;
            ev.Properties.Should().ContainSingle()
                .Which.Name.Should().Be("UserId");
            ev.Properties[0].Value.Should().Be("alice");
        }

        [Fact]
        public void HeraldLog_method_emits_PascalCase_for_multi_arg_template()
        {
            var sink = new CapturingBridge();
            var result = QuickLogBuilder.Create()
                .WithBridge(sink)
                .WithMinimumLevel("trace")
                .SuppressNamingPolicyAnnouncement()
                .BuildAndCommit();

            EmitOrderStage(result.Logger, orderId: "ord-7", stage: "captured", retryNumber: 2);

            var ev = sink.Events.Should().ContainSingle().Subject;
            ev.Properties.Select(p => p.Name).Should().Equal("OrderId", "Stage", "RetryNumber");
            ev.Properties.Select(p => p.Value).Should().Equal("ord-7", "captured", 2);
        }

        [Fact]
        public void HeraldLog_dispatch_increments_compile_time_resolutions()
        {
            var sink = new CapturingBridge();
            var result = QuickLogBuilder.Create()
                .WithBridge(sink)
                .WithMinimumLevel("trace")
                .SuppressNamingPolicyAnnouncement()
                .BuildAndCommit();

            EmitUserSignIn(result.Logger, "alice");
            EmitUserSignIn(result.Logger, "bob");
            EmitOrderStage(result.Logger, "ord-1", "captured", 0);

            var diag = result.Logger.GetNamingPolicyDiagnostics();
            diag.CompileTimeResolutions.Should().Be(3,
                "every [HeraldLog] dispatch bumps the compile-time counter");
            diag.ResolutionCount.Should().Be(0,
                "the source-gen path never touches the runtime resolver");
            diag.CacheHits.Should().Be(0);
            diag.CacheMisses.Should().Be(0);
        }

        private sealed class CapturingBridge : MMP.Herald.ILogger
        {
            public ConcurrentBag<LogEvent> Captured { get; } = new();
            public IReadOnlyList<LogEvent> Events => Captured.ToList();
            public void Log(LogEvent logEvent) => Captured.Add(logEvent);
            public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
            {
                Captured.Add(logEvent);
                return ValueTask.CompletedTask;
            }
        }
    }
}
