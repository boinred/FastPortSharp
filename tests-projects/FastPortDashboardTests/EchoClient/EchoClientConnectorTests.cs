// Design Ref: §8.2 L1-04 — EchoClientConnector state machine transitions (socket-free).
using FastPortDashboard.Maui.EchoClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace FastPortDashboardTests.EchoClient;

[TestClass]
public sealed class EchoClientConnectorTests
{
    private static EchoClientConnector NewConnector() => new(NullLoggerFactory.Instance);

    [TestMethod]
    public void InitialState_IsDisconnected()
    {
        var c = NewConnector();
        Assert.AreEqual(EchoClientState.Disconnected, c.State);
        Assert.IsNull(c.ErrorMessage);
    }

    [TestMethod]
    public void TryBeginConnect_FromDisconnected_TransitionsToConnecting()
    {
        var c = NewConnector();
        Assert.IsTrue(c.TryBeginConnect());
        Assert.AreEqual(EchoClientState.Connecting, c.State);
    }

    [TestMethod]
    public void TryBeginConnect_FromConnecting_Rejected()
    {
        var c = NewConnector();
        c.TryBeginConnect();
        Assert.IsFalse(c.TryBeginConnect());
        Assert.AreEqual(EchoClientState.Connecting, c.State);
    }

    [TestMethod]
    public void TryBeginConnect_FromError_AllowedAndClearsErrorMessage()
    {
        var c = NewConnector();
        c.NotifyError("EC-CONNECT-001: bad host");
        Assert.AreEqual(EchoClientState.Error, c.State);

        Assert.IsTrue(c.TryBeginConnect());
        Assert.AreEqual(EchoClientState.Connecting, c.State);
        Assert.IsNull(c.ErrorMessage);
    }

    [TestMethod]
    public void NotifyConnected_FromConnecting_TransitionsToConnected()
    {
        var c = NewConnector();
        c.TryBeginConnect();
        c.NotifyConnected();
        Assert.AreEqual(EchoClientState.Connected, c.State);
    }

    [TestMethod]
    public void NotifyDisconnected_AfterConnected_TransitionsBackToDisconnected()
    {
        var c = NewConnector();
        c.TryBeginConnect();
        c.NotifyConnected();
        c.NotifyDisconnected();
        Assert.AreEqual(EchoClientState.Disconnected, c.State);
    }

    [TestMethod]
    public void StateChanged_FiresOnEveryTransition()
    {
        var c = NewConnector();
        var transitions = new List<EchoClientState>();
        c.StateChanged += s => transitions.Add(s);

        c.TryBeginConnect();
        c.NotifyConnected();
        c.NotifyDisconnected();

        CollectionAssert.AreEqual(
            new[] { EchoClientState.Connecting, EchoClientState.Connected, EchoClientState.Disconnected },
            transitions);
    }

    [TestMethod]
    public void NotifyError_AfterConnected_PreservesErrorMessage()
    {
        var c = NewConnector();
        c.TryBeginConnect();
        c.NotifyConnected();
        c.NotifyError("EC-RUNTIME-001: server closed");
        Assert.AreEqual(EchoClientState.Error, c.State);
        Assert.AreEqual("EC-RUNTIME-001: server closed", c.ErrorMessage);
    }
}
