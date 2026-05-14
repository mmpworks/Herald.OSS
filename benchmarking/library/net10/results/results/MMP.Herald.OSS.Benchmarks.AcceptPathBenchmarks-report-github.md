```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method                     | Mean     | Error    | StdDev   | Gen0   | Allocated |
|--------------------------- |---------:|---------:|---------:|-------:|----------:|
| Info_no_properties         | 25.28 ns | 0.195 ns | 0.182 ns |      - |         - |
| Info_with_one_property     | 29.75 ns | 0.575 ns | 0.806 ns | 0.0004 |      24 B |
| Info_with_three_properties | 32.86 ns | 0.449 ns | 0.398 ns | 0.0008 |      48 B |
