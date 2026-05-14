```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method                          | Mean       | Error     | StdDev    | Median     | Ratio | Allocated | Alloc Ratio |
|-------------------------------- |-----------:|----------:|----------:|-----------:|------:|----------:|------------:|
| Herald_Rejected_Trace_ZeroProps |  0.0028 ns | 0.0043 ns | 0.0038 ns |  0.0009 ns | 0.000 |         - |          NA |
| Herald_Rejected_Debug_ZeroProps |  0.0018 ns | 0.0036 ns | 0.0032 ns |  0.0000 ns | 0.000 |         - |          NA |
| Herald_Rejected_Info_ZeroProps  |  0.0073 ns | 0.0075 ns | 0.0063 ns |  0.0084 ns | 0.000 |         - |          NA |
| Herald_Rejected_Debug_OneProp   |  0.2220 ns | 0.0124 ns | 0.0116 ns |  0.2230 ns | 0.009 |         - |          NA |
| Herald_Rejected_Debug_FourProps |  0.2141 ns | 0.0122 ns | 0.0102 ns |  0.2163 ns | 0.009 |         - |          NA |
| Herald_Accepted_Warn_ZeroProps  | 25.1565 ns | 0.1887 ns | 0.1672 ns | 25.0928 ns | 1.000 |         - |          NA |
