using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using BKDownloader.Core;

namespace BKDayzModDownloader;

public partial class MainWindow
{
    private readonly List<ModItem> waiting = [];
    private readonly HashSet<ModItem> removeAfterCancel = [];
    private bool jobInFlight;

    private async void StartQueue_Click(object sender, RoutedEventArgs e) => await RunQueueAsync(vm.Mods.Where(m => m.Selected).ToArray());
    private async void PauseQueue_Click(object sender, RoutedEventArgs e) => await PauseItemsAsync(vm.Mods.Where(m => m.Selected).ToArray());
    private async void CancelQueue_Click(object sender, RoutedEventArgs e) => await CancelItemsAsync(vm.Mods.Where(m => m.Selected).ToArray(), false);
    private async void DeleteQueue_Click(object sender, RoutedEventArgs e) => await CancelItemsAsync(vm.Mods.Where(m => m.Selected).ToArray(), true);
    private async void RowStart_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is ModItem mod) await RunQueueAsync([mod]); }
    private async void RowPause_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is ModItem mod) await PauseItemsAsync([mod]); }
    private async void RowCancel_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is ModItem mod) await CancelItemsAsync([mod], false); }
    private async void RowDelete_Click(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).DataContext is ModItem mod) await CancelItemsAsync([mod], true); }
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var selected = ((CheckBox)sender).IsChecked == true;
        foreach (var item in vm.Mods) item.Selected = selected;
        SaveSettings();
    }

    private async Task RunQueueAsync(ModItem[] items)
    {
        if (!loggedIn || worker is not { IsRunning: true }) { ShowLogin("请先登录 Steam。"); return; }
        if (items.Length == 0) { Notice("请先勾选需要下载的模组。"); return; }
        try
        {
            foreach (var mod in items)
            {
                if (mod == current)
                {
                    if (mod.State == DownloadState.Paused)
                    {
                        if (jobInFlight) await worker.ControlAsync("resume");
                        else mod.State = DownloadState.Retrying;
                    }
                    continue;
                }
                if (!mod.CanStart && mod.State != DownloadState.Queued) continue;
                _ = ValidateDestination(mod);
                mod.State = DownloadState.Queued; mod.Detail = "等待下载"; mod.Error = "";
                if (!waiting.Contains(mod)) waiting.Add(mod);
            }
        }
        catch (Exception ex) { Notice(ex.Message); return; }
        SaveSettings();
        if (!downloading) await PumpQueueAsync();
    }

    private string ValidateDestination(ModItem item)
    {
        if (string.IsNullOrWhiteSpace(RootBox.Text)) throw new ArgumentException("请先设置下载目录。");
        var root = Path.GetFullPath(RootBox.Text.Trim());
        var destination = ModList.Destination(root, item.Folder);
        if (vm.Mods.Any(m => m != item && string.Equals(m.Folder, item.Folder, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("不同模组的目标文件夹不能相同。");
        if (FindServerRoot(root) is not null && System.Diagnostics.Process.GetProcessesByName("DayZServer_x64").Length > 0)
            throw new IOException("DayZ 服务器正在运行，请先停服或使用独立下载目录。");
        Directory.CreateDirectory(root);
        return destination;
    }

    private async Task PauseItemsAsync(ModItem[] items)
    {
        try
        {
            foreach (var item in items.Where(m => m.CanPause))
            {
                item.SpeedText = "—"; item.Indeterminate = false;
                if (item == current && jobInFlight)
                {
                    item.State = DownloadState.Pausing;
                    await worker!.ControlAsync("pause");
                }
                else item.State = DownloadState.Paused;
                item.Detail = "已保留进度，点击下载图标继续";
            }
        }
        catch (Exception ex) { Notice(ex.Message); }
    }

    private async Task CancelItemsAsync(ModItem[] items, bool remove)
    {
        try
        {
            foreach (var item in items)
            {
                if (item == current)
                {
                    if (remove) removeAfterCancel.Add(item);
                    item.State = DownloadState.Canceling; item.Detail = "正在结束传输，已有文件保留";
                    item.SpeedText = "—"; item.Indeterminate = false;
                    if (jobInFlight) await worker!.ControlAsync("cancel");
                    continue;
                }
                waiting.Remove(item);
                if (remove) vm.Mods.Remove(item);
                else if (item.CanCancel)
                { item.State = DownloadState.Canceled; item.Detail = "已取消，已有文件保留"; item.Indeterminate = false; item.SpeedText = "—"; }
            }
            RefreshQueueSummary(); SaveSettings();
        }
        catch (Exception ex) { Notice(ex.Message); }
    }

    private async Task PumpQueueAsync()
    {
        downloading = true; operation = new();
        var token = operation.Token;
        SetBusy(true);
        AddModPopup.IsOpen = SettingsPopup.IsOpen = false;
        try
        {
            while (waiting.FirstOrDefault(m => m.State == DownloadState.Queued) is { } item)
            {
                waiting.Remove(item); current = item;
                token.ThrowIfCancellationRequested();
                var retries = RetryAutoBox.IsChecked == true ? 3 : 0;
                for (int attempt = 0; attempt <= retries; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    if (item.State == DownloadState.Canceling) { item.State = DownloadState.Canceled; break; }
                    item.State = DownloadState.Preparing; item.Indeterminate = true;
                    item.Detail = "读取清单与检查本地文件"; item.Error = ""; item.SpeedText = "0 B/s";
                    networkBytes = lastNetworkBytes = 0; speedTick = Environment.TickCount64;
                    timer.Start();
                    try
                    {
                        var destination = ValidateDestination(item);
                        var chunks = int.Parse(((ComboBoxItem)ChunksBox.SelectedItem).Content.ToString()!, CultureInfo.InvariantCulture);
                        Log($"开始 {item.Name} ({item.Id}) → {destination}");
                        jobInFlight = true;
                        await worker!.DownloadAsync(new(account, "", item.Id, destination, RememberBox.IsChecked == true, ValidateBox.IsChecked == true, chunks, loginId,
                            Path.GetFullPath(RootBox.Text.Trim())), token);
                        item.State = DownloadState.Completed; item.Progress = 100; item.Detail = "下载及校验完成";
                        break;
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        item.State = DownloadState.Canceled; item.Detail = "已取消，已有文件保留";
                        break;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        item.Error = item.Detail = ex.Message;
                        Log($"{item.Name}：{ex.Message}");
                        if (item.State == DownloadState.Canceling) { item.State = DownloadState.Canceled; break; }
                        if (attempt == retries || worker is not { IsRunning: true }) { item.State = DownloadState.Failed; break; }
                        jobInFlight = false; item.State = DownloadState.Retrying;
                        timer.Stop(); item.SpeedText = "—"; item.Indeterminate = false;
                        for (int seconds = 30; seconds > 0; seconds--)
                        {
                            while (item.State == DownloadState.Paused) await Task.Delay(100, token);
                            if (item.State == DownloadState.Canceling) break;
                            item.Detail = $"{seconds} 秒后重试 · {attempt + 1}/{retries}";
                            await Task.Delay(1000, token);
                        }
                    }
                    finally { jobInFlight = false; timer.Stop(); item.SpeedText = "—"; item.Indeterminate = false; }
                }
                if (removeAfterCancel.Remove(item)) vm.Mods.Remove(item);
                current = null; RefreshQueueSummary();
                if (worker is not { IsRunning: true }) throw new IOException("下载会话已断开，请重新登录。");
            }
            vm.CurrentDetail = waiting.Any(m => m.State == DownloadState.Paused) ? "部分任务已暂停，可点击行内下载图标继续。" : "队列已就绪。";
        }
        catch (OperationCanceledException) { if (current is not null) current.State = DownloadState.Canceled; }
        catch (Exception ex)
        {
            if (current is not null) { current.State = DownloadState.Failed; current.Error = current.Detail = ex.Message; }
            Log(ex.Message); vm.CurrentDetail = ex.Message;
        }
        finally
        {
            if (current is not null)
            {
                current.SpeedText = "—"; current.Indeterminate = false;
                if (removeAfterCancel.Remove(current)) vm.Mods.Remove(current);
            }
            current = null; jobInFlight = false; downloading = false; timer.Stop();
            SetBusy(false); operation.Dispose(); operation = null;
            RefreshQueueSummary(); SaveSettings();
            if (worker is not { IsRunning: true }) ShowLogin("下载会话已结束，请重新登录。");
        }
    }
    private void RefreshQueueSummary() => vm.QueueText = $"{vm.Mods.Count(m => m.State == DownloadState.Completed)} / {vm.Mods.Count}";
}
