```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-HVRUAR : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method            | Mean      | Error    | StdDev   | Gen0   | Allocated |
|------------------ |----------:|---------:|---------:|-------:|----------:|
| Serilog_ZeroProps |  91.47 ns | 1.794 ns | 2.066 ns | 0.0027 |     160 B |
| Serilog_OneProp   | 146.13 ns | 3.862 ns | 4.448 ns | 0.0067 |     384 B |
| Serilog_FourProps | 243.97 ns | 6.982 ns | 8.041 ns | 0.0126 |     720 B |
