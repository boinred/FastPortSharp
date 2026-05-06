using System.Diagnostics;

namespace LibCommons.Timers;

// 용도: wall-clock 변경 영향을 받지 않는 timer/idle 측정용 시간 공급자
public interface IMonotonicTimeSource
{
    // 용도: 현재 monotonic timestamp 조회
    long GetTimestamp();

    // 용도: 두 monotonic timestamp 사이의 경과 시간 계산
    TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp);

    // 용도: delay 이후의 due timestamp 계산
    long Add(long timestamp, TimeSpan delay);

    // 용도: due timestamp까지 남은 대기 시간 계산
    TimeSpan GetDelay(long nowTimestamp, long dueTimestamp);
}

// 용도: Stopwatch 기반 production monotonic time source
public sealed class StopwatchMonotonicTimeSource : IMonotonicTimeSource
{
    // 용도: DI 없이도 재사용 가능한 stateless singleton instance
    public static readonly StopwatchMonotonicTimeSource Instance = new();

    // 목적: 외부 생성은 허용하되 singleton 사용을 기본 경로로 유지
    public StopwatchMonotonicTimeSource()
    {
    }

    // 용도: Stopwatch timestamp 조회
    public long GetTimestamp()
    {
        return Stopwatch.GetTimestamp();
    }

    // 용도: Stopwatch frequency 기준 경과 시간 계산
    public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp)
    {
        long elapsedTicks = Math.Max(0, endTimestamp - startTimestamp);
        return TimeSpan.FromSeconds(elapsedTicks / (double)Stopwatch.Frequency);
    }

    // 용도: TimeSpan을 Stopwatch tick으로 변환해 due timestamp 계산
    public long Add(long timestamp, TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must be zero or positive.");
        }

        long delayTicks = checked((long)Math.Ceiling(delay.TotalSeconds * Stopwatch.Frequency));
        return checked(timestamp + delayTicks);
    }

    // 용도: due까지 남은 시간을 TimeSpan으로 변환
    public TimeSpan GetDelay(long nowTimestamp, long dueTimestamp)
    {
        if (dueTimestamp <= nowTimestamp)
        {
            return TimeSpan.Zero;
        }

        long remainingTicks = dueTimestamp - nowTimestamp;
        return TimeSpan.FromSeconds(remainingTicks / (double)Stopwatch.Frequency);
    }
}
