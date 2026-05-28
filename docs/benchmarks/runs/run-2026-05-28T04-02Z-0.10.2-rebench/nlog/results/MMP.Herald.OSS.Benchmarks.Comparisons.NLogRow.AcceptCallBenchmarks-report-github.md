```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-UGRYYA : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method         | Mean     | Error    | StdDev   | Gen0   | Allocated |
|--------------- |---------:|---------:|---------:|-------:|----------:|
| NLog_ZeroProps | 35.89 ns | 0.993 ns | 1.144 ns | 0.0021 |     120 B |
| NLog_OneProp   | 49.48 ns | 0.781 ns | 0.692 ns | 0.0030 |     176 B |
| NLog_FourProps | 70.83 ns | 1.441 ns | 1.602 ns | 0.0043 |     248 B |
