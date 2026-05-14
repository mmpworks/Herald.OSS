```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                                       | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------------- |-----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| FiveHealthy_AllSinksLand                     |   397.3 ns |  7.73 ns | 12.26 ns |  1.00 |    0.04 | 0.0114 |     664 B |        1.00 |
| FourHealthyOneThrowing_HealthySinksStillLand | 2,406.1 ns | 18.27 ns | 16.19 ns |  6.06 |    0.19 | 0.0191 |    1224 B |        1.84 |
