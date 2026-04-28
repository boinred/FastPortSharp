using System.Net;
using System.Net.Sockets;
using FastPortLoadRunner;
using LibNetworks.Sessions;
using LibNetworks.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibCommonTest;

[TestClass]
public sealed class FastPortSmokeServerTests
{
    [TestMethod]
    public async Task FastPortSmokeServer_FixedPayload_EchoesAndRecordsTelemetry()
    {
        await using FastPortSmokeServerTestHost server = await FastPortSmokeServerTestHost.StartAsync();

        (MetricsSnapshot client, ServerTelemetrySnapshot serverSnapshot, ServerObservedMetricsSnapshot serverObserved) = await RunSmokeAsync(
            server,
            sessions: 10,
            payload: PayloadProfile.Fixed(1024));

        AssertClientMetrics(client);
        AssertServerTelemetry(serverSnapshot, expectedAcceptedSessions: 10);
        AssertServerObservedMetrics(serverObserved, expectedAcceptedSessions: 10);
    }

    [TestMethod]
    public async Task FastPortSmokeServer_RandomLargePayload_EchoesAndRecordsTelemetry()
    {
        await using FastPortSmokeServerTestHost server = await FastPortSmokeServerTestHost.StartAsync();

        (MetricsSnapshot client, ServerTelemetrySnapshot serverSnapshot, ServerObservedMetricsSnapshot serverObserved) = await RunSmokeAsync(
            server,
            sessions: 2,
            payload: new PayloadProfile(PayloadMode.Random, 4096, 16384));

        AssertClientMetrics(client);
        AssertServerTelemetry(serverSnapshot, expectedAcceptedSessions: 2);
        AssertServerObservedMetrics(serverObserved, expectedAcceptedSessions: 2);
    }

    private static async Task<(MetricsSnapshot Client, ServerTelemetrySnapshot Server, ServerObservedMetricsSnapshot ServerObserved)> RunSmokeAsync(
        FastPortSmokeServerTestHost server,
        int sessions,
        PayloadProfile payload)
    {
        var scenario = new LoadScenario(
            Host: IPAddress.Loopback.ToString(),
            Port: server.Port,
            Sessions: sessions,
            Payload: payload,
            SendRatePerSession: 1,
            RampUp: TimeSpan.FromSeconds(1),
            Duration: TimeSpan.FromSeconds(2),
            MetricsInterval: TimeSpan.FromSeconds(1),
            OutputPath: null);

        var metricsCollector = new MetricsCollector(scenario.Sessions);
        var runner = new LoadRunner(scenario, metricsCollector, Array.Empty<IMetricsReporter>());

        await runner.RunAsync(CancellationToken.None);

        MetricsSnapshot clientSnapshot = metricsCollector.CreateSnapshot();
        ServerTelemetrySnapshot serverSnapshot = await WaitForTelemetryAsync(
            server.Telemetry,
            snapshot => snapshot.ConnectedSessions == 0 && snapshot.DisconnectedSessions >= sessions,
            TimeSpan.FromSeconds(5));
        ServerObservedMetricsSnapshot serverObserved = server.TelemetryExporter.CreateSnapshot();

        return (clientSnapshot, serverSnapshot, serverObserved);
    }

    private static void AssertClientMetrics(MetricsSnapshot snapshot)
    {
        Assert.IsTrue(snapshot.TotalSentPackets > 0, "Client should send packets.");
        Assert.IsTrue(snapshot.TotalReceivedPackets > 0, "Client should receive echo responses.");
        Assert.AreEqual(0, snapshot.ConnectedSessions, "LoadRunner sessions should disconnect after the run.");
        Assert.AreEqual(0, snapshot.SocketErrorCount, "Client smoke run should not record socket errors.");
    }

    private static void AssertServerTelemetry(ServerTelemetrySnapshot snapshot, long expectedAcceptedSessions)
    {
        Assert.IsTrue(snapshot.AcceptedSessions >= expectedAcceptedSessions, "Server should accept all smoke sessions.");
        Assert.IsTrue(snapshot.DisconnectedSessions >= expectedAcceptedSessions, "Server should observe session disconnects.");
        Assert.AreEqual(0, snapshot.ConnectedSessions, "Server connected session count should return to zero.");
        Assert.IsTrue(snapshot.ReceivedPackets > 0, "Server should receive packets.");
        Assert.IsTrue(snapshot.SentPackets > 0, "Server should send echo responses.");
        Assert.IsTrue(snapshot.ReceivedBytes > 0, "Server should record received bytes.");
        Assert.IsTrue(snapshot.SentBytes > 0, "Server should record sent bytes.");
        Assert.AreEqual(0, snapshot.ParseErrors, "Smoke run should not record parse errors.");
        Assert.AreEqual(0, snapshot.ProtocolErrors, "Smoke run should not record protocol errors.");
        Assert.AreEqual(0, snapshot.AcceptErrors, "Smoke run should not record accept errors.");
        Assert.AreEqual(0, snapshot.SocketErrors, "Smoke run should not record server socket errors.");
        Assert.AreEqual(0, snapshot.SocketErrorRate, "Smoke run socket error rate should remain zero.");
    }

    private static void AssertServerObservedMetrics(ServerObservedMetricsSnapshot snapshot, long expectedAcceptedSessions)
    {
        Assert.IsTrue(snapshot.TotalAcceptedSessions >= expectedAcceptedSessions, "Server observed metrics should expose accepted sessions.");
        Assert.IsTrue(snapshot.TotalDisconnectedSessions >= expectedAcceptedSessions, "Server observed metrics should expose disconnects.");
        Assert.AreEqual(0, snapshot.CurrentSessions, "Server observed current sessions should return to zero.");
        Assert.IsTrue(snapshot.TotalReceivedPackets > 0, "Server observed metrics should expose received packets.");
        Assert.IsTrue(snapshot.TotalSendCompletions > 0, "Server observed metrics should expose send completions.");
        Assert.IsTrue(snapshot.TotalParsedPacketBytes > 0, "Server observed metrics should expose parsed packet bytes.");
        Assert.IsTrue(snapshot.TotalSentBytes > 0, "Server observed metrics should expose sent bytes.");
        Assert.AreEqual(0, snapshot.SocketErrorCount, "Server observed metrics should expose socket error count.");
        Assert.AreEqual(0, snapshot.ParseErrorCount, "Server observed metrics should expose parse error count.");
        Assert.AreEqual(0, snapshot.ProtocolErrorCount, "Server observed metrics should expose protocol error count.");
    }

    private static async Task<ServerTelemetrySnapshot> WaitForTelemetryAsync(
        IServerTelemetry telemetry,
        Func<ServerTelemetrySnapshot, bool> predicate,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();

        while (!timeoutSource.IsCancellationRequested)
        {
            snapshot = telemetry.CreateSnapshot();
            if (predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(50, CancellationToken.None);
        }

        return snapshot;
    }

    private sealed class FastPortSmokeServerTestHost : IAsyncDisposable
    {
        private readonly IHost _host;

        private FastPortSmokeServerTestHost(
            IHost host,
            int port,
            IServerTelemetry telemetry,
            IServerTelemetryExporter telemetryExporter)
        {
            _host = host;
            Port = port;
            Telemetry = telemetry;
            TelemetryExporter = telemetryExporter;
        }

        public int Port { get; }

        public IServerTelemetry Telemetry { get; }

        public IServerTelemetryExporter TelemetryExporter { get; }

        public static async Task<FastPortSmokeServerTestHost> StartAsync()
        {
            int port = GetFreeTcpPort();
            var telemetry = new ServerTelemetryCollector();

            IHost host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Warning);
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton(new FastPortSmokeServer.FastPortSmokeServerOptions
                    {
                        Host = IPAddress.Loopback.ToString(),
                        Port = port
                    });
                    services.AddSingleton<IServerTelemetry>(telemetry);
                    services.AddSingleton<IServerTelemetryExporter, ServerTelemetryExporter>();
                    services.AddHostedService<FastPortSmokeServer.FastPortSmokeServerBackgroundService>();
                    services.AddSingleton<IClientSessionFactory, FastPortSmokeServer.Sessions.FastPortSmokeClientSessionFactory>();
                    services.AddSingleton<FastPortSmokeServer.FastPortSmokeServer>();
                })
                .Build();

            var telemetryExporter = host.Services.GetRequiredService<IServerTelemetryExporter>();
            var server = new FastPortSmokeServerTestHost(host, port, telemetry, telemetryExporter);
            await host.StartAsync();
            await server.WaitUntilReadyAsync();
            telemetry.Reset();
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        private async Task WaitUntilReadyAsync()
        {
            using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            while (!timeoutSource.IsCancellationRequested)
            {
                using var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(IPAddress.Loopback, Port, timeoutSource.Token);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (SocketException)
                {
                    await Task.Delay(50, CancellationToken.None);
                }
            }

            throw new TimeoutException($"FastPortSmokeServer did not become ready on port {Port}.");
        }

        private static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
