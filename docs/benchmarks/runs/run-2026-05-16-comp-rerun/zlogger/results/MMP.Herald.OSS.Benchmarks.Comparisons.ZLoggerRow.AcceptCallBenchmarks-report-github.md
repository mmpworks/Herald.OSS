```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method            | Mean     | Error   | StdDev   | Allocated |
|------------------ |---------:|--------:|---------:|----------:|
| ZLogger_ZeroProps | 283.8 ns | 4.05 ns |  3.79 ns |         - |
| ZLogger_OneProp   | 324.8 ns | 3.76 ns |  3.52 ns |         - |
| ZLogger_FourProps | 290.0 ns | 5.79 ns | 11.28 ns |      81 B |
