using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System.Buffers;
using System.Buffers.Binary;

namespace FastPortBenchmark.Benchmarks;

/// <summary>
/// 최적화 기법 비교 벤치마크
/// - ArrayPool vs new byte[]
/// - Span vs Array
/// - BinaryPrimitives vs BitConverter
/// </summary>
[MemoryDiagnoser]
[SimpleJob]  // 현재 호스트 런타임 사용 (.NET 10)
[RankColumn]
public class OptimizationTechniquesBenchmarks
{
    private byte[] _sourceData = null!;

    [Params(256, 1024, 4096)]
    public int BufferSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _sourceData = new byte[BufferSize];
        Random.Shared.NextBytes(_sourceData);
    }

    // === 배열 할당 비교 ===

    [Benchmark(Baseline = true, Description = "new byte[] 할당")]
    public byte[] Allocation_NewArray()
    {
        byte[] buffer = new byte[BufferSize];
        _sourceData.CopyTo(buffer, 0);
        return buffer;
    }

    [Benchmark(Description = "ArrayPool.Rent")]
    public byte[] Allocation_ArrayPool()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        _sourceData.CopyTo(buffer, 0);
        ArrayPool<byte>.Shared.Return(buffer);
        return buffer;
    }

    // === 복사 방식 비교 ===

    [Benchmark(Description = "Buffer.BlockCopy")]
    public byte[] Copy_BlockCopy()
    {
        byte[] dest = new byte[BufferSize];
        Buffer.BlockCopy(_sourceData, 0, dest, 0, BufferSize);
        return dest;
    }

    [Benchmark(Description = "Array.Copy")]
    public byte[] Copy_ArrayCopy()
    {
        byte[] dest = new byte[BufferSize];
        Array.Copy(_sourceData, dest, BufferSize);
        return dest;
    }

    [Benchmark(Description = "Span.CopyTo")]
    public byte[] Copy_SpanCopyTo()
    {
        byte[] dest = new byte[BufferSize];
        _sourceData.AsSpan().CopyTo(dest);
        return dest;
    }

    // === 정수 변환 비교 ===

    [Benchmark(Description = "BitConverter.GetBytes")]
    public byte[] IntConvert_BitConverter()
    {
        return BitConverter.GetBytes(12345678);
    }

    [Benchmark(Description = "BinaryPrimitives (stackalloc)")]
    public int IntConvert_BinaryPrimitives()
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, 12345678);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    // === 패킷 헤더 읽기 비교 ===

    [Benchmark(Description = "BitConverter.ToUInt16")]
    public ushort ReadHeader_BitConverter()
    {
        byte[] header = [0x10, 0x27]; // 10000
        return BitConverter.ToUInt16(header, 0);
    }

    [Benchmark(Description = "BinaryPrimitives.ReadUInt16")]
    public ushort ReadHeader_BinaryPrimitives()
    {
        ReadOnlySpan<byte> header = stackalloc byte[] { 0x10, 0x27 };
        return BinaryPrimitives.ReadUInt16LittleEndian(header);
    }
}
