```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7623/25H2/2025Update/HudsonValley2)
Intel Core i5-14600K 3.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.101
  [Host]   : .NET 9.0.11 (9.0.11, 9.0.1125.51716), X64 RyuJIT x86-64-v3
  .NET 9.0 : .NET 9.0.11 (9.0.11, 9.0.1125.51716), X64 RyuJIT x86-64-v3

Job=.NET 9.0  Runtime=.NET 9.0  InvocationCount=1  
UnrollFactor=1  

```
| Method                           | Mean        | Error     | StdDev    | Median      | Rank | Allocated |
|--------------------------------- |------------:|----------:|----------:|------------:|-----:|----------:|
| &#39;Write 64B&#39;                      |    360.2 ns |  19.54 ns |  55.44 ns |    400.0 ns |    1 |         - |
| &#39;Write 1KB&#39;                      |    430.4 ns |  29.61 ns |  83.52 ns |    400.0 ns |    2 |         - |
| &#39;Write 8KB&#39;                      |    669.9 ns |  23.12 ns |  65.58 ns |    700.0 ns |    4 |         - |
| &#39;Write+Read 64B&#39;                 |    500.0 ns |   0.00 ns |   0.00 ns |    500.0 ns |    3 |         - |
| &#39;Write+Read 1KB&#39;                 |    707.0 ns |  61.59 ns | 181.61 ns |    700.0 ns |    4 |         - |
| &#39;Write x10 (버퍼 확장)&#39;              | 13,539.3 ns | 270.61 ns | 388.10 ns | 13,500.0 ns |    6 |  442584 B |
| &#39;TryGetBasePackets (10 packets)&#39; |  3,476.4 ns | 112.16 ns | 310.80 ns |  3,400.0 ns |    5 |    1864 B |
