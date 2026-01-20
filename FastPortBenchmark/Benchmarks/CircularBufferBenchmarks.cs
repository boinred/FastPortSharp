using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using LibCommons;

namespace FastPortBenchmark.Benchmarks;

/// <summary>
/// CircularBuffer vs ArrayPoolCircularBuffer 성능 벤치마크
/// - Write/Read 성능
/// - 다양한 데이터 크기별 성능
/// - 버퍼 확장 시 성능 (ArrayPool 효과 측정)
/// </summary>
[MemoryDiagnoser]
[SimpleJob]  // 현재 호스트 런타임 사용 (.NET 10)
[RankColumn]
public class CircularBufferBenchmarks
{
    private BaseCircularBuffers _buffer = null!;
    private ArrayPoolCircularBuffers _arrayPoolBuffer = null!;
    private byte[] _smallData = null!;   // 64 bytes
    private byte[] _mediumData = null!;  // 1KB
    private byte[] _largeData = null!;   // 8KB
    private byte[] _readBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new BaseCircularBuffers(8192);
        _arrayPoolBuffer = new ArrayPoolCircularBuffers(8192);
        _smallData = new byte[64];
        _mediumData = new byte[1024];
        _largeData = new byte[8192];
        _readBuffer = new byte[8192];

        // 랜덤 데이터 초기화
        Random.Shared.NextBytes(_smallData);
        Random.Shared.NextBytes(_mediumData);
        Random.Shared.NextBytes(_largeData);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _buffer.Dispose();
        _arrayPoolBuffer.Dispose();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // 각 반복마다 버퍼 초기화
        _buffer.Dispose();
        _arrayPoolBuffer.Dispose();
        _buffer = new BaseCircularBuffers(8192);
        _arrayPoolBuffer = new ArrayPoolCircularBuffers(8192);
    }

    // === 기존 CircularBuffer Write 벤치마크 ===

    [Benchmark(Description = "Write 64B")]
    public int Write_Small() => _buffer.Write(_smallData, 0, _smallData.Length);

    [Benchmark(Description = "Write 1KB")]
    public int Write_Medium() => _buffer.Write(_mediumData, 0, _mediumData.Length);

    [Benchmark(Description = "Write 8KB")]
    public int Write_Large() => _buffer.Write(_largeData, 0, _largeData.Length);

    // === 기존 Write + Read 벤치마크 ===

    [Benchmark(Description = "Write+Read 64B")]
    public int WriteRead_Small()
    {
        _buffer.Write(_smallData, 0, _smallData.Length);
        return _buffer.Peek(ref _readBuffer);
    }

    [Benchmark(Description = "Write+Read 1KB")]
    public int WriteRead_Medium()
    {
        _buffer.Write(_mediumData, 0, _mediumData.Length);
        return _buffer.Peek(ref _readBuffer);
    }

    // === 버퍼 확장 테스트 (핵심 비교) ===

    [Benchmark(Description = "Write x10 (버퍼 확장) - 기존")]
    public int Write_Multiple_WithExpansion()
    {
        int total = 0;
        for (int i = 0; i < 10; i++)
        {
            total += _buffer.Write(_largeData, 0, _largeData.Length);
        }
        return total;
    }

    [Benchmark(Description = "Write x10 (버퍼 확장) - ArrayPool")]
    public int Write_Multiple_WithExpansion_ArrayPool()
    {
        int total = 0;
        for (int i = 0; i < 10; i++)
        {
            total += _arrayPoolBuffer.Write(_largeData, 0, _largeData.Length);
        }
        return total;
    }

    // === 패킷 파싱 벤치마크 ===

    [Benchmark(Description = "TryGetBasePackets (10 packets) - 기존")]
    public int TryGetPackets()
    {
        // 패킷 형식: [2바이트 크기][데이터]
        byte[] packetData = new byte[66]; // 2 + 64
        BitConverter.GetBytes((ushort)66).CopyTo(packetData, 0);
        _smallData.CopyTo(packetData, 2);

        for (int i = 0; i < 10; i++)
        {
            _buffer.Write(packetData, 0, packetData.Length);
        }

        _buffer.TryGetBasePackets(out var packets);
        return packets.Count;
    }

    [Benchmark(Description = "TryGetBasePackets (10 packets) - ArrayPool")]
    public int TryGetPackets_ArrayPool()
    {
        // 패킷 형식: [2바이트 크기][데이터]
        byte[] packetData = new byte[66]; // 2 + 64
        BitConverter.GetBytes((ushort)66).CopyTo(packetData, 0);
        _smallData.CopyTo(packetData, 2);

        for (int i = 0; i < 10; i++)
        {
            _arrayPoolBuffer.Write(packetData, 0, packetData.Length);
        }

        _arrayPoolBuffer.TryGetBasePackets(out var packets);
        return packets.Count;
    }
}
