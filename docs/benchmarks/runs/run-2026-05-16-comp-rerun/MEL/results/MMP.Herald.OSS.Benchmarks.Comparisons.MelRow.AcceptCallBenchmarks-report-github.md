```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method        | Mean      | Error    | StdDev   | Gen0   | Gen1   | Gen2   | Allocated |
|-------------- |----------:|---------:|---------:|-------:|-------:|-------:|----------:|
| Mel_ZeroProps |  10.12 ns | 0.145 ns | 0.136 ns |      - |      - |      - |         - |
| Mel_OneProp   |  53.16 ns | 1.091 ns | 1.380 ns | 0.0023 |      - |      - |     104 B |
| Mel_FourProps | 160.04 ns | 2.579 ns | 2.412 ns | 0.0041 | 0.0002 | 0.0002 |         - |
