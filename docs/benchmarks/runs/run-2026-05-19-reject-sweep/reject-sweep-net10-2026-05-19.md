# Reject-sweep — rejection-path scaling across six loggers — net10

Per-arity rejection-path measurements for Herald and five comparison
loggers (Serilog, NLog, Microsoft.Extensions.Logging, ZLogger, log4net)
at seven arities (0/1/2/4/8/12/16 properties). Every call sits below
the configured minimum level (`Info` floor, `Debug` call); a well-
designed logger short-circuits before any allocation or template
parse.

Each library is configured with its cheapest no-op terminus: Herald
`WithNullSink`, Serilog `Sink.Null`, NLog `NullTarget`, MEL
`NullLoggerFactory` (and a parallel `MelActiveNullProvider` that
returns `IsEnabled=true` so MEL's filter pipeline is exercised
rather than its no-provider short-circuit), ZLogger
`AddZLoggerStream(Stream.Null)`, log4net `Log4NetNullAppender`. The
`_Varied` rows read one argument from a sixteen-slot string array
indexed by an incrementing per-call counter so the JIT cannot hoist
the level check out of the bench loop; the plain rows reuse a single
constant per call and let the JIT see that the inputs are loop-
invariant.

## Host

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8457)
12th Gen Intel Core i9-12900K, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.204
  [Host] : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

Job=InProcess  Toolchain=InProcessEmitToolchain
```

Herald.OSS 0.7.1 (FileVersion 0.7.1.0,
ProductVersion 0.7.1+cc272f7cc1f2251d7df6f112c2472f8a6e03c322).

## Setup

- Library configuration constructed once in `[GlobalSetup]`; per-call
  bodies do nothing but invoke the logger.
- `MinimumLevel = Information` on every library; bench bodies call
  `Debug` so the rejection path runs.
- `[InProcess]` toolchain so the bench shares the host's JIT and
  memory state (matches every prior comparison snapshot).
- Property values are static constants per arity (EPICS-style at 8/12
  properties, telescope-exposure-style at 16 properties).
- `BenchCategory` and `LevelBoundLogger` handles are cached in
  static fields so the bench reflects how Herald is meant to be used
  (categories hoisted to statics, level-bound handles reused).

## Results

| Method                                               | Mean       | Error     | StdDev    | Median     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------------------------------|-----------:|----------:|----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| Herald: rejected (0 props)                           |  0.1924 ns | 0.0097 ns | 0.0091 ns |  0.1916 ns | 0.013 |    0.00 |      - |        —  |        0.00 |
| Herald: rejected (0 props, interpolated)             |  5.7499 ns | 0.0530 ns | 0.0469 ns |  5.7389 ns | 0.391 |    0.04 |      - |        —  |        0.00 |
| Herald: rejected (0 props, level-bound)              |  7.7487 ns | 0.0920 ns | 0.0815 ns |  7.7310 ns | 0.527 |    0.05 |      - |        —  |        0.00 |
| Serilog: rejected (0 props)                          |  1.0334 ns | 0.0224 ns | 0.0175 ns |  1.0305 ns | 0.070 |    0.01 |      - |        —  |        0.00 |
| NLog: rejected (0 props)                             |  0.0406 ns | 0.0293 ns | 0.0474 ns |  0.0207 ns | 0.003 |    0.00 |      - |        —  |        0.00 |
| MEL: rejected (0 props)                              |  4.3749 ns | 0.1063 ns | 0.0994 ns |  4.3638 ns | 0.298 |    0.03 |      - |        —  |        0.00 |
| MEL: rejected (0 props, active)                      |  6.4058 ns | 0.0929 ns | 0.0776 ns |  6.3940 ns | 0.436 |    0.04 |      - |        —  |        0.00 |
| ZLogger: rejected (0 props)                          |  1.4491 ns | 0.0581 ns | 0.0543 ns |  1.4512 ns | 0.099 |    0.01 |      - |        —  |        0.00 |
| log4net: rejected (0 props)                          |  3.6118 ns | 0.1025 ns | 0.2628 ns |  3.5496 ns | 0.246 |    0.03 |      - |        —  |        0.00 |
| Herald: rejected (1 prop)                            |  5.3624 ns | 0.2454 ns | 0.7236 ns |  5.3183 ns | 0.365 |    0.06 | 0.0041 |      64 B |        0.35 |
| Herald: rejected (1 prop, interpolated)              |  5.6777 ns | 0.0264 ns | 0.0247 ns |  5.6698 ns | 0.386 |    0.04 |      - |        —  |        0.00 |
| Herald: rejected (1 prop, level-bound)               |  6.7895 ns | 0.1209 ns | 0.1131 ns |  6.7810 ns | 0.462 |    0.05 |      - |        —  |        0.00 |
| Herald: rejected (1 prop, typed args, constants)     |  2.7953 ns | 0.0483 ns | 0.0452 ns |  2.7837 ns | 0.190 |    0.02 |      - |        —  |        0.00 |
| Herald: rejected (1 prop, typed args, varied)        |  3.3642 ns | 0.0808 ns | 0.0756 ns |  3.3386 ns | 0.229 |    0.02 |      - |        —  |        0.00 |
| Serilog: rejected (1 prop)                           |  6.7819 ns | 0.1643 ns | 0.3607 ns |  6.6684 ns | 0.462 |    0.05 |      - |        —  |        0.00 |
| Serilog: rejected (1 prop, varied)                   |  6.9079 ns | 0.1421 ns | 0.1110 ns |  6.8849 ns | 0.470 |    0.05 |      - |        —  |        0.00 |
| NLog: rejected (1 prop)                              |  0.0000 ns | 0.0000 ns | 0.0000 ns |  0.0000 ns | 0.000 |    0.00 |      - |        —  |        0.00 |
| NLog: rejected (1 prop, varied)                      |  0.0182 ns | 0.0220 ns | 0.0253 ns |  0.0000 ns | 0.001 |    0.00 |      - |        —  |        0.00 |
| MEL: rejected (1 prop)                               | 12.9227 ns | 0.4086 ns | 1.1855 ns | 12.6559 ns | 0.879 |    0.12 | 0.0020 |      32 B |        0.17 |
| MEL: rejected (1 prop, varied)                       | 12.6541 ns | 0.2800 ns | 0.3833 ns | 12.6162 ns | 0.861 |    0.09 | 0.0020 |      32 B |        0.17 |
| MEL: rejected (1 prop, active)                       | 13.3630 ns | 0.2839 ns | 0.3156 ns | 13.3792 ns | 0.909 |    0.09 | 0.0020 |      32 B |        0.17 |
| MEL: rejected (1 prop, active, varied)               | 13.9333 ns | 0.3101 ns | 0.2900 ns | 13.9181 ns | 0.948 |    0.10 | 0.0020 |      32 B |        0.17 |
| ZLogger: rejected (1 prop)                           |  1.7392 ns | 0.0626 ns | 0.1475 ns |  1.7007 ns | 0.118 |    0.02 |      - |        —  |        0.00 |
| ZLogger: rejected (1 prop, varied)                   |  1.8168 ns | 0.0591 ns | 0.0829 ns |  1.8018 ns | 0.124 |    0.01 |      - |        —  |        0.00 |
| log4net: rejected (1 prop)                           |  3.2107 ns | 0.0994 ns | 0.2851 ns |  3.1393 ns | 0.219 |    0.03 |      - |        —  |        0.00 |
| log4net: rejected (1 prop, varied)                   |  2.9794 ns | 0.0877 ns | 0.0900 ns |  2.9544 ns | 0.203 |    0.02 |      - |        —  |        0.00 |
| Herald: rejected (2 props)                           |  7.6282 ns | 0.3607 ns | 1.0578 ns |  7.6606 ns | 0.519 |    0.09 | 0.0066 |     104 B |        0.57 |
| Herald: rejected (2 props, interpolated)             |  6.1302 ns | 0.0764 ns | 0.0715 ns |  6.0970 ns | 0.417 |    0.04 |      - |        —  |        0.00 |
| Herald: rejected (2 props, level-bound)              |  7.9844 ns | 0.0485 ns | 0.0405 ns |  7.9747 ns | 0.543 |    0.05 |      - |        —  |        0.00 |
| Herald: rejected (2 props, typed args, constants)    |  4.9340 ns | 0.1268 ns | 0.1186 ns |  4.9483 ns | 0.336 |    0.03 |      - |        —  |        0.00 |
| Herald: rejected (2 props, typed args, varied)       |  3.5227 ns | 0.0369 ns | 0.0308 ns |  3.5226 ns | 0.240 |    0.02 |      - |        —  |        0.00 |
| Serilog: rejected (2 props)                          |  5.1674 ns | 0.1224 ns | 0.1022 ns |  5.1313 ns | 0.352 |    0.04 |      - |        —  |        0.00 |
| Serilog: rejected (2 props, varied)                  |  5.8881 ns | 0.1421 ns | 0.1521 ns |  5.8382 ns | 0.401 |    0.04 |      - |        —  |        0.00 |
| NLog: rejected (2 props)                             |  0.0130 ns | 0.0223 ns | 0.0257 ns |  0.0000 ns | 0.001 |    0.00 |      - |        —  |        0.00 |
| NLog: rejected (2 props, varied)                     |  0.0188 ns | 0.0230 ns | 0.0274 ns |  0.0029 ns | 0.001 |    0.00 |      - |        —  |        0.00 |
| MEL: rejected (2 props)                              | 13.6098 ns | 0.2936 ns | 0.4304 ns | 13.5354 ns | 0.926 |    0.10 | 0.0025 |      40 B |        0.22 |
| MEL: rejected (2 props, varied)                      | 14.9182 ns | 0.3283 ns | 0.6085 ns | 14.8252 ns | 1.015 |    0.11 | 0.0025 |      40 B |        0.22 |
| MEL: rejected (2 props, active)                      | 16.4853 ns | 0.3597 ns | 0.4678 ns | 16.4838 ns | 1.122 |    0.12 | 0.0025 |      40 B |        0.22 |
| MEL: rejected (2 props, active, varied)              | 16.5625 ns | 0.3521 ns | 0.3767 ns | 16.4915 ns | 1.127 |    0.12 | 0.0025 |      40 B |        0.22 |
| ZLogger: rejected (2 props)                          |  1.5463 ns | 0.0521 ns | 0.0435 ns |  1.5257 ns | 0.105 |    0.01 |      - |        —  |        0.00 |
| ZLogger: rejected (2 props, varied)                  |  2.0726 ns | 0.0407 ns | 0.0318 ns |  2.0685 ns | 0.141 |    0.01 |      - |        —  |        0.00 |
| log4net: rejected (2 props)                          |  2.9959 ns | 0.0860 ns | 0.0804 ns |  2.9728 ns | 0.204 |    0.02 |      - |        —  |        0.00 |
| log4net: rejected (2 props, varied)                  |  3.1719 ns | 0.0927 ns | 0.0821 ns |  3.1288 ns | 0.216 |    0.02 |      - |        —  |        0.00 |
| Herald: rejected (4 props)                           | 14.8268 ns | 0.4544 ns | 1.3399 ns | 15.1452 ns | 1.009 |    0.14 | 0.0117 |     184 B |        1.00 |
| Herald: rejected (4 props, interpolated)             |  6.9115 ns | 0.0335 ns | 0.0313 ns |  6.9059 ns | 0.470 |    0.05 |      - |        —  |        0.00 |
| Herald: rejected (4 props, level-bound)              |  8.0559 ns | 0.1271 ns | 0.1189 ns |  8.0188 ns | 0.548 |    0.06 |      - |        —  |        0.00 |
| Herald: rejected (4 props, typed args, constants)    |  9.4857 ns | 0.2000 ns | 0.1871 ns |  9.4000 ns | 0.646 |    0.07 |      - |        —  |        0.00 |
| Herald: rejected (4 props, typed args, varied)       | 10.3034 ns | 0.1305 ns | 0.1157 ns | 10.2955 ns | 0.701 |    0.07 |      - |        —  |        0.00 |
| Serilog: rejected (4 props)                          |  7.8376 ns | 0.2880 ns | 0.8493 ns |  8.0412 ns | 0.533 |    0.08 | 0.0036 |      56 B |        0.30 |
| Serilog: rejected (4 props, varied)                  |  9.7937 ns | 0.2909 ns | 0.8578 ns | 10.0627 ns | 0.667 |    0.09 | 0.0036 |      56 B |        0.30 |
| NLog: rejected (4 props)                             |  5.4966 ns | 0.2422 ns | 0.7142 ns |  5.5354 ns | 0.374 |    0.06 | 0.0036 |      56 B |        0.30 |
| NLog: rejected (4 props, varied)                     |  7.5612 ns | 0.2105 ns | 0.6174 ns |  7.5398 ns | 0.515 |    0.07 | 0.0036 |      56 B |        0.30 |
| MEL: rejected (4 props)                              | 20.1753 ns | 0.5933 ns | 1.7494 ns | 19.9528 ns | 1.373 |    0.18 | 0.0035 |      56 B |        0.30 |
| MEL: rejected (4 props, varied)                      | 19.4211 ns | 0.4210 ns | 0.8408 ns | 19.3291 ns | 1.322 |    0.14 | 0.0035 |      56 B |        0.30 |
| MEL: rejected (4 props, active)                      | 19.8366 ns | 0.4067 ns | 0.5832 ns | 19.7000 ns | 1.350 |    0.14 | 0.0035 |      56 B |        0.30 |
| MEL: rejected (4 props, active, varied)              | 21.0676 ns | 0.4506 ns | 0.7773 ns | 20.8781 ns | 1.434 |    0.15 | 0.0035 |      56 B |        0.30 |
| ZLogger: rejected (4 props)                          |  1.8823 ns | 0.0836 ns | 0.2413 ns |  1.8376 ns | 0.128 |    0.02 |      - |        —  |        0.00 |
| ZLogger: rejected (4 props, varied)                  |  1.9477 ns | 0.0706 ns | 0.1718 ns |  1.9203 ns | 0.133 |    0.02 |      - |        —  |        0.00 |
| log4net: rejected (4 props)                          |  9.2612 ns | 0.3245 ns | 0.9567 ns |  9.1119 ns | 0.630 |    0.09 | 0.0036 |      56 B |        0.30 |
| log4net: rejected (4 props, varied)                  |  9.5738 ns | 0.3052 ns | 0.8903 ns |  9.5184 ns | 0.652 |    0.09 | 0.0036 |      56 B |        0.30 |
| Herald: rejected (8 props)                           | 25.3292 ns | 1.5197 ns | 4.4808 ns | 24.8134 ns | 1.724 |    0.35 | 0.0219 |     344 B |        1.87 |
| Herald: rejected (8 props, interpolated)             |  9.4729 ns | 0.2180 ns | 0.4599 ns |  9.3469 ns | 0.645 |    0.07 |      - |        —  |        0.00 |
| Herald: rejected (8 props, level-bound)              |  9.8889 ns | 0.1137 ns | 0.1063 ns |  9.8462 ns | 0.673 |    0.07 |      - |        —  |        0.00 |
| Herald: rejected (8 props, typed args, constants)    | 13.2986 ns | 0.1737 ns | 0.1625 ns | 13.2925 ns | 0.905 |    0.09 |      - |        —  |        0.00 |
| Herald: rejected (8 props, typed args, varied)       | 20.3224 ns | 0.4369 ns | 0.4675 ns | 20.2495 ns | 1.383 |    0.14 |      - |        —  |        0.00 |
| Serilog: rejected (8 props)                          |  9.2842 ns | 0.2160 ns | 0.6198 ns |  9.4740 ns | 0.632 |    0.08 | 0.0056 |      88 B |        0.48 |
| Serilog: rejected (8 props, varied)                  | 10.2620 ns | 0.2627 ns | 0.7747 ns | 10.3483 ns | 0.698 |    0.09 | 0.0056 |      88 B |        0.48 |
| NLog: rejected (8 props)                             |  8.0907 ns | 0.1921 ns | 0.5603 ns |  8.1353 ns | 0.551 |    0.07 | 0.0056 |      88 B |        0.48 |
| NLog: rejected (8 props, varied)                     |  7.4919 ns | 0.3708 ns | 1.0934 ns |  7.5033 ns | 0.510 |    0.09 | 0.0056 |      88 B |        0.48 |
| MEL: rejected (8 props)                              | 24.8736 ns | 0.5257 ns | 0.6836 ns | 24.6042 ns | 1.693 |    0.18 | 0.0056 |      88 B |        0.48 |
| MEL: rejected (8 props, varied)                      | 26.7829 ns | 0.5520 ns | 0.7738 ns | 26.5438 ns | 1.823 |    0.19 | 0.0056 |      88 B |        0.48 |
| MEL: rejected (8 props, active)                      | 27.5153 ns | 0.5756 ns | 0.9618 ns | 27.4490 ns | 1.873 |    0.20 | 0.0056 |      88 B |        0.48 |
| MEL: rejected (8 props, active, varied)              | 36.6331 ns | 0.7561 ns | 1.8115 ns | 36.7239 ns | 2.493 |    0.28 | 0.0056 |      88 B |        0.48 |
| ZLogger: rejected (8 props)                          |  1.6098 ns | 0.0633 ns | 0.1713 ns |  1.5403 ns | 0.110 |    0.02 |      - |        —  |        0.00 |
| ZLogger: rejected (8 props, varied)                  |  1.8657 ns | 0.0668 ns | 0.0769 ns |  1.8466 ns | 0.127 |    0.01 |      - |        —  |        0.00 |
| log4net: rejected (8 props)                          | 11.8891 ns | 0.3591 ns | 1.0589 ns | 12.0151 ns | 0.809 |    0.11 | 0.0056 |      88 B |        0.48 |
| log4net: rejected (8 props, varied)                  | 12.7823 ns | 0.2862 ns | 0.7488 ns | 12.8894 ns | 0.870 |    0.10 | 0.0056 |      88 B |        0.48 |
| Herald: rejected (12 props)                          | 40.6861 ns | 1.5589 ns | 4.5964 ns | 41.4728 ns | 2.769 |    0.42 | 0.0321 |     504 B |        2.74 |
| Herald: rejected (12 props, interpolated)            | 11.1857 ns | 0.2484 ns | 0.3867 ns | 11.0782 ns | 0.761 |    0.08 |      - |        —  |        0.00 |
| Herald: rejected (12 props, level-bound)             | 13.1295 ns | 0.2915 ns | 0.2862 ns | 13.0698 ns | 0.894 |    0.09 |      - |        —  |        0.00 |
| Herald: rejected (12 props, typed args, constants)   | 30.0293 ns | 0.6169 ns | 1.4175 ns | 29.4666 ns | 2.044 |    0.23 |      - |        —  |        0.00 |
| Herald: rejected (12 props, typed args, varied)      | 29.0208 ns | 0.4506 ns | 0.4215 ns | 28.8174 ns | 1.975 |    0.20 |      - |        —  |        0.00 |
| Serilog: rejected (12 props)                         | 12.0489 ns | 0.4245 ns | 1.2383 ns | 12.2057 ns | 0.820 |    0.12 | 0.0076 |     120 B |        0.65 |
| Serilog: rejected (12 props, varied)                 | 12.1829 ns | 0.3746 ns | 1.1047 ns | 12.2671 ns | 0.829 |    0.11 | 0.0076 |     120 B |        0.65 |
| NLog: rejected (12 props)                            |  8.3346 ns | 0.3847 ns | 1.1342 ns |  8.0686 ns | 0.567 |    0.10 | 0.0076 |     120 B |        0.65 |
| NLog: rejected (12 props, varied)                    |  9.2020 ns | 0.4054 ns | 1.1954 ns |  9.2438 ns | 0.626 |    0.10 | 0.0076 |     120 B |        0.65 |
| MEL: rejected (12 props)                             | 37.9714 ns | 0.7879 ns | 0.8757 ns | 37.8159 ns | 2.584 |    0.27 | 0.0076 |     120 B |        0.65 |
| MEL: rejected (12 props, varied)                     | 38.1785 ns | 0.6547 ns | 0.5804 ns | 38.1336 ns | 2.598 |    0.26 | 0.0076 |     120 B |        0.65 |
| MEL: rejected (12 props, active)                     | 36.7115 ns | 0.6029 ns | 0.4707 ns | 36.7579 ns | 2.498 |    0.25 | 0.0076 |     120 B |        0.65 |
| MEL: rejected (12 props, active, varied)             | 38.4527 ns | 0.7986 ns | 1.2666 ns | 38.2819 ns | 2.617 |    0.28 | 0.0076 |     120 B |        0.65 |
| ZLogger: rejected (12 props)                         |  1.8336 ns | 0.0656 ns | 0.0614 ns |  1.8154 ns | 0.125 |    0.01 |      - |        —  |        0.00 |
| ZLogger: rejected (12 props, varied)                 |  2.2398 ns | 0.0659 ns | 0.0784 ns |  2.2173 ns | 0.152 |    0.02 |      - |        —  |        0.00 |
| log4net: rejected (12 props)                         | 13.1692 ns | 0.4743 ns | 1.3685 ns | 13.1242 ns | 0.896 |    0.13 | 0.0076 |     120 B |        0.65 |
| log4net: rejected (12 props, varied)                 | 15.3202 ns | 0.7302 ns | 2.1529 ns | 15.4883 ns | 1.043 |    0.18 | 0.0076 |     120 B |        0.65 |
| Herald: rejected (16 props)                          | 48.9468 ns | 2.8808 ns | 8.4941 ns | 49.3490 ns | 3.331 |    0.67 | 0.0423 |     664 B |        3.61 |
| Herald: rejected (16 props, interpolated)            | 12.1639 ns | 0.0767 ns | 0.0680 ns | 12.1531 ns | 0.828 |    0.08 |      - |        —  |        0.00 |
| Herald: rejected (16 props, level-bound)             | 13.8978 ns | 0.1152 ns | 0.0962 ns | 13.8617 ns | 0.946 |    0.10 |      - |        —  |        0.00 |
| Herald: rejected (16 props, typed args, constants)   | 49.9766 ns | 0.4660 ns | 0.4359 ns | 49.9440 ns | 3.401 |    0.34 |      - |        —  |        0.00 |
| Herald: rejected (16 props, typed args, varied)      | 48.5020 ns | 0.5614 ns | 0.4976 ns | 48.3539 ns | 3.301 |    0.33 |      - |        —  |        0.00 |
| Serilog: rejected (16 props)                         | 13.6858 ns | 0.4752 ns | 1.3936 ns | 13.8011 ns | 0.931 |    0.13 | 0.0097 |     152 B |        0.83 |
| Serilog: rejected (16 props, varied)                 | 14.3600 ns | 0.5158 ns | 1.5209 ns | 14.3841 ns | 0.977 |    0.14 | 0.0097 |     152 B |        0.83 |
| NLog: rejected (16 props)                            | 11.1044 ns | 0.5693 ns | 1.6786 ns | 11.2113 ns | 0.756 |    0.14 | 0.0097 |     152 B |        0.83 |
| NLog: rejected (16 props, varied)                    | 12.2335 ns | 0.5438 ns | 1.6033 ns | 12.4844 ns | 0.833 |    0.14 | 0.0097 |     152 B |        0.83 |
| MEL: rejected (16 props)                             | 49.5216 ns | 0.8786 ns | 1.1112 ns | 49.3848 ns | 3.370 |    0.35 | 0.0097 |     152 B |        0.83 |
| MEL: rejected (16 props, varied)                     | 51.5688 ns | 1.0299 ns | 1.2260 ns | 51.7369 ns | 3.510 |    0.36 | 0.0097 |     152 B |        0.83 |
| MEL: rejected (16 props, active)                     | 52.6239 ns | 1.0694 ns | 1.2315 ns | 52.2777 ns | 3.581 |    0.37 | 0.0097 |     152 B |        0.83 |
| MEL: rejected (16 props, active, varied)             | 53.8921 ns | 1.0649 ns | 0.9440 ns | 53.9780 ns | 3.668 |    0.37 | 0.0097 |     152 B |        0.83 |
| ZLogger: rejected (16 props)                         |  1.5704 ns | 0.0233 ns | 0.0194 ns |  1.5674 ns | 0.107 |    0.01 |      - |        —  |        0.00 |
| ZLogger: rejected (16 props, varied)                 |  1.8975 ns | 0.0671 ns | 0.0824 ns |  1.8697 ns | 0.129 |    0.01 |      - |        —  |        0.00 |
| log4net: rejected (16 props)                         | 15.1922 ns | 0.4555 ns | 1.3430 ns | 15.1685 ns | 1.034 |    0.14 | 0.0097 |     152 B |        0.83 |
| log4net: rejected (16 props, varied)                 | 17.8355 ns | 0.3838 ns | 1.0634 ns | 17.8400 ns | 1.214 |    0.14 | 0.0097 |     152 B |        0.83 |

## Reproduce

```bash
dotnet run \
  --project Modules/Core/benchmarks/competitive/Herald.CompetitiveBenchmarks.csproj \
  --framework net10.0 -c Release --no-build \
  -- --filter "*RejectedCallBenchmarks*" --join
```

Source: `Modules/Core/benchmarks/competitive/Benchmarks/RejectedCallBenchmarks.cs`.

Raw BDN artifacts (log + csv + html + github-flavoured md) preserved at
`E:/tmp/glenn-bench/reject/` from the 2026-05-19 run; the report-github
markdown above is a direct transcription of the BDN-emitted table.
