```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7623/25H2/2025Update/HudsonValley2)
Intel Core i5-14600K 3.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3


```
| Method                          | Mean        | Error     | StdDev    | Rank | Gen0   | Gen1   | Allocated |
|-------------------------------- |------------:|----------:|----------:|-----:|-------:|-------:|----------:|
| &#39;Create Packet 64B&#39;             |    15.91 ns |  0.351 ns |  0.567 ns |    1 | 0.0172 |      - |     216 B |
| &#39;Create Packet 256B&#39;            |    21.86 ns |  0.205 ns |  0.192 ns |    3 | 0.0325 | 0.0000 |     408 B |
| &#39;Create Packet 1KB&#39;             |    45.34 ns |  0.920 ns |  0.860 ns |    4 | 0.0937 |      - |    1176 B |
| &#39;Create 100 Packets (64B each)&#39; | 1,497.48 ns | 28.682 ns | 26.829 ns |    5 | 1.7204 |      - |   21600 B |
| &#39;Access Packet Data&#39;            |    19.05 ns |  0.332 ns |  0.310 ns |    2 | 0.0300 |      - |     376 B |
