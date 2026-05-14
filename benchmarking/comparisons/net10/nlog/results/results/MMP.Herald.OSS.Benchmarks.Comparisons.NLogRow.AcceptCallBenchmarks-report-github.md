```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method         | Mean     | Error    | StdDev   | Gen0   | Allocated |
|--------------- |---------:|---------:|---------:|-------:|----------:|
| NLog_ZeroProps | 36.49 ns | 0.348 ns | 0.308 ns | 0.0021 |     120 B |
| NLog_OneProp   | 41.00 ns | 0.734 ns | 0.687 ns | 0.0030 |     176 B |
| NLog_FourProps | 58.04 ns | 0.650 ns | 0.576 ns | 0.0044 |     248 B |
