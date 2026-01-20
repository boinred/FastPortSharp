using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using LibCommons;

namespace FastPortBenchmark.Benchmarks;

/// <summary>
/// BasePacket 생성 및 처리 성능 벤치마크
/// </summary>
[MemoryDiagnoser]
[SimpleJob]  // 현재 호스트 런타임 사용 (.NET 10)
[RankColumn]
public class PacketBenchmarks
{
    private byte[] _smallPacketBuffer = null!;   // 64 + 2 bytes
    private byte[] _mediumPacketBuffer = null!;  // 256 + 2 bytes
    private byte[] _largePacketBuffer = null!;   // 1024 + 2 bytes

    [GlobalSetup]
    public void Setup()
    {
        // 패킷 형식: [2바이트 크기][데이터]
        _smallPacketBuffer = CreatePacketBuffer(64);
        _mediumPacketBuffer = CreatePacketBuffer(256);
        _largePacketBuffer = CreatePacketBuffer(1024);
    }

    private static byte[] CreatePacketBuffer(int dataSize)
    {
        int packetSize = dataSize + BasePacket.HeaderSize;
        byte[] buffer = new byte[packetSize];
        
        // 헤더에 패킷 크기 기록
        BitConverter.GetBytes((ushort)packetSize).CopyTo(buffer, 0);
        
        // 데이터 부분 랜덤 채우기
        Random.Shared.NextBytes(buffer.AsSpan(BasePacket.HeaderSize));
        
        return buffer;
    }

    // === Packet 생성 벤치마크 ===

    [Benchmark(Description = "Create Packet 64B")]
    public BasePacket CreatePacket_Small() => new BasePacket(_smallPacketBuffer.Length, _smallPacketBuffer);

    [Benchmark(Description = "Create Packet 256B")]
    public BasePacket CreatePacket_Medium() => new BasePacket(_mediumPacketBuffer.Length, _mediumPacketBuffer);

    [Benchmark(Description = "Create Packet 1KB")]
    public BasePacket CreatePacket_Large() => new BasePacket(_largePacketBuffer.Length, _largePacketBuffer);

    // === Packet 대량 생성 벤치마크 ===

    [Benchmark(Description = "Create 100 Packets (64B each)")]
    public int CreatePackets_Batch()
    {
        int count = 0;
        for (int i = 0; i < 100; i++)
        {
            var packet = new BasePacket(_smallPacketBuffer.Length, _smallPacketBuffer);
            count += packet.DataSize;
        }
        return count;
    }

    // === Data 접근 벤치마크 ===

    [Benchmark(Description = "Access Packet Data")]
    public int AccessPacketData()
    {
        var packet = new BasePacket(_mediumPacketBuffer.Length, _mediumPacketBuffer);
        var data = packet.Data;
        return data.Length;
    }
}
