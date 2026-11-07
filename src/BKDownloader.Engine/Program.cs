using System.Text;
using System.Text.Json;
using BKDownloader.Core;
using DepotDownloader;

namespace BKDownloader.Engine;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);
        Bridge.Initialize();
        if (args.Contains("--version")) { Bridge.Send(new("info", "DepotDownloader 3.4.0 / BK .NET 10 adapter")); return 0; }
        try
        {
            var request = JsonSerializer.Deserialize<DownloadRequest>(await Bridge.Input.ReadLineAsync() ?? "")
                ?? throw new ArgumentException("缺少下载请求。");
            if (request.Chunks is < 1 or > 32 || string.IsNullOrWhiteSpace(request.Username))
                throw new ArgumentException("下载参数不合法。");
            if (args.Contains("--self-test"))
            {
                _ = Bridge.Prompt("test", "离线协议验证");
                await RunCommandsAsync(offline: true);
                return 0;
            }
            AccountSettingsStore.PersistSession = request.RememberSession;
            AccountSettingsStore.LoadFromFile("account.config");
            var password = request.Password;
            if (string.IsNullOrEmpty(password) && (!request.RememberSession || !AccountSettingsStore.Instance.LoginTokens.ContainsKey(request.Username)))
                password = Bridge.Prompt("password", $"请输入 {request.Username} 的 Steam 密码");
            ContentDownloader.Config = new DownloadConfig
            {
                InstallDirectory = request.Directory, MaxDownloads = request.Chunks,
                VerifyAll = request.Validate, RememberPassword = request.RememberSession,
                LoginID = request.LoginId, SkipAppConfirmation = false
            };
            if (!ContentDownloader.InitializeSteam3(request.Username, password))
                throw new InvalidOperationException("Steam 登录失败，请检查账号、密码、Steam Guard 和网络。若保存的登录状态失效，请先清除登录状态。");
            await RunCommandsAsync(offline: false);
            return 0;
        }
        catch (Exception ex)
        {
            Bridge.Send(new("error", ex.Message));
            return 1;
        }
        finally { ContentDownloader.ShutdownSteam3(); }
    }

    private static async Task RunCommandsAsync(bool offline)
    {
        int busy = 0;
        Task active = Task.CompletedTask;
        Bridge.UseReplyPump = true;
        Bridge.Send(new("authenticated", offline ? "离线协议验证登录" : "Steam 登录成功"));
        try
        {
            while (await Bridge.Input.ReadLineAsync() is { } line)
            {
                using var json = JsonDocument.Parse(line);
                if (json.RootElement.TryGetProperty("Value", out _)) { Bridge.AuthReplies.Writer.TryWrite(line); continue; }
                if (json.RootElement.TryGetProperty("Operation", out var action))
                {
                    if (Volatile.Read(ref busy) == 0) continue;
                    switch (action.GetString())
                    {
                        case "pause": JobControl.Pause(); break;
                        case "resume": JobControl.Resume(); break;
                        case "cancel": JobControl.Cancel(); break;
                    }
                    continue;
                }
                var job = JsonSerializer.Deserialize<DownloadRequest>(line);
                if (job == null || job.ModId == 0 || !Path.IsPathFullyQualified(job.Directory) || job.Chunks is < 1 or > 32)
                { Bridge.Send(new("error", "下载参数不合法。")); continue; }
                if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
                { Bridge.Send(new("log", "已有下载任务正在执行。")); continue; }
                JobControl.Begin();
                active = Task.Run(async () =>
                {
                    WorkerEvent terminal;
                    try
                    {
                        if (offline)
                        {
                            if (job.ModId == 2) throw new IOException("离线模拟：下载失败");
                            if (job.ModId == 4) await Task.Delay(TimeSpan.FromSeconds(30), JobControl.Token);
                            if (job.ModId == 5)
                            {
                                for (ulong step = 1; step <= 100; step++)
                                {
                                    await JobControl.WaitAsync();
                                    await Task.Delay(30, JobControl.Token);
                                    if (!JobControl.IsPaused) Bridge.Send(new("progress", "离线传输", step, step, 100, step));
                                }
                            }
                            else Bridge.Send(new("progress", "离线协议进度", 50, 50, 100, 25));
                        }
                        else
                        {
                            ContentDownloader.Config.InstallDirectory = job.Directory;
                            FolderResolver.Root = job.DirectoryRoot;
                            FolderResolver.PreviousFolder = Path.GetFileName(job.Directory);
                            ContentDownloader.Config.VerifyAll = job.Validate;
                            ContentDownloader.Config.MaxDownloads = job.Chunks;
                            DepotConfigStore.Instance = null;
                            Bridge.Send(new("stage", "正在读取工坊清单 / 校验本地文件"));
                            await ContentDownloader.DownloadPubfileAsync(221100, job.ModId);
                        }
                        await JobControl.WaitAsync();
                        terminal = new("complete", "下载及校验完成", 100);
                    }
                    catch (OperationCanceledException) { terminal = new("canceled", "下载已取消，已有文件保留。"); }
                    catch (Exception ex) { terminal = new("error", ex.Message); }
                    finally { if (!offline) ContentDownloader.ReleaseDownloadResources(); }
                    Interlocked.Exchange(ref busy, 0);
                    Bridge.Send(terminal);
                });
            }
        }
        finally { JobControl.Cancel(); Bridge.AuthReplies.Writer.TryComplete(); await active; }
    }
}

internal static class Bridge
{
    private static TextWriter output;
    private static readonly object Sync = new();
    private static long lastProgress;
    public static volatile bool UseReplyPump;
    public static readonly System.Threading.Channels.Channel<string> AuthReplies = System.Threading.Channels.Channel.CreateUnbounded<string>();
    public static TextReader Input { get; private set; }
    public static void Initialize()
    {
        output = Console.Out;
        Input = Console.In;
        Console.SetOut(new LogWriter());
        Console.SetError(new LogWriter());
        Console.SetIn(new PromptReader());
    }
    public static void Send(WorkerEvent message)
    {
        lock (Sync) { output.WriteLine(JsonSerializer.Serialize(message)); output.Flush(); }
    }
    public static string Prompt(string kind, string message)
    {
        Send(new("auth", message, Kind: kind));
        var response = UseReplyPump ? AuthReplies.Reader.ReadAsync().AsTask().GetAwaiter().GetResult() : Input.ReadLine();
        if (response == null) throw new OperationCanceledException("验证已取消。");
        return JsonSerializer.Deserialize<AuthReply>(response)?.Value ?? throw new OperationCanceledException("验证已取消。");
    }
    public static void Progress(ulong done, ulong total, ulong network)
    {
        var now = Environment.TickCount64;
        if (JobControl.IsPaused || JobControl.Token.IsCancellationRequested) return;
        if (now - Interlocked.Read(ref lastProgress) < 250 && done < total) return;
        Interlocked.Exchange(ref lastProgress, now);
        Send(new("progress", "正在下载", total == 0 ? -1 : Math.Clamp(done * 100d / total, 0, 100), done, total, network));
    }
    private sealed class PromptReader : TextReader
    {
        public override string ReadLine() => Prompt("code", "请输入 Steam Guard 验证码");
    }
    private sealed class LogWriter : TextWriter
    {
        private readonly StringBuilder buffer = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value)
        {
            lock (buffer)
            {
                if (value is '\r' or '\n')
                {
                    if (buffer.Length > 0) { Send(new("log", buffer.ToString())); buffer.Clear(); }
                }
                else if (!char.IsControl(value)) buffer.Append(value);
                if (buffer.Length > 4000) { Send(new("log", buffer.ToString())); buffer.Clear(); }
            }
        }
    }
}
