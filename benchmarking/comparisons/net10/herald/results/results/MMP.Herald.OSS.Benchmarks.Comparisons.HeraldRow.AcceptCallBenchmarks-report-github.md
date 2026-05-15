```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method           | Mean     | Error    | StdDev   | Median   | Allocated |
|----------------- |---------:|---------:|---------:|---------:|----------:|
| Herald_ZeroProps | 25.52 ns | 0.540 ns | 0.792 ns | 25.10 ns |         - |
| Herald_OneProp   | 26.00 ns | 0.201 ns | 0.178 ns | 25.95 ns |         - |
| Herald_FourProps | 26.64 ns | 0.041 ns | 0.036 ns | 26.62 ns |         - |
