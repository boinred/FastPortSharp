namespace LibNetworks.Sessions;

internal sealed class SendCompletionTracker
{
    private readonly Queue<int> _pendingRequestBytes = new();
    private readonly object _lock = new();
    private int _currentRequestRemainingBytes;

    public void Enqueue(int bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        lock (_lock)
        {
            _pendingRequestBytes.Enqueue(bytes);
        }
    }

    public int Complete(int drainedBytes)
    {
        if (drainedBytes <= 0)
        {
            return 0;
        }

        int completedRequests = 0;
        int remainingBytes = drainedBytes;

        lock (_lock)
        {
            while (remainingBytes > 0)
            {
                if (_currentRequestRemainingBytes <= 0)
                {
                    if (_pendingRequestBytes.Count == 0)
                    {
                        return completedRequests;
                    }

                    _currentRequestRemainingBytes = _pendingRequestBytes.Dequeue();
                }

                int consumedBytes = Math.Min(_currentRequestRemainingBytes, remainingBytes);
                _currentRequestRemainingBytes -= consumedBytes;
                remainingBytes -= consumedBytes;

                if (_currentRequestRemainingBytes == 0)
                {
                    completedRequests++;
                }
            }
        }

        return completedRequests;
    }
}
