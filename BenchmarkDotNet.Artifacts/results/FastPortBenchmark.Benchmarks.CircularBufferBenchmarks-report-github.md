```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7623/25H2/2025Update/HudsonValley2)
Intel Core i5-14600K 3.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3
  Job-CNUJVU : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3

InvocationCount=1  UnrollFactor=1  

```
| Method                           | Mean        | Error     | StdDev     | Median      | Rank | Allocated |
|--------------------------------- |------------:|----------:|-----------:|------------:|-----:|----------:|
| &#39;Write 64B&#39;                      |    560.0 ns |  65.48 ns |   193.1 ns |    550.0 ns |    1 |         - |
| &#39;Write 1KB&#39;                      |    753.0 ns |  55.48 ns |   163.6 ns |    800.0 ns |    2 |         - |
| &#39;Write 8KB&#39;                      |  1,099.0 ns |  75.38 ns |   221.1 ns |  1,100.0 ns |    3 |         - |
| &#39;Write+Read 64B&#39;                 |    701.0 ns |  61.44 ns |   178.2 ns |    600.0 ns |    2 |         - |
| &#39;Write+Read 1KB&#39;                 |    718.2 ns | 102.45 ns |   300.5 ns |    700.0 ns |    2 |         - |
| &#39;Write x10 (버퍼 확장)&#39;              | 15,659.0 ns | 675.91 ns | 1,992.9 ns | 15,750.0 ns |    5 |  442584 B |
| &#39;TryGetBasePackets (10 packets)&#39; |  3,009.5 ns | 147.84 ns |   397.2 ns |  2,800.0 ns |    4 |    1864 B |
