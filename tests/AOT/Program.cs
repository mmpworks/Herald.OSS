// AOT smoke test for Herald.OSS (principal-review queue #18).
//
// Builds a minimal pipeline through QuickLogBuilder, commits it, and
// emits one Info event. Any path Herald.OSS takes that the AOT analyzer
// missed (runtime reflection, dynamic-code emit, JSON serialization
// without a source-generated context) surfaces here as a
// PlatformNotSupportedException or NotSupportedException at runtime,
// turning into a non-zero exit code. CI fails on either condition.
//
// Kept deliberately minimal:
//   - No HotReload (the file-watcher path is not in the AOT critical path).
//   - No JSON reload (the slow rebuild path exercises plugin-supplied
//     kinds the OSS smoke can't predict).
//   - No external sinks (console-only; the sink kind is registered via
//     the source-generated module initializer the main csproj ships).
//
// Future regressions in the AOT posture surface here without a developer
// having to remember to run `dotnet publish -p:PublishAot=true` by hand.

using System;
using MMP.Herald.Events;
using MMP.Herald.Quick;

try
{
    var result = QuickLogBuilder.Create("aot-smoke")
        .WithConsoleSink()
        .WithMinimumLevel("info")
        .BuildAndCommit();

    if (result.Logger is null)
    {
        Console.Error.WriteLine("AOT smoke: BuildAndCommit returned a null logger.");
        return 2;
    }

    result.Logger.Information(LogCategory.App, "AOT smoke: pipeline build + emit succeeded.");
    Console.Out.WriteLine("AOT smoke: OK");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"AOT smoke FAILED: {ex.GetType().FullName}: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}
