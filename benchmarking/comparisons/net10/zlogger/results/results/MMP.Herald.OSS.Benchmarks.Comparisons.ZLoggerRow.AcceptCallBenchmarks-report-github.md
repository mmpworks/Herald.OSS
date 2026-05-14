```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method            | Mean     | Error   | StdDev   | Allocated |
|------------------ |---------:|--------:|---------:|----------:|
| ZLogger_ZeroProps | 287.4 ns | 4.16 ns |  3.89 ns |         - |
| ZLogger_OneProp   | 296.3 ns | 5.66 ns |  5.55 ns |         - |
| ZLogger_FourProps | 298.8 ns | 5.93 ns | 13.99 ns |      71 B |
