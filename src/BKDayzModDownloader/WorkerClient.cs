using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BKDownloader.Core;

namespace BKDayzModDownloader;

internal sealed class WorkerClient : IDisposable
{
    private Process? process;
    private TaskCompletionSource? pending;
    private readonly Func<WorkerEvent, Task> onEvent;
    private readonly Func<WorkerEvent, Task<string?>> onAuth;
    private readonly string executable;
    private readonly bool offlineTest;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private volatile bool disposed;
    public WorkerClient(Func<WorkerEvent, Task> onEvent, Func<WorkerEvent, Task<string?>> onAuth,
        string? enginePath = null, bool offlineTest = false)
    {
        this.onEvent = onEvent; this.onAuth = onAuth;
        executable = enginePath ?? Path.Combine(AppContext.BaseDirectory, "Engine", "BKDownloader.Engine.exe");
        this.offlineTest = offlineTest;
    }
    public bool IsRunning => !disposed && process is { HasExited: false };
    public Task ControlAsync(string operation) => WriteAsync(new WorkerControl(operation));

    public async Task LoginAsync(DownloadRequest request, CancellationToken token)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("下载引擎缺失，请使用完整发布文件夹。", executable);
        process = new Process
        {
            StartInfo = new(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = SettingsStore.DataDirectory
            }
        };
        if (offlineTest) process.StartInfo.ArgumentList.Add("--self-test");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        if (!process.Start()) throw new IOException("无法启动下载引擎。");
        _ = ReadOutputAsync(process);
        _ = ReadErrorsAsync(process);
        _ = WatchExitAsync(process);
        await WriteAsync(request);
        using var registration = token.Register(Dispose);
        try { await pending.Task.WaitAsync(token); }
        catch (OperationCanceledException) { Dispose(); throw; }
    }

    public async Task DownloadAsync(DownloadRequest request, CancellationToken token)
    {
        if (!IsRunning) throw new IOException("Steam 会话已断开，请重新登录。");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await WriteAsync(request);
        using var registration = token.Register(Dispose);
        try { await pending.Task.WaitAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Dispose(); throw; }
    }

    private async Task WriteAsync<T>(T value)
    {
        await writeLock.WaitAsync();
        try
        {
            if (process is null || process.HasExited) throw new IOException("下载引擎已退出。");
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value));
            await process.StandardInput.FlushAsync();
        }
        finally { writeLock.Release(); }
    }
    private async Task ReadOutputAsync(Process child)
    {
        try
        {
            while (await child.StandardOutput.ReadLineAsync() is { } line)
            {
                if (disposed) break;
                var message = JsonSerializer.Deserialize<WorkerEvent>(line);
                if (message is null) continue;
                if (message.Type == "auth")
                {
                    var reply = await onAuth(message);
                    if (reply is null) { Dispose(); break; }
                    await WriteAsync(new AuthReply(reply));
                }
                else
                {
                    await onEvent(message);
                    if (message.Type is "authenticated" or "complete") pending?.TrySetResult();
                    if (message.Type == "error") pending?.TrySetException(new IOException(message.Message));
                    if (message.Type == "canceled") pending?.TrySetCanceled();
                }
            }
        }
        catch (Exception ex) { pending?.TrySetException(ex); }
        finally { pending?.TrySetException(new IOException("下载引擎连接已关闭，请重新登录。")); }
    }
    private async Task ReadErrorsAsync(Process child)
    {
        try
        {
            while (await child.StandardError.ReadLineAsync() is { } line)
                if (!disposed) await onEvent(new("log", line));
        }
        catch (Exception) { /* Process may be disposed during cancellation. */ }
    }
    private async Task WatchExitAsync(Process child)
    {
        try
        {
            await child.WaitForExitAsync();
            pending?.TrySetException(new IOException($"下载引擎已退出（代码 {child.ExitCode}），请重新登录。"));
            if (!disposed) await onEvent(new("disconnected", "Steam 会话已结束，请重新登录。"));
        }
        catch (Exception ex) { pending?.TrySetException(ex); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pending?.TrySetCanceled();
        try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        process?.Dispose();
    }
}
