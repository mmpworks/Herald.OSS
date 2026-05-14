```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method           | Mean     | Error    | StdDev   | Gen0   | Allocated |
|----------------- |---------:|---------:|---------:|-------:|----------:|
| Herald_ZeroProps | 25.07 ns | 0.240 ns | 0.212 ns |      - |         - |
| Herald_OneProp   | 29.55 ns | 0.616 ns | 0.710 ns | 0.0004 |      24 B |
| Herald_FourProps | 36.27 ns | 0.685 ns | 0.607 ns | 0.0013 |      72 B |
