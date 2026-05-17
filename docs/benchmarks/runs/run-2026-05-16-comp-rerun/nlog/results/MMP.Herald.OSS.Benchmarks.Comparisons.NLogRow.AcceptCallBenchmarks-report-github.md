```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method         | Mean     | Error    | StdDev   | Median   | Gen0   | Gen1   | Gen2   | Allocated |
|--------------- |---------:|---------:|---------:|---------:|-------:|-------:|-------:|----------:|
| NLog_ZeroProps | 33.36 ns | 0.673 ns | 0.692 ns | 33.28 ns | 0.0027 | 0.0001 | 0.0001 |         - |
| NLog_OneProp   | 42.73 ns | 0.844 ns | 2.148 ns | 41.80 ns | 0.0038 |      - |      - |     176 B |
| NLog_FourProps | 58.55 ns | 0.800 ns | 0.748 ns | 58.36 ns | 0.0043 |      - |      - |     248 B |
