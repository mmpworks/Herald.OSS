```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method        | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| FanOut_Single | 19.55 ns | 0.301 ns | 0.282 ns |  1.00 |    0.02 |         - |          NA |
| FanOut_Pair   | 19.33 ns | 0.320 ns | 0.284 ns |  0.99 |    0.02 |         - |          NA |
| FanOut_Triple | 19.38 ns | 0.133 ns | 0.118 ns |  0.99 |    0.01 |         - |          NA |
| FanOut_Many_5 | 19.97 ns | 0.115 ns | 0.096 ns |  1.02 |    0.01 |         - |          NA |
