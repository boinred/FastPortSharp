using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace FastPortBenchmark.Benchmarks;

/// <summary>
/// Lock vs ReaderWriterLockSlim vs .NET 10 Lock 성능 비교
/// </summary>
[MemoryDiagnoser]
[SimpleJob]  // 현재 호스트 런타임 사용 (.NET 10)
[RankColumn]
public class LockBenchmarks
{
    private readonly object _lockObject = new();
    private readonly ReaderWriterLockSlim _rwLock = new();
    private readonly Lock _dotnetLock = new();

    private int _counter = 0;

    [Params(1000, 10000)]
    public int Iterations { get; set; }

    [IterationSetup]
    public void IterationSetup()
    {
        _counter = 0;
    }

    // === 단순 lock 비교 ===

    [Benchmark(Baseline = true, Description = "lock (object)")]
    public int Lock_Object()
    {
        for (int i = 0; i < Iterations; i++)
        {
            lock (_lockObject)
            {
                _counter++;
            }
        }
        return _counter;
    }

    [Benchmark(Description = ".NET 10 Lock")]
    public int Lock_DotNet10()
    {
        for (int i = 0; i < Iterations; i++)
        {
            lock (_dotnetLock)
            {
                _counter++;
            }
        }
        return _counter;
    }

    [Benchmark(Description = ".NET 10 Lock (EnterScope)")]
    public int Lock_DotNet10_EnterScope()
    {
        for (int i = 0; i < Iterations; i++)
        {
            using (_dotnetLock.EnterScope())
            {
                _counter++;
            }
        }
        return _counter;
    }

    [Benchmark(Description = "ReaderWriterLockSlim (Write)")]
    public int RWLock_Write()
    {
        for (int i = 0; i < Iterations; i++)
        {
            _rwLock.EnterWriteLock();
            try
            {
                _counter++;
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }
        return _counter;
    }

    [Benchmark(Description = "ReaderWriterLockSlim (Read)")]
    public int RWLock_Read()
    {
        int sum = 0;
        for (int i = 0; i < Iterations; i++)
        {
            _rwLock.EnterReadLock();
            try
            {
                sum += _counter;
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }
        return sum;
    }

    // === Interlocked 비교 ===

    [Benchmark(Description = "Interlocked.Increment")]
    public int Interlocked_Increment()
    {
        for (int i = 0; i < Iterations; i++)
        {
            Interlocked.Increment(ref _counter);
        }
        return _counter;
    }
}
