```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                                  | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------------------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| PureKernel_FourProps                    |  26.89 ns | 0.253 ns | 0.211 ns |  1.00 |    0.01 |      - |         - |          NA |
| OneLegacySink_ForcesChainPath_FourProps | 676.98 ns | 8.625 ns | 7.646 ns | 25.17 |    0.33 | 0.0200 |    1160 B |          NA |
