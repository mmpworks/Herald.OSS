```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                 | Mean      | Error    | StdDev    | Median    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |----------:|---------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Herald_HeraldLog       |  26.73 ns | 0.153 ns |  0.128 ns |  26.69 ns |  1.00 |    0.01 |      - |         - |          NA |
| ZLogger_ZLoggerMessage | 145.32 ns | 2.541 ns |  2.495 ns | 145.30 ns |  5.44 |    0.09 |      - |       7 B |          NA |
| Mel_LoggerMessage      | 171.89 ns | 6.402 ns | 18.775 ns | 166.63 ns |  6.43 |    0.70 | 0.0041 |     232 B |          NA |
