```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2


```
| Method                                   | Mean     | Error    | StdDev   | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|---------:|-------:|----------:|
| Herald_TypedArgs_FourProps_AllStrings    | 33.03 ns | 0.689 ns | 1.527 ns |      - |         - |
| Herald_TypedArgs_FourProps_MixedTypes    | 31.46 ns | 0.254 ns | 0.226 ns |      - |         - |
| Herald_TypedArgs_SixteenProps_AllStrings | 79.45 ns | 1.354 ns | 1.330 ns |      - |         - |
| Herald_TypedArgs_SixteenProps_MixedTypes | 58.79 ns | 1.126 ns | 1.053 ns |      - |         - |
| Herald_TypedArgs_EightProps_AllStrings   | 39.74 ns | 0.697 ns | 0.652 ns |      - |         - |
| Herald_TypedArgs_EightProps_MixedTypes   | 39.79 ns | 0.702 ns | 0.623 ns |      - |         - |
| Herald_TypedArgs_FourProps_AuditShape    | 47.71 ns | 0.974 ns | 1.829 ns | 0.0016 |      64 B |
| Herald_TypedArgs_FourProps_FinanceShape  | 53.79 ns | 1.069 ns | 2.477 ns | 0.0019 |      96 B |
