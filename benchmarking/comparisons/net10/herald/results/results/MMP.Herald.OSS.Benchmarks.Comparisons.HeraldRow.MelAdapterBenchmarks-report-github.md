```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                           | Mean      | Error    | StdDev    | Median    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------- |----------:|---------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Herald_Native_FourProps          |  27.53 ns | 0.326 ns |  0.289 ns |  27.51 ns |  1.00 |    0.01 |      - |         - |          NA |
| Herald_Via_Mel_Adapter_FourProps | 149.15 ns | 7.232 ns | 21.323 ns | 133.89 ns |  5.42 |    0.77 | 0.0029 |     168 B |          NA |
| Mel_Native_Active_Null_FourProps | 152.41 ns | 0.988 ns |  0.925 ns | 152.32 ns |  5.54 |    0.06 | 0.0036 |     208 B |          NA |
