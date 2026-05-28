```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-GZIPJH : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

MaxIterationCount=20  MaxWarmupIterationCount=8  

```
| Method                                   | Mean     | Error    | StdDev   | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|---------:|-------:|----------:|
| Herald_TypedArgs_FourProps_AllStrings    | 30.19 ns | 0.090 ns | 0.080 ns |      - |         - |
| Herald_TypedArgs_FourProps_MixedTypes    | 29.88 ns | 0.102 ns | 0.090 ns |      - |         - |
| Herald_TypedArgs_SixteenProps_AllStrings | 45.71 ns | 0.176 ns | 0.165 ns |      - |         - |
| Herald_TypedArgs_SixteenProps_MixedTypes | 39.33 ns | 0.181 ns | 0.169 ns |      - |         - |
| Herald_TypedArgs_EightProps_AllStrings   | 38.02 ns | 0.145 ns | 0.135 ns |      - |         - |
| Herald_TypedArgs_EightProps_MixedTypes   | 38.22 ns | 0.209 ns | 0.195 ns |      - |         - |
| Herald_TypedArgs_FourProps_AuditShape    | 42.88 ns | 0.511 ns | 0.453 ns | 0.0011 |      64 B |
| Herald_TypedArgs_FourProps_FinanceShape  | 48.43 ns | 0.715 ns | 0.634 ns | 0.0032 |      96 B |
