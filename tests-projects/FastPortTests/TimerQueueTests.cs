using LibCommons.Timers;

namespace FastPortTests;

[TestClass]
public sealed class TimerQueueTests
{
    [TestMethod]
    public async Task TimerQueue_Schedule_ExecutesOneShotTimersInDueOrder()
    {
        await using var timerQueue = new LibCommons.Timers.TimerQueue();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = new List<string>();
        object gate = new();

        timerQueue.Schedule(TimeSpan.FromMilliseconds(40), () => AddExecution("second"));
        timerQueue.Schedule(TimeSpan.FromMilliseconds(10), () => AddExecution("first"));

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        CollectionAssert.AreEqual(new[] { "first", "second" }, executed);

        void AddExecution(string value)
        {
            lock (gate)
            {
                executed.Add(value);
                if (executed.Count == 2)
                {
                    completed.TrySetResult();
                }
            }
        }
    }

    [TestMethod]
    public async Task TimerQueue_Cancel_PreventsPendingCallback()
    {
        await using var timerQueue = new LibCommons.Timers.TimerQueue();
        int callbackCount = 0;

        ITimerQueueHandle handle = timerQueue.Schedule(
            TimeSpan.FromMilliseconds(40),
            () => Interlocked.Increment(ref callbackCount));

        Assert.IsTrue(handle.Cancel());
        Assert.IsFalse(handle.Cancel());

        await Task.Delay(120);

        Assert.AreEqual(0, callbackCount);
    }

    [TestMethod]
    public async Task TimerQueue_SchedulePeriodic_RepeatsUntilCanceled()
    {
        await using var timerQueue = new LibCommons.Timers.TimerQueue();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbackCount = 0;

        ITimerQueueHandle handle = timerQueue.SchedulePeriodic(
            TimeSpan.FromMilliseconds(10),
            () =>
            {
                if (Interlocked.Increment(ref callbackCount) >= 3)
                {
                    completed.TrySetResult();
                }
            });

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        handle.Cancel();
        int countAfterCancel = Volatile.Read(ref callbackCount);

        await Task.Delay(80);

        Assert.IsTrue(countAfterCancel >= 3);
        Assert.AreEqual(countAfterCancel, Volatile.Read(ref callbackCount));
    }

    [TestMethod]
    public async Task TimerQueue_CallbackException_DoesNotStopWorker()
    {
        await using var timerQueue = new LibCommons.Timers.TimerQueue();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        timerQueue.Schedule(TimeSpan.Zero, () => throw new InvalidOperationException("test"));
        timerQueue.Schedule(TimeSpan.FromMilliseconds(10), () => completed.TrySetResult());

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, timerQueue.FailedCallbackCount);
    }

    [TestMethod]
    public async Task TimerQueue_DisposeAsync_PreventsPendingCallbacksAndRejectsNewSchedules()
    {
        var timerQueue = new LibCommons.Timers.TimerQueue();
        int callbackCount = 0;

        timerQueue.Schedule(
            TimeSpan.FromMilliseconds(100),
            () => Interlocked.Increment(ref callbackCount));

        await timerQueue.DisposeAsync();
        await Task.Delay(150);

        Assert.AreEqual(0, callbackCount);
        Assert.ThrowsException<ObjectDisposedException>(() => timerQueue.Schedule(TimeSpan.Zero, () => { }));
    }
}
