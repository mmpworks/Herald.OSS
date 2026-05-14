```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method            | Mean     | Error   | StdDev  | Gen0   | Allocated |
|------------------ |---------:|--------:|--------:|-------:|----------:|
| Log4Net_ZeroProps | 165.0 ns | 2.88 ns | 2.70 ns | 0.0038 |     168 B |
| Log4Net_OneProp   | 179.7 ns | 1.75 ns | 1.55 ns | 0.0045 |     264 B |
| Log4Net_FourProps | 191.4 ns | 2.80 ns | 2.62 ns | 0.0057 |     336 B |
