```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method                                   | Mean     | Error    | StdDev   | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|---------:|-------:|----------:|
| Herald_TypedArgs_FourProps_AllStrings    | 26.63 ns | 0.274 ns | 0.256 ns |      - |         - |
| Herald_TypedArgs_FourProps_MixedTypes    | 35.63 ns | 0.238 ns | 0.185 ns | 0.0013 |      72 B |
| Herald_TypedArgs_SixteenProps_AllStrings | 37.21 ns | 0.602 ns | 0.534 ns |      - |         - |
| Herald_TypedArgs_SixteenProps_MixedTypes | 83.28 ns | 1.650 ns | 1.621 ns | 0.0050 |     288 B |
