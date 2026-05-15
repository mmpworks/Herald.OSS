```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                        | Mean     | Error    | StdDev   | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------------ |---------:|---------:|---------:|------:|-------:|----------:|------------:|
| Reload_Alone                  | 32.13 μs | 0.374 μs | 0.332 μs |  1.00 | 0.9155 |  53.83 KB |        1.00 |
| Reload_With_Interleaved_Emits | 36.23 μs | 0.328 μs | 0.307 μs |  1.13 | 1.0376 |   59.2 KB |        1.10 |
