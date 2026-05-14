```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method                     | Mean     | Error    | StdDev   | Gen0   | Allocated |
|--------------------------- |---------:|---------:|---------:|-------:|----------:|
| Info_no_properties         | 25.32 ns | 0.332 ns | 0.310 ns |      - |         - |
| Info_with_one_property     | 29.44 ns | 0.216 ns | 0.191 ns | 0.0004 |      24 B |
| Info_with_three_properties | 33.11 ns | 0.621 ns | 0.581 ns | 0.0008 |      48 B |
