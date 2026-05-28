```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-HETLNC : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method            | Mean     | Error   | StdDev  | Allocated |
|------------------ |---------:|--------:|--------:|----------:|
| ZLogger_ZeroProps | 289.5 ns | 5.35 ns | 5.01 ns |         - |
| ZLogger_OneProp   | 292.2 ns | 4.71 ns | 4.41 ns |         - |
| ZLogger_FourProps | 271.6 ns | 3.09 ns | 2.89 ns |      66 B |
