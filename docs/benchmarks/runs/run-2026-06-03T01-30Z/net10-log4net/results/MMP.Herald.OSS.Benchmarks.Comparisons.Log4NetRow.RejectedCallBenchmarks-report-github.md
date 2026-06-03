```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-RBRCNH : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method                                   | Mean       | Error      | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------------------- |-----------:|-----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| Log4Net_Rejected_Debug_ZeroProps         |   5.563 ns |  0.1977 ns |  0.1942 ns |  0.03 |    0.00 |      - |         - |        0.00 |
| Log4Net_Rejected_Debug_OneProp           |  10.172 ns |  0.2720 ns |  0.3132 ns |  0.05 |    0.01 | 0.0004 |      24 B |        0.14 |
| Log4Net_Rejected_Debug_TwoProps          |   9.875 ns |  0.2242 ns |  0.2399 ns |  0.05 |    0.01 | 0.0005 |      24 B |        0.14 |
| Log4Net_Rejected_Debug_FourProps         |  29.277 ns |  0.8714 ns |  0.9685 ns |  0.15 |    0.02 | 0.0039 |     128 B |        0.76 |
| Log4Net_Rejected_Debug_EightProps        |  51.304 ns |  1.7608 ns |  2.0278 ns |  0.27 |    0.03 | 0.0041 |     232 B |        1.38 |
| Log4Net_Rejected_Debug_TwelveProps       |  72.619 ns |  2.3091 ns |  2.5666 ns |  0.38 |    0.04 | 0.0058 |     336 B |        2.00 |
| Log4Net_Rejected_Debug_SixteenProps      |  93.837 ns |  3.3675 ns |  3.8780 ns |  0.49 |    0.05 | 0.0076 |     440 B |        2.62 |
| Log4Net_Rejected_Debug_FourProps_Guarded |   5.205 ns |  0.4659 ns |  0.5365 ns |  0.03 |    0.00 |      - |         - |        0.00 |
| Log4Net_Accepted_Warn_ZeroProps          | 195.106 ns | 16.5054 ns | 19.0076 ns |  1.01 |    0.14 | 0.0029 |     168 B |        1.00 |
