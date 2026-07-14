// Design Ref: §9 — Echo session I/O layer. Plan SC: FR-03, FR-04.
// SampleClientSession 패턴 차용 + 반복 send loop (in-flight 1 echo 보장: 다음 send는 직전 recv 이후).
using FastPortGameServerTemplate.Protocols;
using LibCommons;
using LibNetworks.Extensions;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Sockets;

namespace FastPortDashboard.Maui.EchoClient;

public sealed class EchoClientSession : BaseSessionServer
{
    private readonly EchoClientOptions _options;
    private readonly EchoClientStats _stats;
    private readonly Action<RttSample> _onRttSample;
    private readonly Action<string> _onError;
    private readonly CancellationTokenSource _loopCts = new();

    private long _sendTimestamp;
    private long _lastBytesSent;

    public EchoClientSession(
        ILogger<BaseSessionServer> logger,
        Socket socket,
        LibCommons.IBuffers receivedBuffers,
        LibCommons.IBuffers sendBuffers,
        EchoClientOptions options,
        EchoClientStats stats,
        Action<RttSample> onRttSample,
        Action<string> onError)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {
        _options = options;
        _stats = stats;
        _onRttSample = onRttSample;
        _onError = onError;
    }

    public override void OnConnected()
    {
        base.OnConnected();
        SendOne();
    }

    private void SendOne()
    {
        if (_loopCts.IsCancellationRequested) return;
        try
        {
            _sendTimestamp = Stopwatch.GetTimestamp();
            var request = new EchoRequest { Message = _options.Message };
            // wire size = 4-byte packet id + protobuf payload (BaseSession.TryRequestSendMessage 와 동일).
            _lastBytesSent = sizeof(int) + request.CalculateSize();
            RequestSendMessage((int)PacketIds.EchoRequest, request);
            _stats.RecordSend(DateTime.UtcNow, _lastBytesSent);
        }
        catch (Exception ex)
        {
            _onError($"EC-RUNTIME-001: {ex.Message}");
        }
    }

    protected override void OnReceived(BasePacket packet)
    {
        base.OnReceived(packet);

        if (!packet.ParseMessageFromPacket<EchoResponse>(out var packetId, out var response) || response is null)
        {
            _onError($"EC-PROTO-001: ParseMessageFromPacket failed (PacketId={packetId}, DataSize={packet.DataSize})");
            return;
        }
        if (packetId != (int)PacketIds.EchoResponse)
        {
            _onError($"EC-PROTO-001: Unexpected packet id (expected={(int)PacketIds.EchoResponse}, got={packetId})");
            return;
        }

        long recvTimestamp = Stopwatch.GetTimestamp();
        double rttMs = (recvTimestamp - _sendTimestamp) * 1000.0 / Stopwatch.Frequency;
        DateTime now = DateTime.UtcNow;

        _stats.RecordReceive(now, packet.DataSize, rttMs);
        _onRttSample(new RttSample(now, rttMs));

        // Plan SC: FR-04 — in-flight 1 echo. 다음 send는 interval 후에만 dispatch.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Math.Max(1, _options.SendIntervalMs), _loopCts.Token);
                if (!IsDisconnected) SendOne();
            }
            catch (OperationCanceledException) { }
        });
    }

    public void StopLoop()
    {
        try { _loopCts.Cancel(); } catch { }
    }
}
