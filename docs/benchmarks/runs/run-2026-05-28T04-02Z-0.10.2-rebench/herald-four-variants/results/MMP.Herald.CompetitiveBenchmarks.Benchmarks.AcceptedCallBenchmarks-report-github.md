```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host] : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

Job=InProcess  Toolchain=InProcessEmitToolchain  MaxIterationCount=20  
MaxWarmupIterationCount=8  

```
| Method                                        | Mean      | Error    | StdDev   | Gen0   | Allocated |
|---------------------------------------------- |----------:|---------:|---------:|-------:|----------:|
| &#39;Herald: 4 props&#39;                             |  27.03 ns | 0.068 ns | 0.060 ns |      - |         - |
| &#39;Herald: 4 props (interpolated)&#39;              |  84.40 ns | 0.281 ns | 0.263 ns |      - |         - |
| &#39;Herald: 4 props (level-bound)&#39;               |  85.10 ns | 0.225 ns | 0.210 ns |      - |         - |
| &#39;Herald: 4 props (typed args)&#39;                |  34.40 ns | 0.164 ns | 0.154 ns |      - |         - |
| &#39;Herald: 4 props (interpolated, system tags)&#39; | 913.71 ns | 5.786 ns | 5.129 ns | 0.1173 |    1840 B |
| &#39;Herald: 4 props (manual array, system tags)&#39; | 824.31 ns | 6.443 ns | 6.027 ns | 0.1049 |    1656 B |
