using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;

namespace FastPortBenchmark.Benchmarks;

/// <summary>
/// Channel vs BufferBlock (TPL Dataflow) 성능 비교
/// </summary>
[MemoryDiagnoser]
[SimpleJob]  // 현재 호스트 런타임 사용 (.NET 10)
[RankColumn]
public class ChannelVsDataflowBenchmarks
{
    private record TestMessage(int Id, byte[] Data);

    [Params(100, 1000)]
    public int MessageCount { get; set; }

    // === Channel (Bounded) ===

    [Benchmark(Description = "Channel<T> Bounded")]
    public async Task<int> Channel_Bounded()
    {
        var channel = Channel.CreateBounded<TestMessage>(new BoundedChannelOptions(1000)
        {
            SingleWriter = true,
            SingleReader = true
        });

        var writer = channel.Writer;
        var reader = channel.Reader;

        // Producer
        var producerTask = Task.Run(async () =>
        {
            for (int i = 0; i < MessageCount; i++)
            {
                await writer.WriteAsync(new TestMessage(i, new byte[64]));
            }
            writer.Complete();
        });

        // Consumer
        int received = 0;
        await foreach (var msg in reader.ReadAllAsync())
        {
            received++;
        }

        await producerTask;
        return received;
    }

    // === Channel (Unbounded) ===

    [Benchmark(Description = "Channel<T> Unbounded")]
    public async Task<int> Channel_Unbounded()
    {
        var channel = Channel.CreateUnbounded<TestMessage>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        var writer = channel.Writer;
        var reader = channel.Reader;

        // Producer
        var producerTask = Task.Run(async () =>
        {
            for (int i = 0; i < MessageCount; i++)
            {
                await writer.WriteAsync(new TestMessage(i, new byte[64]));
            }
            writer.Complete();
        });

        // Consumer
        int received = 0;
        await foreach (var msg in reader.ReadAllAsync())
        {
            received++;
        }

        await producerTask;
        return received;
    }

    // === BufferBlock (TPL Dataflow) ===

    [Benchmark(Description = "BufferBlock<T>")]
    public async Task<int> BufferBlock_Default()
    {
        var block = new BufferBlock<TestMessage>(new DataflowBlockOptions
        {
            BoundedCapacity = 1000
        });

        // Producer
        var producerTask = Task.Run(async () =>
        {
            for (int i = 0; i < MessageCount; i++)
            {
                await block.SendAsync(new TestMessage(i, new byte[64]));
            }
            block.Complete();
        });

        // Consumer
        int received = 0;
        while (await block.OutputAvailableAsync())
        {
            var msg = await block.ReceiveAsync();
            received++;
        }

        await producerTask;
        return received;
    }

    // === 동기 쓰기 성능 비교 ===

    [Benchmark(Description = "Channel TryWrite (sync)")]
    public int Channel_TryWrite_Sync()
    {
        var channel = Channel.CreateUnbounded<TestMessage>();
        var writer = channel.Writer;

        int written = 0;
        for (int i = 0; i < MessageCount; i++)
        {
            if (writer.TryWrite(new TestMessage(i, new byte[64])))
                written++;
        }
        return written;
    }

    [Benchmark(Description = "BufferBlock Post (sync)")]
    public int BufferBlock_Post_Sync()
    {
        var block = new BufferBlock<TestMessage>();

        int posted = 0;
        for (int i = 0; i < MessageCount; i++)
        {
            if (block.Post(new TestMessage(i, new byte[64])))
                posted++;
        }
        return posted;
    }
}
