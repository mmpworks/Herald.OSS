```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-XMWQLI : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method                               | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------------------------------- |-----------:|----------:|----------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| Mel_Rejected_Debug_ZeroProps         |   8.568 ns | 0.5256 ns | 0.6053 ns |  0.42 |    0.04 |      - |      - |      - |         - |          NA |
| Mel_Rejected_Debug_OneProp           |  29.315 ns | 0.7637 ns | 0.8489 ns |  1.43 |    0.09 | 0.0013 |      - |      - |      56 B |          NA |
| Mel_Rejected_Debug_TwoProps          |  30.795 ns | 0.7269 ns | 0.8371 ns |  1.51 |    0.10 | 0.0011 |      - |      - |      64 B |          NA |
| Mel_Rejected_Debug_FourProps         |  44.469 ns | 1.7619 ns | 2.0291 ns |  2.17 |    0.16 | 0.0026 | 0.0001 | 0.0001 |         - |          NA |
| Mel_Rejected_Debug_EightProps        |  69.527 ns | 2.0203 ns | 2.3265 ns |  3.40 |    0.23 | 0.0041 |      - |      - |     232 B |          NA |
| Mel_Rejected_Debug_TwelveProps       |  96.788 ns | 2.1933 ns | 2.4378 ns |  4.73 |    0.30 | 0.0058 |      - |      - |     336 B |          NA |
| Mel_Rejected_Debug_SixteenProps      | 120.358 ns | 4.1209 ns | 4.5804 ns |  5.88 |    0.40 | 0.0076 |      - |      - |     440 B |          NA |
| Mel_Rejected_Debug_FourProps_Guarded |   2.166 ns | 0.3987 ns | 0.4592 ns |  0.11 |    0.02 |      - |      - |      - |         - |          NA |
| Mel_Accepted_Warn_ZeroProps          |  20.527 ns | 1.0257 ns | 1.1812 ns |  1.00 |    0.08 |      - |      - |      - |         - |          NA |
