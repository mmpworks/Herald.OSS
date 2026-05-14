```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method        | Mean       | Error     | StdDev    | Gen0   | Allocated |
|-------------- |-----------:|----------:|----------:|-------:|----------:|
| Mel_ZeroProps |   9.292 ns | 0.1689 ns | 0.2074 ns |      - |         - |
| Mel_OneProp   |  51.440 ns | 0.9743 ns | 0.9113 ns | 0.0023 |     104 B |
| Mel_FourProps | 150.779 ns | 2.1221 ns | 1.7721 ns | 0.0036 |     208 B |
