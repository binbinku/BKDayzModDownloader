using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace BKDownloader.Core;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
    }
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public enum DownloadState { Idle, Queued, Preparing, Downloading, Pausing, Paused, Retrying, Canceling, Canceled, Completed, Failed }

public sealed class ModItem : Observable
{
    private bool selected = true;
    private DownloadState state;
    private string name = "";
    private double progress;
    private string error = "";
    private string speedText = "—";
    private string detail = "等待开始";
    private bool indeterminate;
    public string Name { get => name; set => Set(ref name, value); }
    public ulong Id { get; set; }
    public string Folder { get; set; } = "";
    public bool Selected { get => selected; set => Set(ref selected, value); }
    [JsonIgnore] public DownloadState State
    {
        get => state;
        set
        {
            Set(ref state, value);
            foreach (var property in new[] { nameof(Status), nameof(CanStart), nameof(CanPause), nameof(CanCancel), nameof(CanDelete), nameof(ShowProgress), nameof(StartTooltip) }) Raise(property);
        }
    }
    [JsonIgnore] public string Status => State switch
    {
        DownloadState.Queued => "排队中", DownloadState.Preparing => "校验中", DownloadState.Downloading => "下载中",
        DownloadState.Pausing => "暂停中", DownloadState.Paused => "已暂停", DownloadState.Retrying => "等待重试",
        DownloadState.Canceling => "取消中", DownloadState.Canceled => "已取消", DownloadState.Completed => "已完成",
        DownloadState.Failed => "失败", _ => "待下载"
    };
    [JsonIgnore] public bool CanStart => State is DownloadState.Idle or DownloadState.Paused or DownloadState.Canceled or DownloadState.Completed or DownloadState.Failed;
    [JsonIgnore] public bool CanPause => State is DownloadState.Queued or DownloadState.Preparing or DownloadState.Downloading or DownloadState.Retrying;
    [JsonIgnore] public bool CanCancel => CanPause || State is DownloadState.Paused or DownloadState.Pausing;
    [JsonIgnore] public bool CanDelete => State != DownloadState.Canceling;
    [JsonIgnore] public bool ShowProgress => State is not (DownloadState.Idle or DownloadState.Queued or DownloadState.Canceled or DownloadState.Failed);
    [JsonIgnore] public string StartTooltip => State == DownloadState.Paused ? "继续下载" : "下载模组";
    [JsonIgnore] public double Progress { get => progress; set { Set(ref progress, value); Raise(nameof(ProgressLabel)); } }
    [JsonIgnore] public string Error { get => error; set => Set(ref error, value); }
    [JsonIgnore] public string SpeedText { get => speedText; set => Set(ref speedText, value); }
    [JsonIgnore] public string Detail { get => detail; set => Set(ref detail, value); }
    [JsonIgnore] public bool Indeterminate { get => indeterminate; set { Set(ref indeterminate, value); Raise(nameof(ProgressLabel)); } }
    [JsonIgnore] public string ProgressLabel => Indeterminate ? "…" : $"{Progress:0.0}%";
}

public sealed class AppSettings
{
    public string Username { get; set; } = "";
    public string DownloadRoot { get; set; } = "";
    public int Retries { get; set; } = 3;
    public int RetrySeconds { get; set; } = 30;
    public int ChunkConcurrency { get; set; } = 8;
    public bool RememberSession { get; set; }
    public bool Validate { get; set; } = true;
    public string Theme { get; set; } = "Light";
    public List<ModItem> Mods { get; set; } = [];
}

// Sent through a private redirected stdin pipe, never command-line arguments.
public sealed record DownloadRequest(string Username, string Password, ulong ModId,
    string Directory, bool RememberSession, bool Validate, int Chunks, uint LoginId,
    string? DirectoryRoot = null);
public sealed record WorkerEvent(string Type, string Message = "", double Percent = -1,
    ulong Completed = 0, ulong Total = 0, ulong NetworkBytes = 0, string Kind = "");
public sealed record AuthReply(string Value);
public sealed record WorkerControl(string Operation);
