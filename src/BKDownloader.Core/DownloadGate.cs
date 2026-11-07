namespace BKDownloader.Core;

// Cooperative pause: keep Steam callbacks alive, stop scheduling more content work.
public sealed class DownloadGate : IDisposable
{
    private readonly object sync = new();
    private CancellationTokenSource cancellation = new();
    private TaskCompletionSource signal = OpenSignal();
    public CancellationToken Token => cancellation.Token;
    public bool IsPaused { get { lock (sync) return !signal.Task.IsCompleted; } }
    private static TaskCompletionSource OpenSignal()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        result.SetResult(); return result;
    }
    public void Pause() { lock (sync) if (signal.Task.IsCompleted) signal = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    public void Resume() { lock (sync) signal.TrySetResult(); }
    public Task WaitAsync()
    {
        lock (sync) { Token.ThrowIfCancellationRequested(); return signal.Task.WaitAsync(Token); }
    }
    public void Cancel() { cancellation.Cancel(); Resume(); }
    public void Dispose() { Cancel(); cancellation.Dispose(); }
}
