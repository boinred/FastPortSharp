using System.Net.Sockets;
using LibNetworks;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FastPortTests;

[TestClass]
public sealed class BaseListenerShutdownTests
{
    // 목적: 세션 생성 경로가 호출되지 않는 shutdown 테스트 전용 stub
    private sealed class ThrowingSessionFactory : IClientSessionFactory
    {
        public BaseSessionClient Create(Socket clientSocket)
            => throw new NotSupportedException("No connections expected in shutdown tests.");
    }

    private static BaseMessageListener CreateListener()
        => new(NullLogger<BaseMessageListener>.Instance, new ThrowingSessionFactory());

    [TestMethod]
    public void RequestShutdown_ReleasesListeningPort_AllowsRebind()
    {
        int port = GetFreeTcpPort();

        var first = CreateListener();
        Assert.IsTrue(first.StartAccept("127.0.0.1", port), "First listener should start.");

        first.RequestShutdown();

        // 회귀 검증: shutdown이 리스닝 소켓을 실제로 닫아야 동일 포트 재바인딩이 가능하다.
        var second = CreateListener();
        try
        {
            Assert.IsTrue(
                second.StartAccept("127.0.0.1", port),
                "Port should be rebindable after RequestShutdown closes the listening socket.");
        }
        finally
        {
            second.RequestShutdown();
        }
    }

    [TestMethod]
    public void RequestShutdown_CalledTwice_DoesNotThrow()
    {
        int port = GetFreeTcpPort();

        var listener = CreateListener();
        Assert.IsTrue(listener.StartAccept("127.0.0.1", port));

        listener.RequestShutdown();
        listener.RequestShutdown();
    }

    // 목적: OS가 할당한 빈 포트를 얻어 테스트 간 포트 충돌 방지
    private static int GetFreeTcpPort()
    {
        using var probe = new Socket(SocketType.Stream, ProtocolType.Tcp);
        probe.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        return ((System.Net.IPEndPoint)probe.LocalEndPoint!).Port;
    }
}
