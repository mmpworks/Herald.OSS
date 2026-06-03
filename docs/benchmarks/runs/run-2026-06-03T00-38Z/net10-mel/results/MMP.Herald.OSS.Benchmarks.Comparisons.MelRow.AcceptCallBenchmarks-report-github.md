```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-NNRZTO : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=16  MaxWarmupIterationCount=8  

```
| Method           | Mean        | Error      | StdDev     | Gen0   | Allocated |
|----------------- |------------:|-----------:|-----------:|-------:|----------:|
| Mel_ZeroProps    |    17.82 ns |   1.058 ns |   1.039 ns |      - |         - |
| Mel_OneProp      |   102.56 ns |  11.571 ns |  10.824 ns | 0.0018 |     104 B |
| Mel_TwoProps     |   128.49 ns |  24.489 ns |  24.052 ns | 0.0021 |     128 B |
| Mel_FourProps    |   278.00 ns |  19.158 ns |  14.957 ns | 0.0019 |     208 B |
| Mel_EightProps   |   593.51 ns |  91.235 ns |  89.605 ns | 0.0057 |     352 B |
| Mel_TwelveProps  |   847.79 ns |  95.101 ns |  88.958 ns | 0.0086 |     496 B |
| Mel_SixteenProps | 1,197.27 ns | 265.108 ns | 260.371 ns | 0.0095 |     648 B |
