```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method                              | Mean     | Error    | StdDev   | Gen0   | Allocated |
|------------------------------------ |---------:|---------:|---------:|-------:|----------:|
| Herald_HotReload_FastPath_LevelOnly | 40.15 μs | 1.970 μs | 5.745 μs | 0.9155 |  53.79 KB |
| Herald_HotReload_SlowPath_NoChange  | 32.65 μs | 0.640 μs | 0.567 μs | 0.7324 |  53.79 KB |
