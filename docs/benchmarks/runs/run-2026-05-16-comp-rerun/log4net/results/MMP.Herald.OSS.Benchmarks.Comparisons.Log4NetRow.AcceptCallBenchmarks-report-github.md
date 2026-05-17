```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method            | Mean     | Error   | StdDev  | Gen0   | Allocated |
|------------------ |---------:|--------:|--------:|-------:|----------:|
| Log4Net_ZeroProps | 162.5 ns | 1.36 ns | 1.27 ns | 0.0029 |     168 B |
| Log4Net_OneProp   | 181.2 ns | 1.81 ns | 1.69 ns | 0.0045 |     264 B |
| Log4Net_FourProps | 191.7 ns | 3.77 ns | 3.53 ns | 0.0057 |     336 B |
