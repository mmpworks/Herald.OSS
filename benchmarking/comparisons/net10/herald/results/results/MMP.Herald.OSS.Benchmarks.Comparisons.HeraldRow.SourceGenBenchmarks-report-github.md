```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                 | Mean      | Error    | StdDev   | Median    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |----------:|---------:|---------:|----------:|------:|--------:|-------:|----------:|------------:|
| Herald_HeraldLog       |  34.39 ns | 0.718 ns | 1.892 ns |  33.65 ns |  1.00 |    0.08 | 0.0008 |      48 B |        1.00 |
| ZLogger_ZLoggerMessage | 172.54 ns | 2.752 ns | 2.148 ns | 173.23 ns |  5.03 |    0.27 |      - |       5 B |        0.10 |
| Mel_LoggerMessage      | 156.52 ns | 3.166 ns | 7.017 ns | 155.48 ns |  4.56 |    0.31 | 0.0041 |     232 B |        4.83 |
