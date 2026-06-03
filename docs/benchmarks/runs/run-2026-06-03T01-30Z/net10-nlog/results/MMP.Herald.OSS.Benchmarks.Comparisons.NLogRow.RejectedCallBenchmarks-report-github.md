```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-CDKDDF : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method                                | Mean       | Error     | StdDev    | Median     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------------- |-----------:|----------:|----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| NLog_Rejected_Debug_ZeroProps         |  0.2123 ns | 0.1525 ns | 0.1756 ns |  0.2041 ns | 0.005 |    0.00 |      - |         - |        0.00 |
| NLog_Rejected_Debug_OneProp           |  0.2280 ns | 0.1528 ns | 0.1760 ns |  0.2437 ns | 0.005 |    0.00 |      - |         - |        0.00 |
| NLog_Rejected_Debug_TwoProps          |  0.0302 ns | 0.0509 ns | 0.0587 ns |  0.0000 ns | 0.001 |    0.00 |      - |         - |        0.00 |
| NLog_Rejected_Debug_FourProps         | 22.1929 ns | 0.4707 ns | 0.4623 ns | 22.1149 ns | 0.506 |    0.02 | 0.0022 |     128 B |        1.07 |
| NLog_Rejected_Debug_EightProps        | 43.2610 ns | 1.1770 ns | 1.3082 ns | 43.0870 ns | 0.987 |    0.04 | 0.0041 |     232 B |        1.93 |
| NLog_Rejected_Debug_TwelveProps       | 60.4331 ns | 2.0545 ns | 2.3659 ns | 60.7166 ns | 1.379 |    0.06 | 0.0058 |     336 B |        2.80 |
| NLog_Rejected_Debug_SixteenProps      | 78.4723 ns | 1.1195 ns | 1.0472 ns | 78.4573 ns | 1.790 |    0.05 | 0.0076 |     440 B |        3.67 |
| NLog_Rejected_Debug_FourProps_Guarded |  0.0241 ns | 0.0315 ns | 0.0309 ns |  0.0143 ns | 0.001 |    0.00 |      - |         - |        0.00 |
| NLog_Accepted_Warn_ZeroProps          | 43.8611 ns | 0.9367 ns | 1.0788 ns | 43.6657 ns | 1.001 |    0.03 | 0.0021 |     120 B |        1.00 |
