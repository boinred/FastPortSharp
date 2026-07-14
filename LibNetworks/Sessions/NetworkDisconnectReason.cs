namespace LibNetworks.Sessions;

// 용도: session disconnect 원인을 telemetry와 cleanup policy에서 구분
public enum NetworkDisconnectReason
{
    // 상태: 기존 call site 또는 분류되지 않은 종료
    Unknown = 0,

    // 상태: peer가 정상적으로 receive zero bytes를 반환하며 종료
    RemoteClosed = 1,

    // 상태: receive completion에서 socket error 발생
    ReceiveSocketError = 2,

    // 상태: 다음 receive 요청 시작 중 socket error 발생
    ReceiveRequestError = 3,

    // 상태: send path에서 socket error 발생
    SendSocketError = 4,

    // 상태: send가 0 byte를 반환해 연결 종료로 판단
    SendZeroBytes = 5,

    // 상태: application-level idle timeout에 따른 정리
    IdleTimeout = 6,

    // 상태: local shutdown 또는 명시적 종료 정책
    LocalShutdown = 7
}
