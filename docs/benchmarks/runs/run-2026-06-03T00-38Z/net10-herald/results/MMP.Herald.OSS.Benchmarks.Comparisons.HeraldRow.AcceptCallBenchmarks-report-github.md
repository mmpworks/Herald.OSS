```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-ZCIYNY : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method              | Mean     | Error    | StdDev   | Allocated |
|-------------------- |---------:|---------:|---------:|----------:|
| Herald_ZeroProps    | 31.65 ns | 1.013 ns | 1.166 ns |         - |
| Herald_OneProp      | 39.47 ns | 0.872 ns | 0.969 ns |         - |
| Herald_TwoProps     | 43.04 ns | 1.126 ns | 1.251 ns |         - |
| Herald_FourProps    | 50.37 ns | 0.750 ns | 0.627 ns |         - |
| Herald_EightProps   | 65.97 ns | 1.274 ns | 1.251 ns |         - |
| Herald_TwelveProps  | 68.11 ns | 1.107 ns | 0.982 ns |         - |
| Herald_SixteenProps | 68.59 ns | 0.808 ns | 0.631 ns |         - |
