#nullable enable
#if NET9_0_OR_GREATER
using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// G-LAYER2.1 — Layer-2 / real-Serilog coexistence fails at COMPILE with CS0433.
///
/// <para>
/// The migration-runbook precondition: Layer 2 must be the ONLY <c>Serilog</c> assembly
/// in the dependency graph. If a consumer references both the Layer-2 mirror
/// (<c>AssemblyName=Serilog</c>) AND the real Serilog NuGet package, the compiler emits
/// CS0433 ("The type X exists in both Serilog, ... and Serilog, ...") and refuses to build.
/// </para>
///
/// <para>
/// This test proves the failure is a COMPILE error — not a silent runtime
/// <c>InvalidCastException</c> that slips through a successful build. The gate is structural:
/// Roslyn sees two assemblies named "Serilog" declaring the same types and stops.
/// </para>
///
/// <para>
/// The conflict project (<c>tests/SerilogCompat/Layer2/Coexistence/CoexistenceConflict.csproj</c>)
/// is intentionally broken. This test shells out a <c>dotnet build</c> of that project and
/// asserts the build FAILS with CS0433 in the output.
/// </para>
///
/// <para>
/// <b>Dependency:</b> The conflict project uses a direct <c>&lt;Reference HintPath=...&gt;</c>
/// pointing to the real Serilog 4.3.1 DLL in the NuGet global-packages cache
/// (<c>%USERPROFILE%\.nuget\packages\serilog\4.3.1\lib\net9.0\Serilog.dll</c>).
/// The Serilog NuGet package must be restored in the global cache before running this test.
/// On a clean CI agent, run <c>dotnet restore tests/Herald.OSS.Tests.csproj</c> first —
/// the main test project already declares <c>PackageReference Include="Serilog" Version="4.3.1"</c>
/// so the restore will populate the cache.
/// </para>
///
/// <para>Gated to net9.0+: Layer 2 targets net9/net10 only.</para>
/// </summary>
public sealed class G_Layer2_CoexistenceTests
{
    /// <summary>
    /// Proves that referencing both the Layer-2 Serilog mirror and the real Serilog 4.3.1
    /// DLL in the same compilation fails at compile time with CS0433 (duplicate types),
    /// NOT at runtime with an <c>InvalidCastException</c>.
    /// </summary>
    [Fact]
    public void Layer2_and_realSerilog_coexistence_fails_at_compile_with_CS0433()
    {
        var conflictProject = FindCoexistenceConflictProject();

        // Pre-condition: the real Serilog DLL must be in the NuGet cache; without it the
        // <Reference Condition="Exists(...)"> in the conflict project is silently dropped
        // and the build succeeds (defeating the gate). Surface a clear error here.
        var realSerilogDll = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages", "serilog", "4.3.1", "lib", "net9.0", "Serilog.dll");

        if (!File.Exists(realSerilogDll))
        {
            throw new InvalidOperationException(
                $"G-LAYER2.1 pre-condition: real Serilog 4.3.1 DLL not found at '{realSerilogDll}'. " +
                "Run 'dotnet restore tests/Herald.OSS.Tests.csproj' to populate the NuGet cache, " +
                "then re-run this test.");
        }

        var result = RunDotnetBuild(conflictProject);

        // The build MUST fail — a zero exit code means both assemblies were deduped to one
        // and the structural gate is broken.
        Assert.True(
            result.ExitCode != 0,
            $"G-LAYER2.1 FAILED: the conflict project built successfully (exit code 0). " +
            $"Expected CS0433. Output:\n{result.Output}");

        // The failure MUST be CS0433 — the duplicate-assembly-identity compile error that
        // proves the collision is caught at compile, not runtime.
        Assert.True(
            result.Output.Contains("CS0433"),
            $"G-LAYER2.1 FAILED: build failed (exit code {result.ExitCode}) but CS0433 was NOT " +
            $"in the output. A different error fired — investigate.\nOutput:\n{result.Output}");

        // "Serilog" must appear in the error message (names the conflicting assembly).
        Assert.Contains("Serilog", result.Output);

        // Negative: this is not a runtime check. The test passing means the COMPILER
        // stopped the invalid graph — not that an InvalidCastException would fire at runtime.
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the absolute path to <c>CoexistenceConflict.csproj</c> relative to this
    /// test assembly's location. Walking up from the test binary's directory is resilient
    /// to Debug/Release and TFM subdirectory depth.
    /// </summary>
    private static string FindCoexistenceConflictProject()
    {
        // Walk up from the running test assembly until we find the repo root
        // (identified by Herald.OSS.csproj) then descend to the conflict project.
        // Cognitive complexity note: straightforward directory walk with a clear guard.
        var current = new DirectoryInfo(
            Path.GetDirectoryName(typeof(G_Layer2_CoexistenceTests).Assembly.Location)
            ?? throw new InvalidOperationException("Cannot determine test assembly location."));

        const int maxDepth = 10;
        for (int depth = 0; depth < maxDepth; depth++)
        {
            if (current.Parent is null) break;

            if (File.Exists(Path.Combine(current.FullName, "Herald.OSS.csproj")))
            {
                var conflictPath = Path.Combine(
                    current.FullName,
                    "tests", "SerilogCompat", "Layer2", "Coexistence",
                    "CoexistenceConflict.csproj");

                if (File.Exists(conflictPath)) return conflictPath;

                throw new InvalidOperationException(
                    $"Found Herald.OSS.csproj at '{current.FullName}' but CoexistenceConflict.csproj " +
                    $"was not found at the expected path '{conflictPath}'. " +
                    "Verify that tests/SerilogCompat/Layer2/Coexistence/ was committed.");
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate Herald.OSS.csproj by walking up from the test assembly directory. " +
            $"Started at: {Path.GetDirectoryName(typeof(G_Layer2_CoexistenceTests).Assembly.Location)}");
    }

    /// <summary>
    /// Runs <c>dotnet build</c> on the given project file and returns the combined
    /// stdout+stderr output together with the process exit code.
    /// </summary>
    private static (int ExitCode, string Output) RunDotnetBuild(string projectPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            // -c Debug: fastest path. -v minimal: enough output to see CS0433 without noise.
            Arguments = $"build \"{projectPath}\" -c Debug -v minimal",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start 'dotnet build' process.");

        // Read both streams before WaitForExit to avoid deadlock on full output buffers.
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout + stderr);
    }
}

#endif
