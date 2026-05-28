```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-GZIPJH : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method                          | Mean       | Error     | StdDev    | Median     | Ratio | Allocated | Alloc Ratio |
|-------------------------------- |-----------:|----------:|----------:|-----------:|------:|----------:|------------:|
| Herald_Rejected_Trace_ZeroProps |  0.0002 ns | 0.0007 ns | 0.0006 ns |  0.0000 ns | 0.000 |         - |          NA |
| Herald_Rejected_Debug_ZeroProps |  0.0090 ns | 0.0047 ns | 0.0044 ns |  0.0086 ns | 0.000 |         - |          NA |
| Herald_Rejected_Info_ZeroProps  |  0.0020 ns | 0.0031 ns | 0.0029 ns |  0.0000 ns | 0.000 |         - |          NA |
| Herald_Rejected_Debug_OneProp   |  1.6516 ns | 0.0177 ns | 0.0165 ns |  1.6554 ns | 0.066 |         - |          NA |
| Herald_Rejected_Debug_FourProps |  9.6662 ns | 0.0268 ns | 0.0251 ns |  9.6732 ns | 0.388 |         - |          NA |
| Herald_Accepted_Warn_ZeroProps  | 24.9018 ns | 0.0848 ns | 0.0751 ns | 24.8739 ns | 1.000 |         - |          NA |
