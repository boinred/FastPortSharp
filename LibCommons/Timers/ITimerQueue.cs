namespace LibCommons.Timers;

// 용도: application-wide delayed/periodic callback scheduler abstraction
public interface ITimerQueue : IDisposable, IAsyncDisposable
{
    // 용도: delay 이후 한 번 실행되는 callback 등록
    ITimerQueueHandle Schedule(TimeSpan delay, Action callback);

    // 용도: interval마다 반복 실행되는 callback 등록
    ITimerQueueHandle SchedulePeriodic(TimeSpan interval, Action callback);
}

// 용도: 예약된 timer callback 취소 handle
public interface ITimerQueueHandle : IDisposable
{
    // 상태: 취소 요청 여부
    bool IsCanceled { get; }

    // 용도: 아직 due 되지 않은 callback 취소
    bool Cancel();
}
