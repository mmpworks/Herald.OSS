```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                                   | Mean     | Error    | StdDev   | Allocated |
|----------------------------------------- |---------:|---------:|---------:|----------:|
| Herald_TypedArgs_FourProps_AllStrings    | 27.16 ns | 0.274 ns | 0.256 ns |         - |
| Herald_TypedArgs_FourProps_MixedTypes    | 26.65 ns | 0.067 ns | 0.059 ns |         - |
| Herald_TypedArgs_SixteenProps_AllStrings | 47.27 ns | 0.145 ns | 0.128 ns |         - |
| Herald_TypedArgs_SixteenProps_MixedTypes | 40.44 ns | 0.142 ns | 0.126 ns |         - |
