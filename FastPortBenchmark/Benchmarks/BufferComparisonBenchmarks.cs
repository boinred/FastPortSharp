using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using LibCommons;

namespace FastPortBenchmark.Benchmarks;

/// <summary>
/// CircularBuffer vs QueueBuffer 비교 벤치마크
/// </summary>
[MemoryDiagnoser]
[SimpleJob]  // 현재 호스트 런타임 사용 (.NET 10)
[RankColumn]
public class BufferComparisonBenchmarks
{
    private byte[] _testData = null!;
    private byte[] _readBuffer = null!;

    [Params(64, 512, 4096)]
    public int DataSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _testData = new byte[DataSize];
        _readBuffer = new byte[DataSize];
        Random.Shared.NextBytes(_testData);
    }

    // === CircularBuffer ===

    [Benchmark(Description = "CircularBuffer Write")]
    public int CircularBuffer_Write()
    {
        using var buffer = new BaseCircularBuffers(8192);
        return buffer.Write(_testData, 0, _testData.Length);
    }

    [Benchmark(Description = "CircularBuffer Write+Peek")]
    public int CircularBuffer_WriteAndPeek()
    {
        using var buffer = new BaseCircularBuffers(8192);
        buffer.Write(_testData, 0, _testData.Length);
        return buffer.Peek(ref _readBuffer);
    }

    [Benchmark(Description = "CircularBuffer Write+Drain")]
    public int CircularBuffer_WriteAndDrain()
    {
        using var buffer = new BaseCircularBuffers(8192);
        buffer.Write(_testData, 0, _testData.Length);
        return buffer.Drain(_testData.Length);
    }

    // === QueueBuffer ===

    [Benchmark(Description = "QueueBuffer Write")]
    public int QueueBuffer_Write()
    {
        using var buffer = new BaseQueueBuffers(8192);
        return buffer.Write(_testData, 0, _testData.Length);
    }

    [Benchmark(Description = "QueueBuffer Write+Peek")]
    public int QueueBuffer_WriteAndPeek()
    {
        using var buffer = new BaseQueueBuffers(8192);
        buffer.Write(_testData, 0, _testData.Length);
        return buffer.Peek(ref _readBuffer);
    }

    [Benchmark(Description = "QueueBuffer Write+Drain")]
    public int QueueBuffer_WriteAndDrain()
    {
        using var buffer = new BaseQueueBuffers(8192);
        buffer.Write(_testData, 0, _testData.Length);
        return buffer.Drain(_testData.Length);
    }
}
