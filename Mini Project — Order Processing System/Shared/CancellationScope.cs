namespace OrderProcessing.Shared;

/// <summary>
/// Manages cancellation for phase runs, handling both Ctrl+C (Console.CancelKeyPress)
/// and automatic timeout (--cancel-after <ms>).
/// Fulfills requirements 6.6 R2 and R3.
/// </summary>
public sealed class CancellationScope : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly ConsoleCancelEventHandler _cancelHandler;
    private bool _disposed;

    public CancellationToken Token => _cts.Token;
    public bool IsCancellationRequested => _cts.IsCancellationRequested;

    public CancellationScope(int? cancelAfterMs = null)
    {
        _cts = cancelAfterMs is > 0
            ? new CancellationTokenSource(cancelAfterMs.Value)
            : new CancellationTokenSource();

        _cancelHandler = (sender, e) =>
        {
            e.Cancel = true; // Prevent abrupt process termination; allow graceful rollback/cancellation
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        };

        Console.CancelKeyPress += _cancelHandler;
    }

    public void Cancel()
    {
        if (!_disposed && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Console.CancelKeyPress -= _cancelHandler;
        _cts.Dispose();
    }
}
