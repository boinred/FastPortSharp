// Design Ref: §9 — pure stats with sliding-1s window for rate. Plan SC: FR-04/FR-05/FR-06.
namespace FastPortDashboard.Maui.EchoClient;

public sealed class EchoClientStats
{
    private static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private long _sendCount, _recvCount, _totalBytesSent, _totalBytesRecv, _rttSampleCount;
    private double _lastRttMs, _rttSum;
    private readonly Queue<(DateTime At, long Bytes)> _sendWindow = new();
    private readonly Queue<(DateTime At, long Bytes)> _recvWindow = new();

    public void RecordSend(DateTime utcNow, long bytesSent)
    {
        lock (_gate)
        {
            _sendCount++;
            _totalBytesSent += bytesSent;
            _sendWindow.Enqueue((utcNow, bytesSent));
            TrimWindow(_sendWindow, utcNow);
        }
    }

    public void RecordReceive(DateTime utcNow, long bytesRecv, double rttMs)
    {
        lock (_gate)
        {
            _recvCount++;
            _totalBytesRecv += bytesRecv;
            _lastRttMs = rttMs;
            _rttSum += rttMs;
            _rttSampleCount++;
            _recvWindow.Enqueue((utcNow, bytesRecv));
            TrimWindow(_recvWindow, utcNow);
        }
    }

    public EchoStatsSnapshot Snapshot(DateTime utcNow)
    {
        lock (_gate)
        {
            TrimWindow(_sendWindow, utcNow);
            TrimWindow(_recvWindow, utcNow);
            double avg = _rttSampleCount == 0 ? 0 : _rttSum / _rttSampleCount;
            return new EchoStatsSnapshot(
                _sendCount, _recvCount,
                _sendWindow.Count, _recvWindow.Count,
                _totalBytesSent, _totalBytesRecv,
                _lastRttMs, avg);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _sendCount = _recvCount = _totalBytesSent = _totalBytesRecv = _rttSampleCount = 0;
            _lastRttMs = _rttSum = 0;
            _sendWindow.Clear();
            _recvWindow.Clear();
        }
    }

    private static void TrimWindow(Queue<(DateTime At, long Bytes)> q, DateTime utcNow)
    {
        // cutoff inclusive: 경계 시점 이벤트는 window 안에 포함.
        DateTime cutoff = utcNow - RateWindow;
        while (q.Count > 0 && q.Peek().At < cutoff) q.Dequeue();
    }
}
