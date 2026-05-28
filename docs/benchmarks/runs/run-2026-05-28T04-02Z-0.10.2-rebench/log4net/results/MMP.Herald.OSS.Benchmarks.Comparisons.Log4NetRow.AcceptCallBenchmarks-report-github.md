```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-JMTFDQ : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method            | Mean     | Error   | StdDev  | Gen0   | Allocated |
|------------------ |---------:|--------:|--------:|-------:|----------:|
| Log4Net_ZeroProps | 161.4 ns | 2.21 ns | 2.07 ns | 0.0029 |     168 B |
| Log4Net_OneProp   | 181.3 ns | 2.56 ns | 2.39 ns | 0.0045 |     264 B |
| Log4Net_FourProps | 190.5 ns | 1.35 ns | 1.26 ns | 0.0057 |     336 B |
