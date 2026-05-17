```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method            | Mean      | Error    | StdDev   | Gen0   | Allocated |
|------------------ |----------:|---------:|---------:|-------:|----------:|
| Serilog_ZeroProps |  88.11 ns | 1.707 ns | 1.753 ns | 0.0027 |     160 B |
| Serilog_OneProp   | 123.53 ns | 2.395 ns | 3.029 ns | 0.0067 |     384 B |
| Serilog_FourProps | 209.71 ns | 3.696 ns | 3.457 ns | 0.0124 |     720 B |
