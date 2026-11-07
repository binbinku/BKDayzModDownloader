using BKDownloader.Core;

namespace BKDownloader.Engine;

internal static class JobControl
{
    private static DownloadGate gate = new();
    public static CancellationToken Token => gate.Token;
    public static bool IsPaused => gate.IsPaused;
    public static void Begin() { gate.Dispose(); gate = new(); }
    public static Task WaitAsync() => gate.WaitAsync();
    public static void Pause() { gate.Pause(); Bridge.Send(new("paused", "已暂停；正在传输的数据块可能仍会完成。")); }
    public static void Resume() { gate.Resume(); Bridge.Send(new("resumed", "继续下载")); }
    public static void Cancel() => gate.Cancel();
}
