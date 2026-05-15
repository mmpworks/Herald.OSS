```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                   | Mean      | Error    | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------- |----------:|---------:|----------:|------:|--------:|-------:|----------:|------------:|
| Herald_DestructureOrder  |  27.04 ns | 0.242 ns |  0.226 ns |  1.00 |    0.01 |      - |         - |          NA |
| Serilog_DestructureOrder | 533.14 ns | 7.882 ns | 10.522 ns | 19.72 |    0.41 | 0.0229 |    1320 B |          NA |
