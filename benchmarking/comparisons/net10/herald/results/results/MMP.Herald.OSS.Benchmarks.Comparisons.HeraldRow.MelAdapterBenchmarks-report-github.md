```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                           | Mean      | Error    | StdDev    | Median    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------- |----------:|---------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Herald_Native_FourProps          |  33.90 ns | 0.709 ns |  1.570 ns |  33.09 ns |  1.00 |    0.06 | 0.0008 |      48 B |        1.00 |
| Herald_Via_Mel_Adapter_FourProps | 293.76 ns | 5.871 ns | 11.860 ns | 289.64 ns |  8.68 |    0.52 | 0.0091 |     528 B |       11.00 |
| Mel_Native_Active_Null_FourProps | 157.10 ns | 3.167 ns |  5.711 ns | 156.37 ns |  4.64 |    0.27 | 0.0036 |     208 B |        4.33 |
