```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method                       | Mean      | Error    | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------- |----------:|---------:|----------:|------:|--------:|-------:|----------:|------------:|
| Herald_Baseline_NoRedaction  |  25.96 ns | 0.146 ns |  0.129 ns |  1.00 |    0.01 |      - |         - |          NA |
| Herald_WithFastRedaction     |  34.20 ns | 0.326 ns |  0.272 ns |  1.32 |    0.01 |      - |         - |          NA |
| Herald_WithCompiledRedaction | 407.42 ns | 8.017 ns | 15.058 ns | 15.69 |    0.58 | 0.0134 |     672 B |          NA |
