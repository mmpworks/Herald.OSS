```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-FVBYXC : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=16  MaxWarmupIterationCount=8  

```
| Method            | Mean        | Error     | StdDev    | Gen0   | Gen1   | Gen2   | Allocated |
|------------------ |------------:|----------:|----------:|-------:|-------:|-------:|----------:|
| NLog_ZeroProps    |    44.69 ns |  1.412 ns |  1.321 ns | 0.0021 |      - |      - |     120 B |
| NLog_OneProp      |    60.35 ns |  2.493 ns |  2.449 ns | 0.0040 |      - |      - |     176 B |
| NLog_TwoProps     |    62.76 ns |  1.719 ns |  1.608 ns | 0.0032 |      - |      - |         - |
| NLog_FourProps    |    88.00 ns |  2.884 ns |  2.833 ns | 0.0064 | 0.0001 | 0.0001 |         - |
| NLog_EightProps   |   896.05 ns | 16.617 ns | 16.320 ns | 0.0172 |      - |      - |     984 B |
| NLog_TwelveProps  | 1,353.28 ns | 31.440 ns | 30.878 ns | 0.0629 | 0.0019 | 0.0019 |         - |
| NLog_SixteenProps | 1,726.65 ns | 18.832 ns | 17.615 ns | 0.0420 |      - |      - |    2480 B |
