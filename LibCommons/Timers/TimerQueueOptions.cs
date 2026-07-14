namespace LibCommons.Timers;

// 용도: TimerQueue worker 실행 정책 설정
public sealed class TimerQueueOptions
{
    // 용도: 기본 TimerQueue option
    public static readonly TimerQueueOptions Default = new();

    // 목적: 한 번 깨어났을 때 너무 많은 callback이 worker를 독점하지 않도록 제한
    public int MaxCallbacksPerWake { get; init; } = 1024;

    // 상태: 0 이하 값이 들어와도 worker가 진행 가능한 최소값으로 보정
    public int NormalizedMaxCallbacksPerWake => Math.Max(1, MaxCallbacksPerWake);
}
