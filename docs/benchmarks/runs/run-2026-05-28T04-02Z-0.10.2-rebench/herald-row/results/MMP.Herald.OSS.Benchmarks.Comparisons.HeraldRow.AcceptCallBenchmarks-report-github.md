```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-GZIPJH : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method           | Mean     | Error    | StdDev   | Allocated |
|----------------- |---------:|---------:|---------:|----------:|
| Herald_ZeroProps | 24.74 ns | 0.088 ns | 0.082 ns |         - |
| Herald_OneProp   | 26.35 ns | 0.087 ns | 0.082 ns |         - |
| Herald_FourProps | 30.10 ns | 0.137 ns | 0.128 ns |         - |
