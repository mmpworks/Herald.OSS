```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                      | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------- |-----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| Herald_Utf8Json_Discard     | 1,293.1 ns | 25.56 ns | 43.40 ns |  1.00 |    0.05 | 0.0248 |    1448 B |        1.00 |
| ZLogger_Utf8_StreamNull     |   285.3 ns |  4.65 ns |  4.12 ns |  0.22 |    0.01 |      - |      74 B |        0.05 |
| Serilog_CompactJson_Discard |   467.9 ns |  9.34 ns | 14.54 ns |  0.36 |    0.02 | 0.0167 |     968 B |        0.67 |
