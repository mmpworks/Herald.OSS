```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method            | Mean      | Error    | StdDev   | Gen0   | Allocated |
|------------------ |----------:|---------:|---------:|-------:|----------:|
| Serilog_ZeroProps |  89.21 ns | 0.878 ns | 0.733 ns | 0.0027 |     160 B |
| Serilog_OneProp   | 127.21 ns | 2.127 ns | 1.776 ns | 0.0067 |     384 B |
| Serilog_FourProps | 207.62 ns | 3.037 ns | 2.536 ns | 0.0126 |     720 B |
