#nullable enable

using BenchmarkDotNet.Running;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

public static class Program
{
    public static void Main(string[] args)
    {
        // Diagnostic hook for Utf8FormatBenchmarks: when invoked as
        //     dotnet run -c Release --framework net10.0 -- --dump-utf8-samples
        // run the one-shot byte-count dump and exit. Keeps the standard
        // BDN switcher behaviour for all other arg shapes.
        if (args.Length == 1 && args[0] == "--dump-utf8-samples")
        {
            Utf8FormatBenchmarks.DumpSampleEvents();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
