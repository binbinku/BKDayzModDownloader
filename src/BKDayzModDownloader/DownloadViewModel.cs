using System.Collections.ObjectModel;
using BKDownloader.Core;

namespace BKDayzModDownloader;

public sealed class DownloadViewModel : Observable
{
    public ObservableCollection<ModItem> Mods { get; } = [];
    private string title = "准备下载";
    private string detail = "输入 Mod ID 或导入模组清单";
    private double percent;
    private bool indeterminate;
    private string speed = "0 B/s";
    private string queue = "0 / 0";
    public string CurrentTitle { get => title; set => Set(ref title, value); }
    public string CurrentDetail { get => detail; set => Set(ref detail, value); }
    public double Percent { get => percent; set => Set(ref percent, value); }
    public bool Indeterminate { get => indeterminate; set => Set(ref indeterminate, value); }
    public string SpeedText { get => speed; set => Set(ref speed, value); }
    public string QueueText { get => queue; set => Set(ref queue, value); }
    public static string Bytes(double value)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.0} {units[unit]}";
    }
}
