#nullable enable

using BenchmarkDotNet.Running;
using MMP.Herald.OSS.Benchmarks;

namespace MMP.Herald.OSS.Benchmarks.Net8;

public static class Program
{
    public static void Main(string[] args)
    {
        // Scan the shared bench assembly so BenchmarkSwitcher picks up
        // KernelFanOutBenchmarks + AcceptPathBenchmarks. Using the bench
        // type as the assembly anchor (rather than typeof(Program))
        // ensures BDN sees the [Benchmark] methods that live in the
        // shared project.
        BenchmarkSwitcher.FromAssembly(typeof(KernelFanOutBenchmarks).Assembly).Run(args);
    }
}
