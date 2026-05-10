using FastPortTestSmokeServer.Sessions;
using LibCommons.Timers;
using LibNetworks.Sessions;
using LibTestTelemetry;

namespace FastPortTests;

[TestClass]
public sealed class SessionIdleTrackerTests
{
    [TestMethod]
    public void SessionIdleTracker_ScanExpired_DisconnectsIdleSession()
    {
        var timeSource = new ManualTimeSource();
        var telemetry = new ServerTelemetryCollector();
        using var tracker = new SessionIdleTracker(
            new SessionIdleTrackerOptions
            {
                Enabled = true,
                IdleTimeout = TimeSpan.FromSeconds(10),
                ScanInterval = TimeSpan.FromSeconds(1)
            },
            new NullTimerQueue(),
            timeSource,
            telemetry);
        var session = new TestIdleSession(id: 1, lastReceivedTimestamp: timeSource.GetTimestamp());

        tracker.Register(session);
        timeSource.Advance(TimeSpan.FromSeconds(11));

        int disconnected = tracker.ScanExpired();
        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();

        Assert.AreEqual(1, disconnected);
        Assert.IsTrue(session.IsDisconnected);
        Assert.AreEqual(NetworkDisconnectReason.IdleTimeout, session.DisconnectReason);
        Assert.AreEqual(1, snapshot.IdleTimeoutDisconnects);
        Assert.AreEqual(11000, snapshot.MaxIdleTimeoutAgeMs);
    }

    [TestMethod]
    public void SessionIdleTracker_ScanExpired_DoesNotDisconnectActiveSession()
    {
        var timeSource = new ManualTimeSource();
        var telemetry = new ServerTelemetryCollector();
        using var tracker = new SessionIdleTracker(
            new SessionIdleTrackerOptions
            {
                Enabled = true,
                IdleTimeout = TimeSpan.FromSeconds(10),
                ScanInterval = TimeSpan.FromSeconds(1)
            },
            new NullTimerQueue(),
            timeSource,
            telemetry);
        var session = new TestIdleSession(id: 1, lastReceivedTimestamp: timeSource.GetTimestamp());

        tracker.Register(session);
        timeSource.Advance(TimeSpan.FromSeconds(9));

        int disconnected = tracker.ScanExpired();

        Assert.AreEqual(0, disconnected);
        Assert.IsFalse(session.IsDisconnected);
        Assert.AreEqual(0, telemetry.CreateSnapshot().IdleTimeoutDisconnects);
    }

    [TestMethod]
    public void SessionIdleTracker_Unregister_RemovesSessionFromScan()
    {
        var timeSource = new ManualTimeSource();
        var telemetry = new ServerTelemetryCollector();
        using var tracker = new SessionIdleTracker(
            new SessionIdleTrackerOptions
            {
                Enabled = true,
                IdleTimeout = TimeSpan.FromSeconds(10),
                ScanInterval = TimeSpan.FromSeconds(1)
            },
            new NullTimerQueue(),
            timeSource,
            telemetry);
        var session = new TestIdleSession(id: 1, lastReceivedTimestamp: timeSource.GetTimestamp());

        tracker.Register(session);
        Assert.IsTrue(tracker.Unregister(session.Id));
        timeSource.Advance(TimeSpan.FromSeconds(11));

        int disconnected = tracker.ScanExpired();

        Assert.AreEqual(0, disconnected);
        Assert.IsFalse(session.IsDisconnected);
        Assert.AreEqual(0, tracker.Count);
    }

    private sealed class TestIdleSession : IIdleTrackedSession
    {
        private int m_Disconnected;

        public TestIdleSession(long id, long lastReceivedTimestamp)
        {
            Id = id;
            LastReceivedTimestamp = lastReceivedTimestamp;
        }

        public long Id { get; }

        public bool IsDisconnected => Volatile.Read(ref m_Disconnected) == 1;

        public long LastReceivedTimestamp { get; private set; }

        public NetworkDisconnectReason DisconnectReason { get; private set; } = NetworkDisconnectReason.Unknown;

        public bool RequestDisconnect(NetworkDisconnectReason reason)
        {
            if (Interlocked.Exchange(ref m_Disconnected, 1) != 0)
            {
                return false;
            }

            DisconnectReason = reason;
            return true;
        }
    }

    private sealed class NullTimerQueue : ITimerQueue
    {
        public ITimerQueueHandle Schedule(TimeSpan delay, Action callback)
        {
            return new NullTimerQueueHandle();
        }

        public ITimerQueueHandle SchedulePeriodic(TimeSpan interval, Action callback)
        {
            return new NullTimerQueueHandle();
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NullTimerQueueHandle : ITimerQueueHandle
    {
        public bool IsCanceled { get; private set; }

        public bool Cancel()
        {
            if (IsCanceled)
            {
                return false;
            }

            IsCanceled = true;
            return true;
        }

        public void Dispose()
        {
            Cancel();
        }
    }

    private sealed class ManualTimeSource : IMonotonicTimeSource
    {
        private long m_Timestamp;

        public long GetTimestamp()
        {
            return Volatile.Read(ref m_Timestamp);
        }

        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp)
        {
            return TimeSpan.FromTicks(Math.Max(0, endTimestamp - startTimestamp));
        }

        public long Add(long timestamp, TimeSpan delay)
        {
            return checked(timestamp + delay.Ticks);
        }

        public TimeSpan GetDelay(long nowTimestamp, long dueTimestamp)
        {
            return dueTimestamp <= nowTimestamp
                ? TimeSpan.Zero
                : TimeSpan.FromTicks(dueTimestamp - nowTimestamp);
        }

        public void Advance(TimeSpan delta)
        {
            Interlocked.Add(ref m_Timestamp, delta.Ticks);
        }
    }
}
