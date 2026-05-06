using System.Collections.Generic;

namespace LibCommons.Timers;

// 용도: process/application 범위에서 DI singleton으로 재사용 가능한 delayed/periodic scheduler
public sealed class TimerQueue : ITimerQueue
{
    // 동기화: priority queue mutation 보호
    private readonly object m_Gate = new();

    // 상태: due timestamp 기준 min-heap
    private readonly PriorityQueue<TimerQueueEntry, long> m_Entries = new();

    // 신호: 새 earliest timer 등록 또는 cancel 시 worker wake-up
    private readonly SemaphoreSlim m_Signal = new(0);

    // 수명: worker cancellation 제어
    private readonly CancellationTokenSource m_CancellationTokenSource = new();

    // 용도: wall-clock 변경과 무관한 due 계산
    private readonly IMonotonicTimeSource m_TimeSource;

    // 정책: 한 wake에서 처리할 callback 수 제한
    private readonly TimerQueueOptions m_Options;

    // 실행: TimerQueue background worker
    private readonly Task m_WorkerTask;

    // 상태: timer entry id 생성
    private long m_NextEntryId;

    // 상태: dispose 이후 schedule 방지
    private int m_Disposed;

    // 지표: 실행 완료 callback 수
    private long m_ExecutedCallbackCount;

    // 지표: exception 발생 callback 수
    private long m_FailedCallbackCount;

    // 편의: DI 없는 코드에서 기본 TimerQueue 생성
    public TimerQueue()
        : this(StopwatchMonotonicTimeSource.Instance, TimerQueueOptions.Default)
    {
    }

    // 편의: 테스트 또는 DI에서 time source만 교체
    public TimerQueue(IMonotonicTimeSource timeSource)
        : this(timeSource, TimerQueueOptions.Default)
    {
    }

    // 용도: DI에서 time source/options를 명시적으로 주입
    public TimerQueue(IMonotonicTimeSource timeSource, TimerQueueOptions options)
    {
        m_TimeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));
        m_Options = options ?? TimerQueueOptions.Default;
        m_WorkerTask = Task.Run(RunWorkerAsync);
    }

    // 지표: callback 실행 수 snapshot
    public long ExecutedCallbackCount => Interlocked.Read(ref m_ExecutedCallbackCount);

    // 지표: callback exception 수 snapshot
    public long FailedCallbackCount => Interlocked.Read(ref m_FailedCallbackCount);

    // 상태: dispose 완료 또는 진행 여부
    public bool IsDisposed => Volatile.Read(ref m_Disposed) == 1;

    // 용도: one-shot timer 등록
    public ITimerQueueHandle Schedule(TimeSpan delay, Action callback)
    {
        return ScheduleCore(delay, period: null, callback);
    }

    // 용도: periodic timer 등록
    public ITimerQueueHandle SchedulePeriodic(TimeSpan interval, Action callback)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be greater than zero.");
        }

        return ScheduleCore(interval, interval, callback);
    }

    // 용도: 비동기 dispose를 동기 dispose 경로에서도 지원
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // 용도: host shutdown 시 worker 중단 및 pending timer 제거
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref m_Disposed, 1) != 0)
        {
            return;
        }

        m_CancellationTokenSource.Cancel();
        SignalWorker();

        lock (m_Gate)
        {
            m_Entries.Clear();
        }

        try
        {
            await m_WorkerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 상태: shutdown cancellation은 정상 종료 경로
        }

        m_CancellationTokenSource.Dispose();
        m_Signal.Dispose();
    }

    // 용도: one-shot/periodic 공통 등록 처리
    private ITimerQueueHandle ScheduleCore(TimeSpan delay, TimeSpan? period, Action callback)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must be zero or positive.");
        }

        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfDisposed();

        long id = Interlocked.Increment(ref m_NextEntryId);
        long dueTimestamp = m_TimeSource.Add(m_TimeSource.GetTimestamp(), delay);
        var entry = new TimerQueueEntry(id, dueTimestamp, period, callback);
        var handle = new TimerQueueHandle(entry, SignalWorker);

        lock (m_Gate)
        {
            ThrowIfDisposed();
            m_Entries.Enqueue(entry, entry.DueTimestamp);
        }

        SignalWorker();
        return handle;
    }

    // 실행: due timer를 순차 처리하고 다음 due 또는 signal까지 대기
    private async Task RunWorkerAsync()
    {
        CancellationToken cancellationToken = m_CancellationTokenSource.Token;
        int callbacksThisWake = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (TryDequeueDueEntry(out TimerQueueEntry? entry) && entry is not null)
            {
                ExecuteEntry(entry);
                callbacksThisWake++;

                if (callbacksThisWake >= m_Options.NormalizedMaxCallbacksPerWake)
                {
                    callbacksThisWake = 0;
                    await Task.Yield();
                }

                continue;
            }

            callbacksThisWake = 0;
            TimeSpan? waitDelay = GetDelayUntilNextDue();
            await WaitForNextSignalAsync(waitDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    // 용도: 현재 due 된 entry 하나를 queue에서 제거
    private bool TryDequeueDueEntry(out TimerQueueEntry? entry)
    {
        entry = null;
        lock (m_Gate)
        {
            RemoveCanceledEntriesFromHead();
            if (!m_Entries.TryPeek(out TimerQueueEntry? candidate, out long dueTimestamp))
            {
                return false;
            }

            long nowTimestamp = m_TimeSource.GetTimestamp();
            if (dueTimestamp > nowTimestamp)
            {
                return false;
            }

            m_Entries.Dequeue();
            entry = candidate;
            return true;
        }
    }

    // 용도: 다음 due까지 남은 delay 조회
    private TimeSpan? GetDelayUntilNextDue()
    {
        lock (m_Gate)
        {
            RemoveCanceledEntriesFromHead();
            if (!m_Entries.TryPeek(out _, out long dueTimestamp))
            {
                return null;
            }

            return m_TimeSource.GetDelay(m_TimeSource.GetTimestamp(), dueTimestamp);
        }
    }

    // 용도: lazy cancel된 head entries 제거
    private void RemoveCanceledEntriesFromHead()
    {
        while (m_Entries.TryPeek(out TimerQueueEntry? entry, out _) && entry.IsCanceled)
        {
            m_Entries.Dequeue();
        }
    }

    // 용도: callback 실행 및 periodic 재등록
    private void ExecuteEntry(TimerQueueEntry entry)
    {
        if (entry.IsCanceled || IsDisposed)
        {
            return;
        }

        try
        {
            entry.Callback();
            Interlocked.Increment(ref m_ExecutedCallbackCount);
        }
        catch
        {
            Interlocked.Increment(ref m_FailedCallbackCount);
        }

        if (entry.Period is null || entry.IsCanceled || IsDisposed)
        {
            return;
        }

        entry.DueTimestamp = m_TimeSource.Add(m_TimeSource.GetTimestamp(), entry.Period.Value);
        lock (m_Gate)
        {
            if (!entry.IsCanceled && !IsDisposed)
            {
                m_Entries.Enqueue(entry, entry.DueTimestamp);
            }
        }

        SignalWorker();
    }

    // 용도: 새 timer 또는 cancel signal까지 worker 대기
    private async Task WaitForNextSignalAsync(TimeSpan? waitDelay, CancellationToken cancellationToken)
    {
        try
        {
            if (waitDelay is null)
            {
                await m_Signal.WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await m_Signal.WaitAsync(waitDelay.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 상태: dispose cancellation은 정상 worker 종료 신호
        }
    }

    // 용도: worker가 다음 due 계산을 다시 하도록 깨움
    private void SignalWorker()
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            m_Signal.Release();
        }
        catch (ObjectDisposedException)
        {
            // 상태: dispose race에서는 signal 불필요
        }
    }

    // 보호: dispose 이후 schedule 거부
    private void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(TimerQueue));
        }
    }

    // 상태: priority queue에 저장되는 timer entry
    private sealed class TimerQueueEntry
    {
        // 용도: equal due timestamp에서도 handle/debug 식별 가능
        public readonly long Id;

        // 용도: callback 실행 시점
        public long DueTimestamp;

        // 용도: null이면 one-shot, 값이 있으면 periodic
        public readonly TimeSpan? Period;

        // 용도: due 시 실행할 짧은 callback
        public readonly Action Callback;

        // 상태: cancel 요청 여부
        private int m_Canceled;

        public TimerQueueEntry(long id, long dueTimestamp, TimeSpan? period, Action callback)
        {
            Id = id;
            DueTimestamp = dueTimestamp;
            Period = period;
            Callback = callback;
        }

        // 상태: cancel 요청 snapshot
        public bool IsCanceled => Volatile.Read(ref m_Canceled) == 1;

        // 용도: handle cancel에서 한 번만 취소 상태 전환
        public bool Cancel()
        {
            return Interlocked.Exchange(ref m_Canceled, 1) == 0;
        }
    }

    // 용도: 외부 caller가 timer를 취소하는 handle 구현
    private sealed class TimerQueueHandle : ITimerQueueHandle
    {
        // 대상: 취소할 timer entry
        private readonly TimerQueueEntry m_Entry;

        // 신호: cancel 이후 worker wake-up
        private readonly Action m_OnCanceled;

        public TimerQueueHandle(TimerQueueEntry entry, Action onCanceled)
        {
            m_Entry = entry;
            m_OnCanceled = onCanceled;
        }

        // 상태: 취소 여부
        public bool IsCanceled => m_Entry.IsCanceled;

        // 용도: Dispose를 cancel과 동일하게 처리
        public void Dispose()
        {
            Cancel();
        }

        // 용도: 아직 실행되지 않은 timer 취소
        public bool Cancel()
        {
            bool canceled = m_Entry.Cancel();
            if (canceled)
            {
                m_OnCanceled();
            }

            return canceled;
        }
    }
}
