```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                                    | Mean       | Error     | StdDev    | Median     | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------------------ |-----------:|----------:|----------:|-----------:|-------:|--------:|-------:|----------:|------------:|
| Recorder_BufferWrite_DebugBelowFloor      |  0.2049 ns | 0.0033 ns | 0.0027 ns |  0.2043 ns |   0.92 |    0.02 |      - |         - |          NA |
| Baseline_RejectedAtFilter_DebugBelowFloor |  0.2226 ns | 0.0051 ns | 0.0047 ns |  0.2240 ns |   1.00 |    0.03 |      - |         - |          NA |
| Recorder_TriggerDump_ErrorFlushes         | 30.4110 ns | 0.6338 ns | 1.3912 ns | 29.6018 ns | 136.69 |    6.83 | 0.0004 |      24 B |          NA |
