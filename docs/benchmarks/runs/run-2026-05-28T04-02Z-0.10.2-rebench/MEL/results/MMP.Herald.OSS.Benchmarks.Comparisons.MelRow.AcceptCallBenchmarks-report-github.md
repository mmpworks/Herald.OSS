```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-AZNXIW : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method        | Mean       | Error     | StdDev    | Gen0   | Allocated |
|-------------- |-----------:|----------:|----------:|-------:|----------:|
| Mel_ZeroProps |   9.230 ns | 0.0268 ns | 0.0250 ns |      - |         - |
| Mel_OneProp   |  53.680 ns | 0.8535 ns | 0.7566 ns | 0.0018 |     104 B |
| Mel_FourProps | 161.861 ns | 2.8365 ns | 2.6533 ns | 0.0036 |     208 B |
