```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7623/25H2/2025Update/HudsonValley2)
Intel Core i5-14600K 3.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3


```
| Method                       | DataSize | Mean        | Error     | StdDev    | Rank | Gen0   | Gen1   | Allocated |
|----------------------------- |--------- |------------:|----------:|----------:|-----:|-------:|-------:|----------:|
| **&#39;CircularBuffer Write&#39;**       | **64**       |    **230.0 ns** |   **3.65 ns** |   **3.42 ns** |    **1** | **0.6673** |      **-** |   **8.17 KB** |
| &#39;CircularBuffer Write+Peek&#39;  | 64       |    250.7 ns |   5.03 ns |   5.38 ns |    2 | 0.6671 |      - |   8.17 KB |
| &#39;CircularBuffer Write+Drain&#39; | 64       |    228.1 ns |   3.31 ns |   3.09 ns |    1 | 0.6673 |      - |   8.17 KB |
| &#39;QueueBuffer Write&#39;          | 64       |    311.9 ns |   4.18 ns |   3.91 ns |    3 | 0.6685 | 0.0205 |    8.2 KB |
| &#39;QueueBuffer Write+Peek&#39;     | 64       |    317.4 ns |   6.35 ns |   6.52 ns |    3 | 0.6752 | 0.0215 |   8.28 KB |
| &#39;QueueBuffer Write+Drain&#39;    | 64       |    392.0 ns |   6.68 ns |   6.25 ns |    4 | 0.6685 | 0.0205 |    8.2 KB |
| **&#39;CircularBuffer Write&#39;**       | **512**      |    **230.5 ns** |   **3.37 ns** |   **3.15 ns** |    **1** | **0.6673** |      **-** |   **8.17 KB** |
| &#39;CircularBuffer Write+Peek&#39;  | 512      |    247.9 ns |   4.90 ns |   7.33 ns |    2 | 0.6671 |      - |   8.17 KB |
| &#39;CircularBuffer Write+Drain&#39; | 512      |    231.6 ns |   3.47 ns |   3.24 ns |    1 | 0.6673 |      - |   8.17 KB |
| &#39;QueueBuffer Write&#39;          | 512      |    907.3 ns |  14.12 ns |  13.20 ns |    5 | 0.6685 | 0.0200 |    8.2 KB |
| &#39;QueueBuffer Write+Peek&#39;     | 512      |    936.9 ns |  12.90 ns |  12.07 ns |    5 | 0.7114 | 0.0229 |   8.72 KB |
| &#39;QueueBuffer Write+Drain&#39;    | 512      |  1,394.7 ns |  27.36 ns |  28.10 ns |    6 | 0.6676 | 0.0191 |    8.2 KB |
| **&#39;CircularBuffer Write&#39;**       | **4096**     |    **261.2 ns** |   **4.91 ns** |   **7.65 ns** |    **2** | **0.6671** |      **-** |   **8.17 KB** |
| &#39;CircularBuffer Write+Peek&#39;  | 4096     |    301.7 ns |   6.01 ns |   8.22 ns |    3 | 0.6671 |      - |   8.17 KB |
| &#39;CircularBuffer Write+Drain&#39; | 4096     |    262.5 ns |   5.02 ns |   9.05 ns |    2 | 0.6671 |      - |   8.17 KB |
| &#39;QueueBuffer Write&#39;          | 4096     |  9,195.6 ns | 183.87 ns | 204.37 ns |    7 | 0.6638 | 0.0153 |    8.2 KB |
| &#39;QueueBuffer Write+Peek&#39;     | 4096     |  9,541.3 ns | 180.76 ns | 215.18 ns |    7 | 0.9918 | 0.0381 |  12.22 KB |
| &#39;QueueBuffer Write+Drain&#39;    | 4096     | 16,077.1 ns | 203.96 ns | 190.79 ns |    8 | 0.6561 | 0.0153 |    8.2 KB |
