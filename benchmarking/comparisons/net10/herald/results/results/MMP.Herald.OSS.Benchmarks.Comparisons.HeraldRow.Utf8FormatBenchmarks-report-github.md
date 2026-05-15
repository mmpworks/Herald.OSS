```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                      | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| Herald_Utf8Json_Discard     | 402.9 ns | 4.22 ns | 3.95 ns |  1.00 |    0.01 | 0.0038 |     224 B |        1.00 |
| ZLogger_Utf8_StreamNull     | 277.4 ns | 5.54 ns | 5.18 ns |  0.69 |    0.01 |      - |      67 B |        0.30 |
| Serilog_CompactJson_Discard | 445.9 ns | 5.08 ns | 4.51 ns |  1.11 |    0.02 | 0.0167 |     968 B |        4.32 |
