#nullable enable

using BenchmarkDotNet.Running;
using MMP.Herald.OSS.Benchmarks;

namespace MMP.Herald.OSS.Benchmarks.Net9;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(KernelFanOutBenchmarks).Assembly).Run(args);
    }
}
