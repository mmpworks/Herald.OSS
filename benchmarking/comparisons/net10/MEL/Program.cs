#nullable enable

using BenchmarkDotNet.Running;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.MelRow;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
