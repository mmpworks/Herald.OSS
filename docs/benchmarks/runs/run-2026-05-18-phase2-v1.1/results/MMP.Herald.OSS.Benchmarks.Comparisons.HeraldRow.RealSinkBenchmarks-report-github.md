```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                      | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| Herald_FourProps_NullSink   | 32.02 ns | 0.611 ns | 0.572 ns |  1.00 |    0.02 |         - |          NA |
| Herald_FourProps_MemorySink | 32.62 ns | 0.672 ns | 1.596 ns |  1.02 |    0.05 |         - |          NA |
| Herald_FourProps_FileSink   | 31.55 ns | 0.551 ns | 0.488 ns |  0.99 |    0.02 |         - |          NA |
