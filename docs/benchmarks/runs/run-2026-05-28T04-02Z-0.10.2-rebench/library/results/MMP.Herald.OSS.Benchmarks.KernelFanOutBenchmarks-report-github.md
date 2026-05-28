```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-PFYEXA : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method         | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------------- |---------:|---------:|---------:|------:|----------:|------------:|
| FanOut_Single  | 18.98 ns | 0.080 ns | 0.075 ns |  1.00 |         - |          NA |
| FanOut_Pair    | 19.27 ns | 0.092 ns | 0.086 ns |  1.02 |         - |          NA |
| FanOut_Triple  | 19.24 ns | 0.059 ns | 0.049 ns |  1.01 |         - |          NA |
| FanOut_Many_5  | 21.70 ns | 0.083 ns | 0.078 ns |  1.14 |         - |          NA |
| FanOut_Many_8  | 24.72 ns | 0.069 ns | 0.061 ns |  1.30 |         - |          NA |
| FanOut_Many_16 | 31.20 ns | 0.204 ns | 0.191 ns |  1.64 |         - |          NA |
