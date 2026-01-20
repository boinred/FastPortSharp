using BenchmarkDotNet.Running;
using FastPortBenchmark.Benchmarks;

namespace FastPortBenchmark;

class Program
{
    static void Main(string[] args)
    {
        // 전체 벤치마크 실행
         BenchmarkRunner.Run(typeof(Program).Assembly);

        // 개별 벤치마크 선택 실행
        //var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);
        //switcher.Run(args);

        // 직접 실행 예시:
        // dotnet run -c Release -- --filter *CircularBuffer*
        // dotnet run -c Release -- --filter *Packet*
        // dotnet run -c Release -- --filter *Session*
    }
}
