```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                           | Mean      | Error    | StdDev    | Median    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------- |----------:|---------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Herald_Native_FourProps          |  35.83 ns | 1.116 ns |  3.291 ns |  34.68 ns |  1.01 |    0.13 | 0.0008 |      48 B |        1.00 |
| Herald_Via_Mel_Adapter_FourProps | 125.40 ns | 3.738 ns | 11.023 ns | 118.03 ns |  3.53 |    0.43 | 0.0029 |     168 B |        3.50 |
| Mel_Native_Active_Null_FourProps | 159.69 ns | 3.656 ns | 10.722 ns | 156.03 ns |  4.49 |    0.49 | 0.0036 |     208 B |        4.33 |
