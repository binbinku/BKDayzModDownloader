using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BKDownloader.Core;
using Microsoft.Win32;

namespace BKDayzModDownloader;

public partial class MainWindow : Window
{
    private readonly DownloadViewModel vm = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private AppSettings settings = new();
    private WorkerClient? worker;
    private CancellationTokenSource? operation;
    private ModItem? current;
    private bool loggingIn, downloading, loggedIn;
    private ulong networkBytes, lastNetworkBytes;
    private long speedTick;
    private readonly uint loginId = (uint)Random.Shared.Next(1, int.MaxValue);
    private string? logFile;
    private string account = "";
    private readonly bool previewMode = Environment.GetCommandLineArgs().Contains("--preview");

    public MainWindow()
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += Window_Loaded;
        timer.Tick += (_, _) => UpdateSpeed();
        SourceInitialized += (_, _) => RoundedWindow.Apply(this);
        SizeChanged += (_, _) => RoundedWindow.Apply(this);
        DpiChanged += (_, _) => RoundedWindow.Apply(this);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeGlyph is null) return;
        MaximizeGlyph.Data = Geometry.Parse(WindowState == WindowState.Maximized
            ? "M 2,2 L 2,0 L 10,0 L 10,8 L 8,8 M 0,3 L 7,3 L 7,10 L 0,10 Z"
            : "M 0,0 L 9,0 L 9,9 L 0,9 Z");
        WindowBorder.BorderThickness = new Thickness(WindowState == WindowState.Maximized ? 0 : 1);
        WindowBorder.CornerRadius = new CornerRadius(WindowState == WindowState.Maximized ? 0 : 10);
        RoundedWindow.Apply(this);
        AddModPopup.IsOpen = SettingsPopup.IsOpen = false;
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Apply(ThemeManager.Current == "Dark" ? "Light" : "Dark");
        ThemeButton.Content = ThemeManager.Current == "Dark" ? "亮色模式" : "暗色模式";
        SaveSettings();
    }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (AddModPopup.IsOpen || SettingsPopup.IsOpen))
        { AddModPopup.IsOpen = SettingsPopup.IsOpen = false; e.Handled = true; }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!previewMode)
            {
                Directory.CreateDirectory(Path.Combine(SettingsStore.DataDirectory, "logs"));
                logFile = Path.Combine(SettingsStore.DataDirectory, "logs", "client.log");
                RotateLog();
            }
            settings = SettingsStore.Load();
        }
        catch (Exception ex) { LoginStatus.Text = "无法读取配置：" + ex.Message; }
        UsernameBox.Text = settings.Username;
        ThemeManager.Apply(settings.Theme);
        ThemeButton.Content = ThemeManager.Current == "Dark" ? "亮色模式" : "暗色模式";
        RememberBox.IsChecked = settings.RememberSession;
        RootBox.Text = string.IsNullOrWhiteSpace(settings.DownloadRoot) ? Path.Combine(AppContext.BaseDirectory, "Downloads") : settings.DownloadRoot;
        ValidateBox.IsChecked = settings.Validate;
        RetryAutoBox.IsChecked = settings.Retries > 0;
        foreach (ComboBoxItem item in ChunksBox.Items)
            if (item.Content.ToString() == settings.ChunkConcurrency.ToString(CultureInfo.InvariantCulture)) ChunksBox.SelectedItem = item;
        foreach (var mod in settings.Mods.Where(m => m.Id != 0).DistinctBy(m => m.Id))
        {
            if (string.IsNullOrWhiteSpace(mod.Folder) || mod.Folder.EndsWith($"_{mod.Id}", StringComparison.Ordinal))
                mod.Folder = ModList.FolderName(mod.Name, mod.Id);
            vm.Mods.Add(mod);
        }
        if (vm.Mods.Count == 0)
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "ModList.txt");
            if (File.Exists(bundled))
            {
                foreach (var mod in ModList.Parse(File.ReadLines(bundled)).Mods)
                { mod.Selected = false; vm.Mods.Add(mod); }
            }
        }
        Log("客户端已就绪。登录后可使用 Mod ID 下载，或导入批量清单。");
        if (previewMode)
        {
            Title = "BK DayZ Mod Downloader · 离线预览";
            LoginPage.Visibility = Visibility.Collapsed;
            DownloadPage.Visibility = Visibility.Visible;
            AccountLabel.Text = "离线界面预览 · 示例数据，未连接 Steam";
            vm.CurrentTitle = "离线界面预览";
            vm.CurrentDetail = "点击右下角 ＋ 添加模组；实际下载前需要登录 Steam。";
            if (vm.Mods.Count >= 3)
            {
                vm.Mods[0].State = DownloadState.Completed; vm.Mods[0].Progress = 100; vm.Mods[0].Detail = "下载及校验完成";
                vm.Mods[1].State = DownloadState.Downloading; vm.Mods[1].Progress = 37.4; vm.Mods[1].SpeedText = "3.6 MiB/s"; vm.Mods[1].Detail = "74.8 MiB / 200.0 MiB";
                vm.Mods[2].State = DownloadState.Paused; vm.Mods[2].Progress = 64; vm.Mods[2].Detail = "点击下载图标继续";
            }
        }
        RefreshQueueSummary();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (loggingIn || loggedIn) return;
        account = UsernameBox.Text.Trim();
        if (string.IsNullOrEmpty(account)) { LoginStatus.Text = "请输入 Steam 账号。"; return; }
        loggingIn = true;
        LoginButton.IsEnabled = false;
        UsernameBox.IsEnabled = PasswordInput.IsEnabled = RememberBox.IsEnabled = false;
        LoginProgress.Visibility = CancelLoginButton.Visibility = Visibility.Visible;
        LoginStatus.Text = "正在连接 Steam，首次连接可能需要一些时间…";
        operation = new();
        worker?.Dispose();
        worker = new WorkerClient(HandleWorkerEvent, HandleAuth);
        var request = new DownloadRequest(account, PasswordInput.Password, 0, "", RememberBox.IsChecked == true, true, 8, loginId);
        PasswordInput.Clear();
        try
        {
            await worker.LoginAsync(request, operation.Token);
            loggedIn = true;
            AccountLabel.Text = $"{account} · DayZ 创意工坊 · App 221100";
            LoginPage.Visibility = Visibility.Collapsed;
            DownloadPage.Visibility = Visibility.Visible;
            vm.CurrentTitle = "登录成功，准备下载";
            vm.CurrentDetail = "输入 Mod ID，例如 1559212036；文件夹以名称和 ID 命名。";
            SaveSettings();
        }
        catch (OperationCanceledException) { LoginStatus.Text = "登录已取消。"; worker?.Dispose(); }
        catch (Exception ex) { LoginStatus.Text = ex.Message; Log("登录失败：" + ex.Message); worker?.Dispose(); }
        finally
        {
            request = request with { Password = "" };
            loggingIn = false;
            LoginButton.IsEnabled = true;
            UsernameBox.IsEnabled = PasswordInput.IsEnabled = RememberBox.IsEnabled = true;
            LoginProgress.Visibility = CancelLoginButton.Visibility = Visibility.Collapsed;
            operation?.Dispose(); operation = null;
        }
    }

    private Task HandleWorkerEvent(WorkerEvent message) => Dispatcher.InvokeAsync(() =>
    {
        switch (message.Type)
        {
            case "progress":
                networkBytes = Math.Max(networkBytes, message.NetworkBytes);
                if (current?.State is DownloadState.Pausing or DownloadState.Paused or DownloadState.Canceling) break;
                vm.Indeterminate = message.Percent < 0;
                if (current is not null)
                {
                    current.Indeterminate = message.Percent < 0;
                    current.State = DownloadState.Downloading;
                    current.Detail = message.Total == 0
                        ? $"已接收 {DownloadViewModel.Bytes(message.Completed)}"
                        : $"{DownloadViewModel.Bytes(message.Completed)} / {DownloadViewModel.Bytes(message.Total)}";
                }
                if (message.Percent >= 0)
                {
                    vm.Percent = message.Percent;
                    if (current is not null) current.Progress = message.Percent;
                }
                vm.CurrentDetail = $"{message.Percent:0.0}% · 已处理 {DownloadViewModel.Bytes(message.Completed)} / {DownloadViewModel.Bytes(message.Total)}";
                break;
            case "stage":
                if (loggingIn) LoginStatus.Text = message.Message;
                else
                {
                    vm.CurrentDetail = message.Message; vm.Indeterminate = true;
                    if (current is not null && current.State is not (DownloadState.Paused or DownloadState.Pausing or DownloadState.Canceling))
                    {
                        current.Indeterminate = true;
                        current.State = DownloadState.Preparing;
                        current.Detail = message.Message;
                    }
                }
                Log(message.Message);
                break;
            case "log":
                Log(message.Message);
                break;
            case "authenticated": Log("Steam 登录成功。"); break;
            case "name":
                if (current is not null)
                {
                    current.Name = message.Message;
                    vm.CurrentTitle = $"正在下载 · {message.Message}";
                    // Name is metadata; the destination folder remains stable for resumable downloads.
                }
                break;
            case "destination":
                if (current is not null) current.Folder = Path.GetFileName(message.Message);
                Log($"保存目录：{message.Message}");
                break;
            case "complete": Log(message.Message); break;
            case "paused":
                if (current is not null && current.State != DownloadState.Canceling)
                { current.State = DownloadState.Paused; current.Indeterminate = false; current.SpeedText = "—"; current.Detail = "点击下载图标继续"; }
                vm.CurrentDetail = "当前任务已暂停，继续或取消后将处理后续任务。";
                break;
            case "resumed":
                lastNetworkBytes = networkBytes; speedTick = Environment.TickCount64;
                if (current is not null && current.State != DownloadState.Canceling)
                { current.State = DownloadState.Downloading; current.Detail = "继续下载"; }
                break;
            case "canceled": Log(message.Message); break;
            case "error": Log("错误：" + message.Message); break;
            case "disconnected":
                loggedIn = false;
                if (!downloading && !loggingIn) ShowLogin(message.Message);
                break;
        }
    }).Task;

    private Task<string?> HandleAuth(WorkerEvent message) => Dispatcher.InvokeAsync(() =>
    {
        if (operation?.IsCancellationRequested != false) return null;
        var dialog = new AuthDialog(message.Message, this);
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }).Task;

    private ModItem? AddFromInput()
    {
        var text = ModIdBox.Text.Trim();
        if (text.Length == 0) { Notice("请输入 Mod ID，或粘贴 Steam 工坊链接。"); return null; }
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            if (!uri.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase))
            { Notice("请输入 steamcommunity.com 工坊链接或数字 ID。"); return null; }
            text = Regex.Match(uri.Query, @"(?:\?|&)id=(\d+)(?:&|$)").Groups[1].Value;
        }
        if (!ulong.TryParse(text, out var id) || id == 0) { Notice("Mod ID 必须是大于 0 的数字。"); return null; }
        var existing = vm.Mods.FirstOrDefault(m => m.Id == id);
        if (existing is not null) { existing.Selected = true; ModGrid.ScrollIntoView(existing); ModIdBox.Clear(); return existing; }
        var mod = new ModItem { Name = $"Workshop {id}", Id = id, Folder = ModList.FolderName($"Workshop {id}", id) };
        vm.Mods.Add(mod);
        ModGrid.ScrollIntoView(mod);
        ModIdBox.Clear();
        SaveSettings();
        RefreshQueueSummary();
        return mod;
    }

    private void ShowAddMod_Click(object sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = false;
        AddModPopup.IsOpen = !AddModPopup.IsOpen;
        if (AddModPopup.IsOpen) Dispatcher.BeginInvoke(() => ModIdBox.Focus(), DispatcherPriority.Input);
    }
    private void ToggleLogs_Click(object sender, RoutedEventArgs e)
    {
        var show = LogPanel.Visibility != Visibility.Visible;
        LogPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LogPanelColumn.Width = new GridLength(show ? Math.Clamp(ActualWidth - 760, 240, 340) : 0);
        LogGapColumn.Width = new GridLength(show ? 14 : 0);
        LogsButton.Content = show ? "收起日志" : "日志";
        if (show) LogBox.ScrollToEnd();
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        AddModPopup.IsOpen = false;
        SettingsPopup.IsOpen = !SettingsPopup.IsOpen;
    }
    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = false;
        SaveSettings();
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (AddFromInput() is not null) AddModPopup.IsOpen = false;
    }
    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var mod = AddFromInput();
        if (mod is not null)
        {
            AddModPopup.IsOpen = false;
            await RunQueueAsync([mod]);
        }
    }
    private static string? FindServerRoot(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DayZServer_x64.exe"))) return directory.FullName;
        return null;
    }
    private void UpdateSpeed()
    {
        var now = Environment.TickCount64;
        var seconds = (now - speedTick) / 1000d;
        if (seconds <= 0) return;
        vm.SpeedText = DownloadViewModel.Bytes((networkBytes - lastNetworkBytes) / seconds) + "/s";
        if (current is not null && current.State is DownloadState.Preparing or DownloadState.Downloading) current.SpeedText = vm.SpeedText;
        lastNetworkBytes = networkBytes;
        speedTick = now;
    }
    private void SetBusy(bool busy)
    {
        SettingsButton.IsEnabled = !busy;
        RootBox.IsEnabled = ChunksBox.IsEnabled = ValidateBox.IsEnabled = RetryAutoBox.IsEnabled = !busy;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { operation?.Cancel(); worker?.Dispose(); loggedIn = false; }
    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (downloading) { Notice("请先取消当前下载，再退出登录。"); return; }
        worker?.Dispose(); loggedIn = false;
        ShowLogin("已退出当前会话。可重新登录，或清除保存的登录状态。");
    }
    private void ShowLogin(string message)
    {
        AddModPopup.IsOpen = SettingsPopup.IsOpen = false;
        loggedIn = false;
        DownloadPage.Visibility = Visibility.Collapsed;
        LoginPage.Visibility = Visibility.Visible;
        LoginStatus.Text = message;
    }
    private void Forget_Click(object sender, RoutedEventArgs e)
    {
        if (loggingIn || downloading) return;
        try
        {
            var file = Path.Combine(SettingsStore.DataDirectory, "steam-session.dpapi");
            if (File.Exists(file)) File.Delete(file);
            RememberBox.IsChecked = false;
            SaveSettings();
            RefreshQueueSummary();
            LoginStatus.Text = "已清除本工具保存的 Steam 登录状态。";
        }
        catch (Exception ex) { LoginStatus.Text = ex.Message; }
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (downloading) return;
        var dialog = new OpenFolderDialog { Title = "选择模组下载目录", Multiselect = false };
        if (Directory.Exists(RootBox.Text)) dialog.InitialDirectory = RootBox.Text;
        if (dialog.ShowDialog(this) == true) { RootBox.Text = dialog.FolderName; SaveSettings(); }
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "模组清单 (*.txt)|*.txt|所有文件 (*.*)|*.*", Title = "导入名称 ID 或 SteamCMD 清单" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var parsed = ModList.Parse(File.ReadLines(dialog.FileName));
            int added = 0;
            foreach (var mod in parsed.Mods)
            {
                var existing = vm.Mods.FirstOrDefault(m => m.Id == mod.Id);
                if (existing is not null) { existing.Selected = true; continue; }
                vm.Mods.Add(mod); added++;
            }
            Log($"导入完成：新增 {added} 个，跳过无效 ID 或重复项 {parsed.Skipped} 个。");
            SaveSettings();
        }
        catch (Exception ex) { Notice("导入失败：" + ex.Message); }
    }
    private void OpenDownloads_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var configuredPath = RootBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                Notice("请先在下载设置中选择保存目录。");
                return;
            }
            var path = Path.GetFullPath(configuredPath);
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Notice("无法打开下载目录：" + ex.Message); }
    }
    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(SettingsStore.DataDirectory, "logs");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = true });
        }
        catch (Exception ex) { Notice(ex.Message); }
    }
    private void SaveSettings()
    {
        if (previewMode) return;
        try
        {
            settings.Username = UsernameBox.Text.Trim();
            settings.RememberSession = RememberBox.IsChecked == true;
            settings.DownloadRoot = RootBox.Text.Trim();
            settings.Validate = ValidateBox.IsChecked == true;
            settings.Theme = ThemeManager.Current;
            settings.Retries = RetryAutoBox.IsChecked == true ? 3 : 0;
            settings.ChunkConcurrency = int.Parse(((ComboBoxItem)ChunksBox.SelectedItem).Content.ToString()!, CultureInfo.InvariantCulture);
            settings.Mods = vm.Mods.ToList();
            SettingsStore.Save(settings);
        }
        catch (Exception ex) { Log("保存配置失败：" + ex.Message); }
    }
    private void Root_LostFocus(object sender, RoutedEventArgs e) => SaveSettings();
    private void Password_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Login_Click(sender, e); }
    private void ModId_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Add_Click(sender, e); }
    private void Notice(string message) => MessageBox.Show(this, message, "BK DayZ Mod Downloader", MessageBoxButton.OK, MessageBoxImage.Information);
    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogBox.AppendText(line + Environment.NewLine);
        if (LogBox.LineCount > 1600) LogBox.Text = LogBox.Text[^Math.Min(60000, LogBox.Text.Length)..];
        LogBox.ScrollToEnd();
        if (logFile is null) return;
        try { RotateLog(); File.AppendAllText(logFile, line + Environment.NewLine); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void RotateLog()
    {
        if (logFile is not null && File.Exists(logFile) && new FileInfo(logFile).Length > 4 * 1024 * 1024)
            File.Move(logFile, logFile + ".previous", true);
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if ((loggingIn || downloading) && MessageBox.Show(this, "关闭窗口将取消当前登录或下载，是否继续？", "退出", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        { e.Cancel = true; return; }
        SaveSettings();
        operation?.Cancel(); worker?.Dispose(); timer.Stop();
    }
}
