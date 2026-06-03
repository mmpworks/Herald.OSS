```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-PEQZIJ : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=16  MaxWarmupIterationCount=8  

```
| Method               | Mean     | Error    | StdDev   | Gen0   | Allocated |
|--------------------- |---------:|---------:|---------:|-------:|----------:|
| Log4Net_ZeroProps    | 214.3 ns | 26.89 ns | 25.15 ns | 0.0029 |     168 B |
| Log4Net_OneProp      | 247.6 ns | 22.03 ns | 21.63 ns | 0.0067 |     264 B |
| Log4Net_TwoProps     | 265.1 ns | 48.39 ns | 47.52 ns | 0.0057 |     272 B |
| Log4Net_FourProps    | 316.4 ns | 58.69 ns | 54.90 ns | 0.0057 |     336 B |
| Log4Net_EightProps   | 349.4 ns | 59.48 ns | 58.42 ns | 0.0076 |     440 B |
| Log4Net_TwelveProps  | 372.2 ns | 54.31 ns | 53.34 ns | 0.0095 |     544 B |
| Log4Net_SixteenProps | 430.5 ns | 65.86 ns | 64.69 ns | 0.0186 |     648 B |
