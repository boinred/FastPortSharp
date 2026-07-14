using System.ComponentModel;
using System.Windows.Input;
using FastPortDashboard.Maui.ViewModels;
using LibTestTelemetry;

namespace FastPortDashboardTests.ViewModels;

// Design Ref: §3.3 — DashboardViewModel Toolkit 동작 검증.
[TestClass]
public class DashboardViewModelTests
{
    // ─── Helper ──────────────────────────────────────────────────
    private static ServerObservedMetricsSnapshot MakeServerSnap(
        long sessions = 10,
        long totalAccepted = 100,
        long totalSentBytes = 12345,
        long pendingSendRequests = 3,
        long sendBufferBytes = 768,
        double sentBytesPerSecond = 200.5,
        DateTimeOffset? ts = null)
    {
        return new ServerObservedMetricsSnapshot(
            Timestamp: ts ?? DateTimeOffset.UtcNow,
            CurrentSessions: sessions,
            TotalAcceptedSessions: totalAccepted,
            TotalDisconnectedSessions: 0,
            TotalReceivedPackets: 0,
            TotalSendCompletions: 0,
            TotalParsedPacketBytes: 0,
            TotalSentBytes: totalSentBytes,
            ReceivedPacketsPerSecond: 0,
            SendCompletionsPerSecond: 0,
            ParsedPacketBytesPerSecond: 0,
            SentBytesPerSecond: sentBytesPerSecond,
            AcceptedSessionsPerSecond: 0,
            DisconnectedSessionsPerSecond: 0,
            AcceptErrorCount: 0,
            SocketErrorCount: 0,
            ParseErrorCount: 0,
            ProtocolErrorCount: 0,
            SocketErrorRate: 0,
            TotalSendRequests: 0,
            PendingSendRequests: pendingSendRequests,
            SendBufferBytes: sendBufferBytes);
    }

    private static List<string> CapturePropertyChanged(INotifyPropertyChanged vm)
    {
        var hits = new List<string>();
        vm.PropertyChanged += (_, e) => hits.Add(e.PropertyName ?? "<null>");
        return hits;
    }

    private static int CountCanExecuteChanged(ICommand cmd)
    {
        int n = 0;
        cmd.CanExecuteChanged += (_, _) => n++;
        return n; // Note: 호출자가 closure로 n을 reset 못하므로 ref counter 패턴은 별도 사용
    }

    // T-VM-1
    [TestMethod]
    public void FilePath_Set_FiresPropertyChanged()
    {
        var vm = new DashboardViewModel();
        var hits = CapturePropertyChanged(vm);

        vm.FilePath = "/tmp/x.jsonl";

        CollectionAssert.Contains(hits, nameof(DashboardViewModel.FilePath));
    }

    // T-VM-2
    [TestMethod]
    public void UseMock_Set_FiresPropertyChanged()
    {
        var vm = new DashboardViewModel();
        var hits = CapturePropertyChanged(vm);

        vm.UseMock = true;

        CollectionAssert.Contains(hits, nameof(DashboardViewModel.UseMock));
    }

    // T-VM-3
    [TestMethod]
    public void ApplySnapshot_FiresPropertyChangedForKpiFields()
    {
        var vm = new DashboardViewModel();
        var hits = CapturePropertyChanged(vm);

        vm.ApplySnapshot(ObservedMetricsSnapshot.FromServer(MakeServerSnap(sessions: 7)));

        CollectionAssert.Contains(hits, nameof(DashboardViewModel.CurrentSessions));
        CollectionAssert.Contains(hits, nameof(DashboardViewModel.LastUpdate));
    }

    // T-VM-4
    [TestMethod]
    public void Reflection_StateField_HasNotifyCanExecuteChangedForAttributes()
    {
        // Toolkit이 _state field의 [NotifyCanExecuteChangedFor] attribute를 인식해
        // Connect/DisconnectCommand의 CanExecuteChanged를 fire하는지 확인.
        var field = typeof(DashboardViewModel).GetField("_state",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(field, "_state field 존재");

        var attrNames = field!.GetCustomAttributes(inherit: false)
            .Select(a => a.GetType().Name)
            .ToList();

        // Source generator는 attribute를 generated property에 옮길 수 있으므로, field에 있거나 generated property에 있어야 함.
        // 우선 field에 NotifyCanExecuteChangedForAttribute가 그대로 남아있는지 확인.
        bool hasOnField = attrNames.Any(n => n.Contains("NotifyCanExecuteChangedFor"));

        var prop = typeof(DashboardViewModel).GetProperty("State");
        Assert.IsNotNull(prop, "State property generated");
        bool hasOnProp = prop!.GetCustomAttributes(inherit: false)
            .Any(a => a.GetType().Name.Contains("NotifyCanExecuteChangedFor"));

        Assert.IsTrue(hasOnField || hasOnProp,
            $"NotifyCanExecuteChangedFor가 field 또는 property에 존재해야 함. field attrs: [{string.Join(',', attrNames)}]");
    }

    // T-VM-5
    [TestMethod]
    public void State_ChangeFromIdleToPolling_FiresBothCommandsCanExecuteChanged()
    {
        var vm = new DashboardViewModel();
        int connectFires = 0;
        int disconnectFires = 0;
        vm.ConnectCommand.CanExecuteChanged += (_, _) => connectFires++;
        vm.DisconnectCommand.CanExecuteChanged += (_, _) => disconnectFires++;

        // private setter라 reflection 사용
        var stateProp = typeof(DashboardViewModel).GetProperty("State")!;
        stateProp.SetValue(vm, PollingState.Polling);

        Assert.IsTrue(connectFires >= 1, $"ConnectCommand.CanExecuteChanged 발생 (count={connectFires})");
        Assert.IsTrue(disconnectFires >= 1, $"DisconnectCommand.CanExecuteChanged 발생 (count={disconnectFires})");
    }

    // T-VM-6
    [TestMethod]
    public void ConnectCommand_CanExecute_TrueWhenIdle_FalseWhenPolling()
    {
        var vm = new DashboardViewModel();
        // 초기 State = Idle
        Assert.IsTrue(vm.ConnectCommand.CanExecute(null), "Idle에서 Connect 가능");

        var stateProp = typeof(DashboardViewModel).GetProperty("State")!;
        stateProp.SetValue(vm, PollingState.Polling);

        Assert.IsFalse(vm.ConnectCommand.CanExecute(null), "Polling 중에는 Connect 불가");
    }

    // T-VM-7
    [TestMethod]
    public void DisconnectCommand_CanExecute_FalseWhenIdle_TrueWhenPolling()
    {
        var vm = new DashboardViewModel();
        Assert.IsFalse(vm.DisconnectCommand.CanExecute(null), "Idle에서 Disconnect 불가");

        var stateProp = typeof(DashboardViewModel).GetProperty("State")!;
        stateProp.SetValue(vm, PollingState.Polling);

        Assert.IsTrue(vm.DisconnectCommand.CanExecute(null), "Polling 중 Disconnect 가능");
    }

    // T-VM-8
    [TestMethod]
    public void ApplySnapshot_AssignsAllKpiAndAppendsSeries()
    {
        var vm = new DashboardViewModel();
        var ts = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        var snap = ObservedMetricsSnapshot.FromServer(MakeServerSnap(
            sessions: 42, totalAccepted: 500, totalSentBytes: 999_999,
            pendingSendRequests: 7, sendBufferBytes: 1_792, sentBytesPerSecond: 333.5, ts: ts));

        vm.ApplySnapshot(snap);

        Assert.AreEqual(42, vm.CurrentSessions);
        Assert.AreEqual(500, vm.TotalAcceptedSessions);
        Assert.AreEqual(999_999, vm.TotalSentBytes);
        Assert.AreEqual(7, vm.PendingSendRequests);
        Assert.AreEqual(1_792, vm.SendBufferBytes);
        Assert.AreEqual(ts, vm.LastUpdate);
        Assert.AreEqual(1, vm.ThroughputSeries.Count);
    }

    // T-VM-9
    [TestMethod]
    public void ApplySnapshot_TrimsThroughputSeriesAt600()
    {
        var vm = new DashboardViewModel();
        var baseTs = new DateTimeOffset(2026, 5, 11, 0, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < 601; i++)
        {
            var snap = ObservedMetricsSnapshot.FromServer(
                MakeServerSnap(sessions: i, sentBytesPerSecond: i, ts: baseTs.AddSeconds(i)));
            vm.ApplySnapshot(snap);
        }

        Assert.AreEqual(600, vm.ThroughputSeries.Count, "MaxChartPoints 600 trimming");
        // 첫번째(i=0) 제거, 가장 오래된 것은 i=1.
        Assert.AreEqual((double)1, vm.ThroughputSeries[0].Value, "가장 오래된 point 제거");
        Assert.AreEqual((double)600, vm.ThroughputSeries[^1].Value, "최신 point 보존");
    }

    // ─── Helper for client snap ──────────────────────────────────
    private static ClientObservedMetricsSnapshot MakeClientSnap(
        double rttP95Ms = 75, DateTimeOffset? ts = null)
    {
        return new ClientObservedMetricsSnapshot(
            Timestamp: ts ?? DateTimeOffset.UtcNow,
            TargetSessions: 100, CurrentSessions: 10,
            TotalSentPackets: 0, TotalReceivedPackets: 0,
            TotalSentBytes: 0, TotalReceivedBytes: 0,
            SentPacketsPerSecond: 0, ReceivedPacketsPerSecond: 0,
            SentBytesPerSecond: 0, ReceivedBytesPerSecond: 0,
            Tps: 0,
            RttAverageMs: 40, RttP50Ms: 30, RttP95Ms: rttP95Ms, RttP99Ms: 150,
            ConnectCount: 0, DisconnectCount: 0,
            SocketErrorCount: 0, SocketErrorRate: 0);
    }

    // T-VM-11 (dashboard-multi-rtt-overlay): ClientObserved → ClientRttSeries P50/P95/P99 append.
    [TestMethod]
    public void ApplySnapshot_ClientObserved_AppendsRttSeries()
    {
        var vm = new DashboardViewModel();
        var combined = ObservedMetricsSnapshot.Combined(
            MakeClientSnap(rttP95Ms: 87.5),
            MakeServerSnap());

        vm.ApplySnapshot(combined);

        Assert.AreEqual(1, vm.ClientRttSeries.Count);
        Assert.AreEqual(30, vm.ClientRttSeries[0].P50Ms, "P50 from MakeClientSnap default");
        Assert.AreEqual(87.5, vm.ClientRttSeries[0].P95Ms, "P95 from argument");
        Assert.AreEqual(150, vm.ClientRttSeries[0].P99Ms, "P99 from MakeClientSnap default");
    }

    // T-VM-12 (dashboard-multi-rtt-overlay): ClientRttSeries 600 trim — P95 기준 검증.
    [TestMethod]
    public void ApplyClientSnapshot_TrimsRttSeriesAt600()
    {
        var vm = new DashboardViewModel();
        var baseTs = new DateTimeOffset(2026, 5, 11, 0, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < 601; i++)
        {
            var snap = ObservedMetricsSnapshot.Combined(
                MakeClientSnap(rttP95Ms: i, ts: baseTs.AddSeconds(i)),
                serverObserved: null);
            vm.ApplySnapshot(snap);
        }

        Assert.AreEqual(600, vm.ClientRttSeries.Count, "MaxChartPoints 600 trim (client RTT)");
        Assert.AreEqual((double)1, vm.ClientRttSeries[0].P95Ms, "가장 오래된 (i=0) 제거");
        Assert.AreEqual((double)600, vm.ClientRttSeries[^1].P95Ms, "최신 보존");
    }

    // T-VM-10
    [TestMethod]
    public async Task ConnectCommand_NoMockAndEmptyFilePath_SetsErrorState()
    {
        var vm = new DashboardViewModel();
        vm.UseMock = false;
        vm.FilePath = string.Empty;

        // RelayCommand는 IAsyncRelayCommand 구현 → ExecuteAsync 사용
        var cmd = (CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.ConnectCommand;
        await cmd.ExecuteAsync(null);

        Assert.AreEqual(PollingState.Error, vm.State);
        Assert.IsFalse(string.IsNullOrEmpty(vm.ErrorMessage));
    }
}
