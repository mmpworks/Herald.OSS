```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host] : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=16  MaxWarmupIterationCount=8  

```
| Method           | Mean | Error |
|----------------- |-----:|------:|
| NLog_TwelveProps |   NA |    NA |

Benchmarks with issues:
  AcceptCallBenchmarks.NLog_TwelveProps: Job-IZLTLS(MaxIterationCount=16, MaxWarmupIterationCount=8)
