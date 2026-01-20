```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7623/25H2/2025Update/HudsonValley2)
Intel Core i5-14600K 3.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.101
  [Host]   : .NET 9.0.11 (9.0.11, 9.0.1125.51716), X64 RyuJIT x86-64-v3
  .NET 9.0 : .NET 9.0.11 (9.0.11, 9.0.1125.51716), X64 RyuJIT x86-64-v3

Job=.NET 9.0  Runtime=.NET 9.0  InvocationCount=1  
UnrollFactor=1  

```
| Method                         | Iterations | Mean       | Error     | StdDev    | Median     | Ratio | RatioSD | Rank | Allocated | Alloc Ratio |
|------------------------------- |----------- |-----------:|----------:|----------:|-----------:|------:|--------:|-----:|----------:|------------:|
| **&#39;lock (object)&#39;**                | **1000**       |  **17.223 μs** | **0.3454 μs** | **0.8342 μs** |  **17.100 μs** |  **1.00** |    **0.07** |    **3** |         **-** |          **NA** |
| &#39;.NET 9 Lock&#39;                  | 1000       |  15.801 μs | 0.3174 μs | 0.7786 μs |  15.600 μs |  0.92 |    0.06 |    2 |         - |          NA |
| &#39;ReaderWriterLockSlim (Write)&#39; | 1000       |  19.535 μs | 0.3926 μs | 1.0480 μs |  19.300 μs |  1.14 |    0.08 |    4 |         - |          NA |
| &#39;ReaderWriterLockSlim (Read)&#39;  | 1000       |  20.324 μs | 0.4040 μs | 0.8069 μs |  19.900 μs |  1.18 |    0.07 |    4 |         - |          NA |
| Interlocked.Increment          | 1000       |   6.736 μs | 0.1762 μs | 0.4941 μs |   6.600 μs |  0.39 |    0.03 |    1 |         - |          NA |
|                                |            |            |           |           |            |       |         |      |           |             |
| **&#39;lock (object)&#39;**                | **10000**      | **138.614 μs** | **0.8370 μs** | **0.7420 μs** | **138.650 μs** |  **1.00** |    **0.01** |    **3** |         **-** |          **NA** |
| &#39;.NET 9 Lock&#39;                  | 10000      | 128.908 μs | 0.3298 μs | 0.2575 μs | 128.900 μs |  0.93 |    0.01 |    2 |         - |          NA |
| &#39;ReaderWriterLockSlim (Write)&#39; | 10000      | 151.169 μs | 0.7437 μs | 0.6210 μs | 151.200 μs |  1.09 |    0.01 |    4 |         - |          NA |
| &#39;ReaderWriterLockSlim (Read)&#39;  | 10000      | 158.814 μs | 1.0166 μs | 0.9012 μs | 158.850 μs |  1.15 |    0.01 |    5 |         - |          NA |
| Interlocked.Increment          | 10000      |  37.986 μs | 0.4124 μs | 0.3655 μs |  38.050 μs |  0.27 |    0.00 |    1 |         - |          NA |
