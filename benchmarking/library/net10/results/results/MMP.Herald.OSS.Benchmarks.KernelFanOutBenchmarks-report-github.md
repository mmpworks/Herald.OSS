```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method         | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| FanOut_Single  | 20.04 ns | 0.408 ns | 0.585 ns |  1.00 |    0.04 |         - |          NA |
| FanOut_Pair    | 19.39 ns | 0.240 ns | 0.225 ns |  0.97 |    0.03 |         - |          NA |
| FanOut_Triple  | 20.42 ns | 0.437 ns | 0.902 ns |  1.02 |    0.05 |         - |          NA |
| FanOut_Many_5  | 20.72 ns | 0.438 ns | 0.885 ns |  1.03 |    0.05 |         - |          NA |
| FanOut_Many_8  | 21.43 ns | 0.429 ns | 0.401 ns |  1.07 |    0.04 |         - |          NA |
| FanOut_Many_16 | 25.76 ns | 0.119 ns | 0.093 ns |  1.29 |    0.04 |         - |          NA |
