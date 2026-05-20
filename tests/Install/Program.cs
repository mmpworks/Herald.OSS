// Install smoke test for Herald.OSS (OSS-install-experience quick win #3).
//
// Exercises the user-facing install experience end-to-end:
//
//   1. Pack the current Herald.OSS.csproj to a temp feed directory.
//   2. Scaffold a `dotnet new console` project in a temp directory.
//   3. Pin a NuGet.config to that one local feed (clear nuget.org so a
//      released Herald.OSS cannot resolve in place of the local pack).
//   4. `dotnet add package Herald.OSS` — local pack wins because the
//      feed list is empty otherwise.
//   5. Write a minimal Program.cs that calls QuickLogBuilder and emits
//      one Info event.
//   6. `dotnet build` the temp project with warnings as errors — this
//      is what fires the interceptor generator.
//   7. (Optional) run the produced binary and assert the expected line
//      reached stdout.
//
// Each step has a numeric id. Exit code 0 = all assertions held; the
// failing step's id (1..7) otherwise; 99 for an unexpected exception.
//
// The temp directory is cleaned up on success; on failure it is left
// behind and its path printed so the operator can inspect logs.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MMP.Herald.OSS.SmokeTests.Install;

internal static class Program
{
    private static async Task<int> Main()
    {
        var workDir = Path.Combine(Path.GetTempPath(),
            "herald-oss-install-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(workDir);

        var feedDir = Path.Combine(workDir, "feed");
        var appDir = Path.Combine(workDir, "app");
        Directory.CreateDirectory(feedDir);
        Directory.CreateDirectory(appDir);

        var keepArtifacts = false;

        try
        {
            var repoRoot = FindRepoRoot();
            var csproj = Path.Combine(repoRoot, "Herald.OSS.csproj");

            await RunStep(1, "pack Herald.OSS to local feed",
                () => Pack(csproj, feedDir));

            await RunStep(2, "scaffold console project",
                () => NewConsole(appDir));

            RunStepSync(3, "write NuGet.config pinning local feed",
                () => WriteNugetConfig(appDir, feedDir));

            await RunStep(4, "dotnet add package Herald.OSS",
                () => AddPackage(appDir));

            RunStepSync(5, "write Program.cs that emits one event",
                () => WriteAppProgram(appDir));

            await RunStep(6, "dotnet build the consumer (warnings as errors)",
                () => BuildApp(appDir));

            await RunStep(7, "run the consumer and assert the log line lands",
                () => RunApp(appDir));

            Console.Out.WriteLine("Install smoke: OK");
            Directory.Delete(workDir, recursive: true);
            return 0;
        }
        catch (SmokeFailure sf)
        {
            keepArtifacts = true;
            Console.Error.WriteLine($"Install smoke FAILED at step {sf.Step}: {sf.Reason}");
            if (sf.StdOut is { Length: > 0 } so)
            {
                Console.Error.WriteLine("--- stdout ---");
                Console.Error.WriteLine(so);
            }
            if (sf.StdErr is { Length: > 0 } se)
            {
                Console.Error.WriteLine("--- stderr ---");
                Console.Error.WriteLine(se);
            }
            return sf.Step;
        }
        catch (Exception ex)
        {
            keepArtifacts = true;
            Console.Error.WriteLine($"Install smoke FAILED (unexpected): {ex.GetType().FullName}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 99;
        }
        finally
        {
            if (keepArtifacts)
            {
                Console.Error.WriteLine($"Artifacts left at: {workDir}");
            }
        }
    }

    // -- Steps ---------------------------------------------------------------

    private static async Task Pack(string csproj, string feedDir)
    {
        var (exit, stdout, stderr) = await Dotnet(
            workingDir: Path.GetDirectoryName(csproj)!,
            args: $"pack \"{csproj}\" -c Release -o \"{feedDir}\" --nologo");
        if (exit != 0)
        {
            throw new SmokeFailure(1, $"dotnet pack failed (exit {exit}).", stdout, stderr);
        }
        var produced = Directory.GetFiles(feedDir, "Herald.OSS.*.nupkg");
        if (produced.Length == 0)
        {
            throw new SmokeFailure(1, "dotnet pack produced no Herald.OSS.*.nupkg.", stdout, stderr);
        }
    }

    private static async Task NewConsole(string appDir)
    {
        var (exit, stdout, stderr) = await Dotnet(
            workingDir: appDir,
            args: "new console --name InstallSmokeApp --output . --force");
        if (exit != 0)
        {
            throw new SmokeFailure(2, $"dotnet new console failed (exit {exit}).", stdout, stderr);
        }
    }

    private static void WriteNugetConfig(string appDir, string feedDir)
    {
        // <clear /> drops every inherited source (incl. nuget.org) so the
        // local pack is the only Herald.OSS the restore can see. Without
        // this, a published Herald.OSS on nuget.org could resolve in place
        // of the in-tree pack and the smoke test would be lying.
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="herald-local" value="{feedDir}" />
              </packageSources>
            </configuration>
            """;
        File.WriteAllText(Path.Combine(appDir, "NuGet.config"), xml);
    }

    private static async Task AddPackage(string appDir)
    {
        var (exit, stdout, stderr) = await Dotnet(
            workingDir: appDir,
            args: "add package Herald.OSS");
        if (exit != 0)
        {
            throw new SmokeFailure(4, $"dotnet add package Herald.OSS failed (exit {exit}).", stdout, stderr);
        }
    }

    private static void WriteAppProgram(string appDir)
    {
        // Minimal consumer: build a pipeline through QuickLogBuilder, emit
        // one Info event with a templated property, and write a marker line
        // to stdout. Compile-time interceptor must fire cleanly (warnings as
        // errors will catch any HRLD-class analyzer regression).
        var program = """
            using MMP.Herald.Events;
            using MMP.Herald.Quick;

            var result = QuickLogBuilder.Create("install-smoke")
                .WithConsoleSink()
                .WithMinimumLevel("info")
                .BuildAndCommit();

            result.Logger.Info(LogCategory.App, "hello {Name}", "world");
            System.Console.Out.WriteLine("install-smoke: emitted");
            """;
        File.WriteAllText(Path.Combine(appDir, "Program.cs"), program);
    }

    private static async Task BuildApp(string appDir)
    {
        var (exit, stdout, stderr) = await Dotnet(
            workingDir: appDir,
            args: "build -c Release --nologo -warnaserror");
        if (exit != 0)
        {
            throw new SmokeFailure(6, $"dotnet build failed (exit {exit}).", stdout, stderr);
        }
    }

    private static async Task RunApp(string appDir)
    {
        var (exit, stdout, stderr) = await Dotnet(
            workingDir: appDir,
            args: "run -c Release --no-build --nologo");
        if (exit != 0)
        {
            throw new SmokeFailure(7, $"dotnet run failed (exit {exit}).", stdout, stderr);
        }
        // The marker line is what proves the binary actually executed end
        // to end (build succeeding alone would not). The console-sink log
        // line itself isn't asserted because its exact format depends on
        // the default renderer; the marker is renderer-agnostic.
        if (stdout.IndexOf("install-smoke: emitted", StringComparison.Ordinal) < 0)
        {
            throw new SmokeFailure(7, "consumer ran but the marker line 'install-smoke: emitted' was not in stdout.", stdout, stderr);
        }
    }

    // -- Plumbing ------------------------------------------------------------

    private static string FindRepoRoot()
    {
        // Walk up from the test binary's directory until we find a folder
        // that contains Herald.OSS.csproj. This is the same shape the AOT
        // and Interceptor smoke tests rely on (they use a ProjectReference,
        // which the build resolves; this test invokes dotnet pack at
        // runtime, so it has to find the csproj itself).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Herald.OSS.csproj")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new SmokeFailure(0,
            $"could not locate Herald.OSS.csproj walking up from {AppContext.BaseDirectory}.");
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> Dotnet(string workingDir, string args)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Process.Start returned null for dotnet.");

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var stdoutTask = ConsumeAsync(p.StandardOutput, stdoutBuilder);
        var stderrTask = ConsumeAsync(p.StandardError, stderrBuilder);

        await p.WaitForExitAsync();
        await stdoutTask;
        await stderrTask;

        return (p.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }

    private static async Task ConsumeAsync(TextReader reader, StringBuilder sink)
    {
        // ReadToEndAsync drains both streams concurrently so the child
        // process can't deadlock on a full pipe (large `dotnet build` /
        // `dotnet run` output is the usual culprit).
        var text = await reader.ReadToEndAsync();
        sink.Append(text);
    }

    private static async Task RunStep(int step, string name, Func<Task> body)
    {
        try { await body(); }
        catch (SmokeFailure) { throw; }
        catch (Exception ex)
        {
            throw new SmokeFailure(step, $"{name} threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void RunStepSync(int step, string name, Action body)
    {
        try { body(); }
        catch (SmokeFailure) { throw; }
        catch (Exception ex)
        {
            throw new SmokeFailure(step, $"{name} threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class SmokeFailure : Exception
    {
        public int Step { get; }
        public string Reason { get; }
        public string? StdOut { get; }
        public string? StdErr { get; }

        public SmokeFailure(int step, string reason, string? stdout = null, string? stderr = null)
            : base(reason)
        {
            Step = step;
            Reason = reason;
            StdOut = stdout;
            StdErr = stderr;
        }
    }
}
