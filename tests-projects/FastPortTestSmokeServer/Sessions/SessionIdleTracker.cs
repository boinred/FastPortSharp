using System.Collections.Concurrent;
using LibCommons.Timers;
using LibNetworks.Sessions;
using LibTestTelemetry;

namespace FastPortTestSmokeServer.Sessions;

// 용도: TimerQueue singleton을 사용하는 smoke/load validation server idle session cleanup policy
public sealed class SessionIdleTracker : IDisposable
{
    // 상태: 현재 idle scan 대상 sessions
    private readonly ConcurrentDictionary<long, IIdleTrackedSession> m_Sessions = new();

    // 설정: idle timeout 및 scan interval
    private readonly SessionIdleTrackerOptions m_Options;

    // 시간: BaseSession timestamp와 동일한 monotonic time source
    private readonly IMonotonicTimeSource m_TimeSource;

    // 관측: idle timeout cleanup counter 기록
    private readonly IServerTelemetry m_ServerTelemetry;

    // 수명: TimerQueue periodic scan 취소 handle
    private readonly ITimerQueueHandle? m_TimerHandle;

    // 상태: dispose 이후 register/scan 방지
    private int m_Disposed;

    public SessionIdleTracker(
        SessionIdleTrackerOptions options,
        ITimerQueue timerQueue,
        IMonotonicTimeSource timeSource,
        IServerTelemetry serverTelemetry)
    {
        m_Options = options;
        m_TimeSource = timeSource;
        m_ServerTelemetry = serverTelemetry;

        if (m_Options.Enabled)
        {
            m_TimerHandle = timerQueue.SchedulePeriodic(m_Options.NormalizedScanInterval, () => ScanExpired());
        }
    }

    // 상태: 현재 등록된 session 수
    public int Count => m_Sessions.Count;

    // 용도: accepted session을 idle scan 대상으로 등록
    public void Register(IIdleTrackedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (IsDisposed || !m_Options.Enabled)
        {
            return;
        }

        m_Sessions[session.Id] = session;
    }

    // 용도: disconnected session을 idle scan 대상에서 제거
    public bool Unregister(long sessionId)
    {
        return m_Sessions.TryRemove(sessionId, out _);
    }

    // 용도: 현재 시각 기준 idle timeout 초과 session 정리
    public int ScanExpired()
    {
        if (IsDisposed || !m_Options.Enabled)
        {
            return 0;
        }

        int disconnectedCount = 0;
        long nowTimestamp = m_TimeSource.GetTimestamp();
        TimeSpan idleTimeout = m_Options.NormalizedIdleTimeout;

        foreach (KeyValuePair<long, IIdleTrackedSession> pair in m_Sessions)
        {
            IIdleTrackedSession session = pair.Value;
            if (session.IsDisconnected)
            {
                Unregister(pair.Key);
                continue;
            }

            TimeSpan idleAge = m_TimeSource.GetElapsedTime(session.LastReceivedTimestamp, nowTimestamp);
            if (idleAge <= idleTimeout)
            {
                continue;
            }

            if (session.RequestDisconnect(NetworkDisconnectReason.IdleTimeout))
            {
                disconnectedCount++;
                m_ServerTelemetry.RecordIdleTimeoutDisconnect(idleAge);
            }
        }

        return disconnectedCount;
    }

    // 용도: host shutdown 시 periodic scan 취소
    public void Dispose()
    {
        if (Interlocked.Exchange(ref m_Disposed, 1) != 0)
        {
            return;
        }

        m_TimerHandle?.Dispose();
        m_Sessions.Clear();
    }

    // 상태: dispose 완료 또는 진행 여부
    private bool IsDisposed => Volatile.Read(ref m_Disposed) == 1;
}
