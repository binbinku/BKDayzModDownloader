using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using BKDownloader.Core;
using BKDownloader.Engine;
using SteamKit2.Authentication;

namespace DepotDownloader;

// Replacement for upstream console authentication, communicated through private pipes.
internal sealed class ConsoleAuthenticator : IAuthenticator
{
    public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) => Task.FromResult(Bridge.Prompt("code",
        (previousCodeWasIncorrect ? "验证码错误，请重新输入。\n" : "") + "请输入手机 Steam Guard 验证码"));
    public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) => Task.FromResult(Bridge.Prompt("code",
        (previousCodeWasIncorrect ? "验证码错误，请重新输入。\n" : "") + $"请输入发送到 {email} 的验证码"));
    public Task<bool> AcceptDeviceConfirmationAsync()
    {
        Bridge.Send(new("stage", "请在 Steam 手机应用中批准此次登录"));
        return Task.FromResult(true);
    }
}

internal static class PlatformUtilities
{
    public static void SetExecutable(string path, bool value) { } // Windows-only build.
}

internal static class Ansi
{
    public enum ProgressState { Hidden, Default, Error, Indeterminate, Warning }
    public static void Progress(ulong done, ulong total, ulong network = 0) => Bridge.Progress(done, total, network);
    public static void Progress(ProgressState state, byte progress = 0) { }
}

// Preserve upstream's API, but encrypt login tokens for the current Windows user.
internal sealed class AccountSettingsStore
{
    public ConcurrentDictionary<string, int> ContentServerPenalty { get; set; } = new();
    public Dictionary<string, string> LoginTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> GuardData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public static AccountSettingsStore Instance;
    public static bool PersistSession;
    private static string TokenFile => Path.Combine(SettingsStore.DataDirectory, "steam-session.dpapi");
    public static void LoadFromFile(string filename)
    {
        Instance = new();
        if (!PersistSession || !File.Exists(TokenFile)) return;
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(TokenFile), null, DataProtectionScope.CurrentUser);
            try { Instance = JsonSerializer.Deserialize<AccountSettingsStore>(plain) ?? new(); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException)
        { Bridge.Send(new("log", "无法读取保存的登录状态，将重新登录。")); }
    }
    public static void Save()
    {
        if (!PersistSession) return;
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        var plain = JsonSerializer.SerializeToUtf8Bytes(Instance);
        try
        {
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenFile + ".tmp", encrypted);
            File.Move(TokenFile + ".tmp", TokenFile, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
