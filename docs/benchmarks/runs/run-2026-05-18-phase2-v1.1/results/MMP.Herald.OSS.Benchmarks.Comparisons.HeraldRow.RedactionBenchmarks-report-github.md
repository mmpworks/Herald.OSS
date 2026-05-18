```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                                                 | Mean        | Error     | StdDev     | Ratio  | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------------------------------------------------- |------------:|----------:|-----------:|-------:|--------:|-------:|-------:|-------:|----------:|------------:|
| Herald_Baseline_NoRedaction                            |    27.86 ns |  0.557 ns |   0.547 ns |   1.00 |    0.03 |      - |      - |      - |         - |          NA |
| Herald_WithFastRedaction                               |    75.94 ns |  1.527 ns |   2.868 ns |   2.73 |    0.11 | 0.0011 | 0.0001 | 0.0001 |         - |          NA |
| Herald_WithCompiledRedaction                           |   893.52 ns | 26.336 ns |  77.654 ns |  32.09 |    2.84 | 0.0238 |      - |      - |    1368 B |          NA |
| Herald_Baseline_NoRedaction_SixteenProps               |    82.53 ns |  1.692 ns |   4.118 ns |   2.96 |    0.16 |      - |      - |      - |         - |          NA |
| Herald_WithFastRedaction_SixteenProps_TwoRulesFire     |   245.11 ns |  4.530 ns |   9.253 ns |   8.80 |    0.37 | 0.0010 |      - |      - |      56 B |          NA |
| Herald_WithCompiledRedaction_SixteenProps_TwoRulesFire | 3,403.16 ns | 67.864 ns | 147.530 ns | 122.21 |    5.74 | 0.0763 |      - |      - |    4488 B |          NA |
