using LibNetworks.Sessions;

namespace FastPortTestSmokeServer.Sessions;

// 용도: SessionIdleTracker가 concrete session 구현에 과도하게 결합되지 않도록 하는 최소 contract
public interface IIdleTrackedSession
{
    // 식별자: registry key
    long Id { get; }

    // 상태: 이미 disconnect 처리된 session 여부
    bool IsDisconnected { get; }

    // 상태: 마지막 receive/activity monotonic timestamp
    long LastReceivedTimestamp { get; }

    // 용도: idle timeout 등 reason-aware disconnect 요청
    bool RequestDisconnect(NetworkDisconnectReason reason);
}
