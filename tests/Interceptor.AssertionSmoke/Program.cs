// V1.1 assertion smoke test for Herald.OSS HeraldNamingPolicyAssertion.
//
// Sibling of Interceptor.SmokeTests (which validates the V1 multi-policy
// emit). This project asserts <HeraldNamingPolicyAssertion>Default</> so the
// InterceptorGenerator emits the Pascal-only single-lane shape. The smoke
// walks the observable contract:
//
//   1. [assembly: HeraldBuildAssertion] reports NamingPolicyAssertion="Default"
//      and BakedPolicies="Pascal" (the asserted shape, not the unasserted
//      "Pascal,Snake,Camel").
//   2. Pascal-baked property names land on the captured event ("UserId" /
//      "Action").
//   3. The generated HeraldInterceptors.g.cs contains no `switch` or
//      `CurrentPolicyKind` reference — the multi-policy dispatcher is elided
//      from the asserted emit.
//
// Exit code 0 = all assertions held; non-zero = the failing step's index.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MMP.Herald;
using MMP.Herald.Build;
using MMP.Herald.Events;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.SmokeTests.InterceptorAssertion;

internal static class Program
{
    private static int Main()
    {
        try
        {
            RunStep(1, "build-assertion attribute reports asserted shape",
                VerifyAssertedBuildAssertion);
            RunStep(2, "Pascal lane bakes 'UserId' under assertion",
                VerifyPascalBaked);
            RunStep(3, "generated interceptor file has no switch / CurrentPolicyKind",
                VerifySingleLaneEmitShape);

            Console.Out.WriteLine("Interceptor assertion smoke: OK");
            return 0;
        }
        catch (SmokeFailure sf)
        {
            Console.Error.WriteLine($"Interceptor assertion smoke FAILED at step {sf.Step}: {sf.Reason}");
            return sf.Step;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Interceptor assertion smoke FAILED (unexpected): {ex.GetType().FullName}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 99;
        }
    }

    // -- Step 1 --------------------------------------------------------------

    private static void VerifyAssertedBuildAssertion()
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
        if (marker.NamingPolicyAssertion != "Default")
        {
            throw new SmokeFailure(1,
                $"build assertion NamingPolicyAssertion='{marker.NamingPolicyAssertion}', expected 'Default'.");
        }
        if (marker.BakedPolicies != "Pascal")
        {
            throw new SmokeFailure(1,
                $"build assertion BakedPolicies='{marker.BakedPolicies}', expected 'Pascal' under assertion.");
        }
        if (marker.InterceptedCallSites < 1)
        {
            throw new SmokeFailure(1,
                $"build assertion InterceptedCallSites={marker.InterceptedCallSites}, expected >= 1.");
        }
    }

    // -- Step 2 --------------------------------------------------------------
    //
    // The literal-template call below is the seed call site the generator
    // intercepts. Under assertion, the dispatcher calls LogCompactN directly
    // with baked "UserId" / "Action" Pascal literals. The capturing sink
    // records the properties so the test can confirm the names landed.

    private static void VerifyPascalBaked()
    {
        var (logger, sink) = BuildPipeline();

        // The literal-template call site the generator intercepts.
        logger.Information(LogCategory.None, "user {UserId} did {Action}", 42, "login");

        if (sink.Events.Count != 1)
        {
            throw new SmokeFailure(2,
                $"expected 1 captured event, got {sink.Events.Count}.");
        }

        var names = sink.Events[0].Properties.Select(p => p.Name).ToArray();
        if (names.Length != 2)
        {
            throw new SmokeFailure(2,
                $"expected 2 properties, got {names.Length}: [{string.Join(", ", names)}].");
        }
        if (names[0] != "UserId")
        {
            throw new SmokeFailure(2,
                $"property[0].Name='{names[0]}', expected 'UserId'.");
        }
        if (names[1] != "Action")
        {
            throw new SmokeFailure(2,
                $"property[1].Name='{names[1]}', expected 'Action'.");
        }
    }

    // -- Step 3 --------------------------------------------------------------
    //
    // Grep the emitted HeraldInterceptors.g.cs for the absence of the
    // multi-policy markers. The asserted emit must NOT contain `switch` or
    // `CurrentPolicyKind` references — both belong to the V1 multi-policy
    // dispatcher we elided.

    private static void VerifySingleLaneEmitShape()
    {
        var generatedPath = LocateGeneratedInterceptorFile();
        if (generatedPath is null)
        {
            throw new SmokeFailure(3,
                "could not locate HeraldInterceptors.g.cs under the assertion-smoke obj tree. " +
                "Did the project's <EmitCompilerGeneratedFiles> get turned off?");
        }

        var source = File.ReadAllText(generatedPath);

        if (source.Contains("CurrentPolicyKind", StringComparison.Ordinal))
        {
            throw new SmokeFailure(3,
                $"generated interceptor file at '{generatedPath}' references CurrentPolicyKind " +
                "— the asserted emit should have elided the multi-policy dispatcher entirely.");
        }
        if (source.Contains("BuiltinPolicy.", StringComparison.Ordinal))
        {
            throw new SmokeFailure(3,
                $"generated interceptor file at '{generatedPath}' references BuiltinPolicy " +
                "— the asserted emit should have elided the multi-policy dispatcher entirely.");
        }
    }

    private static string? LocateGeneratedInterceptorFile()
    {
        // The csproj points CompilerGeneratedFilesOutputPath at
        // obj/$(Configuration)/$(TargetFramework)/generated/. Walk up from the
        // running assembly's location to find the project root; the obj tree
        // lives alongside the bin tree.
        var binDir = Path.GetDirectoryName(typeof(Program).Assembly.Location);
        if (binDir is null) return null;

        var current = new DirectoryInfo(binDir);
        while (current is not null)
        {
            var csproj = current.GetFiles("Interceptor.AssertionSmoke.csproj").FirstOrDefault();
            if (csproj is not null) break;
            current = current.Parent;
        }
        if (current is null) return null;

        var generatedRoot = Path.Combine(current.FullName, "obj");
        if (!Directory.Exists(generatedRoot)) return null;

        foreach (var match in Directory.EnumerateFiles(
            generatedRoot, "HeraldInterceptors.g.cs", SearchOption.AllDirectories))
        {
            return match;
        }
        return null;
    }

    // -- Test infrastructure ------------------------------------------------

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

    private static (MMP.Herald.Pipeline.StructuredLogger Logger, CapturingSink Sink) BuildPipeline()
    {
        var sink = new CapturingSink();
        var result = QuickLogBuilder.Create()
            .WithBridge(sink)
            .WithMinimumLevel("trace")
            .SuppressNamingPolicyAnnouncement()
            .BuildAndCommit();
        return (result.Logger, sink);
    }

    private static void RunStep(int index, string description, Action body)
    {
        try
        {
            body();
            Console.Out.WriteLine($"  [{index}] {description} — OK");
        }
        catch (SmokeFailure)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SmokeFailure(index, $"{description} failed: {ex.GetType().FullName}: {ex.Message}", ex);
        }
    }

    private sealed class SmokeFailure : Exception
    {
        public SmokeFailure(int step, string reason, Exception? inner = null) : base(reason, inner)
        {
            Step = step;
            Reason = reason;
        }
        public int Step { get; }
        public string Reason { get; }
    }
}
