```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                     | Mean     | Error    | StdDev   | Allocated |
|--------------------------- |---------:|---------:|---------:|----------:|
| Info_no_properties         | 24.90 ns | 0.082 ns | 0.076 ns |         - |
| Info_with_one_property     | 26.04 ns | 0.125 ns | 0.117 ns |         - |
| Info_with_three_properties | 26.64 ns | 0.142 ns | 0.126 ns |         - |
