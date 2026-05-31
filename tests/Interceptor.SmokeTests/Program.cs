// Interceptor smoke test for Herald.OSS V1 multi-policy generator (P3).
//
// Walks through every observable contract the InterceptorGenerator promises:
//
//   1. The generator emits an interceptor per literal-template call site, so
//      the runtime cache stays cold even after many dispatches.
//   2. Each interceptor carries Pascal / Snake / Camel baked lanes; the
//      property names that land on the captured event depend on the active
//      StructuredLogger.CurrentPolicyKind.
//   3. A custom IPropertyNamingPolicy falls through the interceptor's default
//      lane to the runtime resolver — the consumer-supplied policy still wins
//      for the names it cares about.
//   4. The assembly-level HeraldBuildAssertionAttribute marker is emitted and
//      reports the expected baked-policy set + interceptor count.
//
// Exit code 0 = all assertions held; non-zero = the failing step's index.

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald;
using MMP.Herald.Build;
using MMP.Herald.Events;
using MMP.Herald.Quick;
using MMP.Herald.Templating;
using MMP.Herald.Templating.NamingPolicies;

namespace MMP.Herald.OSS.SmokeTests.Interceptor;

internal static class Program
{
    private static int Main()
    {
        try
        {
            RunStep(1, "build-assertion attribute present", VerifyBuildAssertion);
            RunStep(2, "default Pascal policy bakes 'UserId'", VerifyDefaultPascal);
            RunStep(3, "SnakeCasePolicy lane bakes 'user_id'", VerifySnake);
            RunStep(4, "CamelCasePolicy lane bakes 'userId'", VerifyCamel);
            RunStep(5, "custom policy falls through interceptor default lane", VerifyCustomFallthrough);
            RunStep(6, "interceptors bypass diagnostics counters entirely",
                VerifyDiagnosticsCounters);

            Console.Out.WriteLine("Interceptor smoke: OK");
            return 0;
        }
        catch (SmokeFailure sf)
        {
            Console.Error.WriteLine($"Interceptor smoke FAILED at step {sf.Step}: {sf.Reason}");
            return sf.Step;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Interceptor smoke FAILED (unexpected): {ex.GetType().FullName}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 99;
        }
    }

    // -- Step 1 --------------------------------------------------------------

    private static void VerifyBuildAssertion()
    {
        var marker = typeof(Program).Assembly.GetCustomAttribute<HeraldBuildAssertionAttribute>();
        if (marker is null)
        {
            throw new SmokeFailure(1,
                "no [assembly: HeraldBuildAssertion] attribute found — the InterceptorGenerator " +
                "did not emit the marker (or InterceptorsNamespaces is wired wrong).");
        }
        if (!marker.InterceptorsEnabled)
        {
            throw new SmokeFailure(1, "build assertion reports InterceptorsEnabled=false.");
        }
        if (marker.BakedPolicies != "Pascal,Snake,Camel")
        {
            throw new SmokeFailure(1,
                $"build assertion BakedPolicies='{marker.BakedPolicies}', expected 'Pascal,Snake,Camel'.");
        }
        if (marker.InterceptedCallSites <= 0)
        {
            throw new SmokeFailure(1,
                $"build assertion InterceptedCallSites={marker.InterceptedCallSites}, expected > 0.");
        }
    }

    // -- Step 2 --------------------------------------------------------------

    private static void VerifyDefaultPascal()
    {
        var (logger, sink) = BuildPipeline();   // no .WithNamingPolicy → default Pascal
        var userId = "alice";
        var action = "signin";

        logger.Information(LogCategory.App, "user {UserId} did {Action}", userId, action);

        AssertNames(2, sink, expected: new[] { "UserId", "Action" });
    }

    // -- Step 3 --------------------------------------------------------------

    private static void VerifySnake()
    {
        var (logger, sink) = BuildPipeline(policy: SnakeCasePolicy.Instance);
        var userId = "alice";
        var action = "signin";

        logger.Information(LogCategory.App, "user {UserId} did {Action}", userId, action);

        AssertNames(3, sink, expected: new[] { "user_id", "action" });
    }

    // -- Step 4 --------------------------------------------------------------

    private static void VerifyCamel()
    {
        var (logger, sink) = BuildPipeline(policy: CamelCasePolicy.Instance);
        var userId = "alice";
        var action = "signin";

        logger.Information(LogCategory.App, "user {UserId} did {Action}", userId, action);

        AssertNames(4, sink, expected: new[] { "userId", "action" });
    }

    // -- Step 5 --------------------------------------------------------------

    private static void VerifyCustomFallthrough()
    {
        var (logger, sink) = BuildPipeline(policy: new UpperCasePolicy());
        var userId = "alice";
        var action = "signin";

        // The interceptor's switch sees BuiltinPolicy.Custom and forwards to
        // the runtime resolver path, which runs UpperCasePolicy.ResolveAll.
        // Property names: USERID, ACTION.
        logger.Information(LogCategory.App, "user {UserId} did {Action}", userId, action);

        AssertNames(5, sink, expected: new[] { "USERID", "ACTION" });
    }

    // -- Step 6 --------------------------------------------------------------

    private static void VerifyDiagnosticsCounters()
    {
        var (logger, _) = BuildPipeline();
        var v = 42;

        for (var i = 0; i < 3; i++)
        {
            logger.Information(LogCategory.App, "smoke {V}", v);
        }

        var diag = logger.GetNamingPolicyDiagnostics();
        // Multi-policy interceptors no longer bump CompileTimeResolutions —
        // the per-policy lane split (V1 perf-tightening pass) dropped the
        // RecordInterceptedDispatch hook so the JIT can inline the dispatcher
        // body end-to-end. The intercepted hot path now bypasses every
        // diagnostics counter, so the consumer's only signal that dispatch
        // is on the interceptor path is the HeraldBuildAssertionAttribute
        // marker checked in Step 1.
        if (diag.CompileTimeResolutions != 0)
        {
            throw new SmokeFailure(6,
                $"diag.CompileTimeResolutions={diag.CompileTimeResolutions}, expected 0 " +
                "(interceptors do not bump this counter after the V1 perf split).");
        }
        if (diag.CacheHits != 0 || diag.CacheMisses != 0)
        {
            throw new SmokeFailure(6,
                $"diag.CacheHits={diag.CacheHits}, CacheMisses={diag.CacheMisses}; " +
                "interceptors should bypass the runtime cache entirely.");
        }
    }

    // -- Plumbing ------------------------------------------------------------

    private sealed class CapturingSink : MMP.Herald.ILogger
    {
        private readonly System.Collections.Generic.List<LogEvent> _events = new();
        private readonly object _sync = new();

        public System.Collections.Generic.IReadOnlyList<LogEvent> Events
        {
            get { lock (_sync) { return _events.ToArray(); } }
        }

        public void Log(LogEvent logEvent) { lock (_sync) { _events.Add(logEvent); } }
        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Log(logEvent);
            return ValueTask.CompletedTask;
        }
    }

    private static (MMP.Herald.Pipeline.StructuredLogger Logger, CapturingSink Sink) BuildPipeline(
        IPropertyNamingPolicy? policy = null)
    {
        var sink = new CapturingSink();
        var builder = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .SuppressNamingPolicyAnnouncement();
        if (policy is not null)
        {
            builder = builder.WithNamingPolicy(policy);
        }
        var result = builder.BuildAndCommit();
        return (result.Logger, sink);
    }

    private static void AssertNames(int step, CapturingSink sink, string[] expected)
    {
        if (sink.Events.Count != 1)
        {
            throw new SmokeFailure(step,
                $"expected 1 event, got {sink.Events.Count}.");
        }
        var actual = sink.Events[0].Properties.Select(p => p.Name).ToArray();
        if (actual.Length != expected.Length)
        {
            throw new SmokeFailure(step,
                $"expected {expected.Length} properties, got {actual.Length}.");
        }
        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(actual[i], expected[i], StringComparison.Ordinal))
            {
                throw new SmokeFailure(step,
                    $"property[{i}].Name='{actual[i]}', expected '{expected[i]}'.");
            }
        }
    }

    private static void RunStep(int step, string name, Action body)
    {
        try { body(); }
        catch (SmokeFailure) { throw; }
        catch (Exception ex)
        {
            throw new SmokeFailure(step, $"{name} threw {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    // Tiny custom policy that uppercases the source name. Used to verify the
    // interceptor's BuiltinPolicy.Custom default lane routes through to the
    // runtime resolver instead of falling through to one of the baked lanes.
    private sealed class UpperCasePolicy : IPropertyNamingPolicy
    {
        public string Id => "upper";
        public string[] ResolveAll(
            ReadOnlySpan<MessageTemplateToken.Property> tokens,
            ReadOnlySpan<string> argExprs)
        {
            var result = new string[argExprs.Length];
            for (var i = 0; i < argExprs.Length; i++)
            {
                var source = i < tokens.Length && !string.IsNullOrEmpty(tokens[i].Name)
                    ? tokens[i].Name
                    : argExprs[i];
                result[i] = (string.IsNullOrEmpty(source) ? "arg" + (i + 1) : source).ToUpperInvariant();
            }
            return result;
        }
    }

    private sealed class SmokeFailure : Exception
    {
        public int Step { get; }
        public string Reason { get; }
        public SmokeFailure(int step, string reason, Exception? inner = null)
            : base(reason, inner) { Step = step; Reason = reason; }
    }
}
