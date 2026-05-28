```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host] : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

Job=InProcess  Toolchain=InProcessEmitToolchain  MaxIterationCount=20  
MaxWarmupIterationCount=8  

```
| Method                                                   | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------------------------- |------------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| &#39;Herald: baseline (sync, no async, 4 props)&#39;             |    34.29 ns |  0.123 ns |  0.109 ns |  1.00 |    0.00 |      - |         - |          NA |
| &#39;Herald: WithAsyncLogging (chain-decorator async)&#39;       | 1,005.99 ns | 19.397 ns | 17.195 ns | 29.33 |    0.49 | 0.0782 |    1225 B |          NA |
| &#39;Herald: FastPathAsyncSink (kernel-aware async wrapper)&#39; |   291.20 ns |  4.400 ns |  4.115 ns |  8.49 |    0.12 |      - |       3 B |          NA |
