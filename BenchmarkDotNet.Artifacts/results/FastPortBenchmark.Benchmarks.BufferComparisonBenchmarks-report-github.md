```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7623/25H2/2025Update/HudsonValley2)
Intel Core i5-14600K 3.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.101
  [Host]   : .NET 9.0.11 (9.0.11, 9.0.1125.51716), X64 RyuJIT x86-64-v3
  .NET 9.0 : .NET 9.0.11 (9.0.11, 9.0.1125.51716), X64 RyuJIT x86-64-v3

Job=.NET 9.0  Runtime=.NET 9.0  

```
| Method                       | DataSize | Mean       | Error    | StdDev   | Rank | Gen0   | Gen1   | Allocated |
|----------------------------- |--------- |-----------:|---------:|---------:|-----:|-------:|-------:|----------:|
| **&#39;CircularBuffer Write&#39;**       | **64**       |   **244.3 ns** |  **4.08 ns** |  **3.82 ns** |    **1** | **0.6673** |      **-** |   **8.17 KB** |
| &#39;CircularBuffer Write+Peek&#39;  | 64       |   255.4 ns |  4.92 ns |  4.60 ns |    1 | 0.6671 |      - |   8.17 KB |
| &#39;CircularBuffer Write+Drain&#39; | 64       |   236.3 ns |  4.26 ns |  3.78 ns |    1 | 0.6673 |      - |   8.17 KB |
| &#39;QueueBuffer Write&#39;          | 64       |   315.1 ns |  4.58 ns |  4.28 ns |    2 | 0.6685 | 0.0205 |    8.2 KB |
| &#39;QueueBuffer Write+Peek&#39;     | 64       |   339.9 ns |  5.27 ns |  4.93 ns |    3 | 0.6752 | 0.0215 |   8.28 KB |
| &#39;QueueBuffer Write+Drain&#39;    | 64       |   386.0 ns |  6.50 ns |  6.08 ns |    4 | 0.6685 | 0.0205 |    8.2 KB |
| **&#39;CircularBuffer Write&#39;**       | **512**      |   **239.6 ns** |  **4.44 ns** |  **4.15 ns** |    **1** | **0.6673** |      **-** |   **8.17 KB** |
| &#39;CircularBuffer Write+Peek&#39;  | 512      |   262.5 ns |  5.06 ns |  5.62 ns |    1 | 0.6671 |      - |   8.17 KB |
| &#39;CircularBuffer Write+Drain&#39; | 512      |   236.0 ns |  2.21 ns |  2.06 ns |    1 | 0.6673 |      - |   8.17 KB |
| &#39;QueueBuffer Write&#39;          | 512      |   913.4 ns | 11.92 ns | 10.57 ns |    5 | 0.6685 | 0.0200 |    8.2 KB |
| &#39;QueueBuffer Write+Peek&#39;     | 512      |   956.0 ns |  9.99 ns |  9.35 ns |    6 | 0.7114 | 0.0229 |   8.72 KB |
| &#39;QueueBuffer Write+Drain&#39;    | 512      | 1,277.5 ns | 24.80 ns | 26.53 ns |    7 | 0.6676 | 0.0191 |    8.2 KB |
| **&#39;CircularBuffer Write&#39;**       | **4096**     |   **267.2 ns** |  **5.31 ns** |  **5.45 ns** |    **1** | **0.6671** |      **-** |   **8.17 KB** |
| &#39;CircularBuffer Write+Peek&#39;  | 4096     |   319.0 ns |  5.59 ns |  5.23 ns |    2 | 0.6671 |      - |   8.17 KB |
| &#39;CircularBuffer Write+Drain&#39; | 4096     |   269.7 ns |  3.93 ns |  3.68 ns |    1 | 0.6671 |      - |   8.17 KB |
| &#39;QueueBuffer Write&#39;          | 4096     | 5,546.9 ns | 21.18 ns | 18.77 ns |    8 | 0.6638 | 0.0153 |    8.2 KB |
| &#39;QueueBuffer Write+Peek&#39;     | 4096     | 5,754.7 ns | 48.18 ns | 45.07 ns |    9 | 0.9918 | 0.0381 |  12.22 KB |
| &#39;QueueBuffer Write+Drain&#39;    | 4096     | 8,282.3 ns | 65.95 ns | 61.69 ns |   10 | 0.6561 | 0.0153 |    8.2 KB |
