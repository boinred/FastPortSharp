namespace FastPortTestSmokeServer.Sessions;

// 용도: smoke/load validation server의 application-level idle cleanup 설정
public sealed class SessionIdleTrackerOptions
{
    // 상태: idle cleanup 활성화 여부
    public bool Enabled { get; init; } = true;

    // 정책: activity 없이 유지 가능한 최대 시간
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(2);

    // 정책: registered sessions scan 주기
    public TimeSpan ScanInterval { get; init; } = TimeSpan.FromSeconds(5);

    // 상태: 0 이하 timeout 입력 방어
    public TimeSpan NormalizedIdleTimeout => IdleTimeout <= TimeSpan.Zero
        ? TimeSpan.FromMinutes(2)
        : IdleTimeout;

    // 상태: 0 이하 scan interval 입력 방어
    public TimeSpan NormalizedScanInterval => ScanInterval <= TimeSpan.Zero
        ? TimeSpan.FromSeconds(5)
        : ScanInterval;
}
