```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                      | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------- |---------:|---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| Herald_Utf8Json_Discard     | 442.3 ns |  8.61 ns | 23.58 ns | 444.0 ns |  1.00 |    0.07 | 0.0052 |     304 B |        1.00 |
| ZLogger_Utf8_StreamNull     | 288.1 ns |  5.73 ns |  6.13 ns | 286.9 ns |  0.65 |    0.04 |      - |      77 B |        0.25 |
| Serilog_CompactJson_Discard | 489.5 ns | 17.17 ns | 50.62 ns | 460.1 ns |  1.11 |    0.13 | 0.0162 |     968 B |        3.18 |
