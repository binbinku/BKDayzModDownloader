using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BKDownloader.Core;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Reject(Action action, string name)
{
    try { action(); } catch (ArgumentException) { return; }
    throw new Exception("Expected rejection: " + name);
}
var parsed = ModList.Parse(["CF 1559212036", "MMG - Mightys Military Gear 2663169692", "wu0 0", "CF 1559212036"]);
Check(parsed.Mods.Count == 2 && parsed.Skipped == 2, "Named list deduplication / zero IDs");
Check(parsed.Mods[1].Name == "MMG - Mightys Military Gear", "Names containing spaces");
var scripts = ModList.Parse(["login example_user", "// Dabs Framework", "workshop_download_item 221100 2545327648", "CF workshop_download_item 221100 1559212036", "quit"]);
Check(scripts.Mods.Count == 2 && scripts.Mods[0].Name == "Dabs Framework", "Annotated SteamCMD list");
try { ModList.Parse(["workshop_download_item 223350 1559212036"]); throw new Exception("Wrong app accepted"); } catch (FormatException) { }
var root = Path.Combine(Path.GetTempPath(), "BKDownloaderTests-" + Guid.NewGuid().ToString("N"));
var destination = ModList.Destination(root, ModList.FolderName("../bad:mod", 123));
Check(Path.GetDirectoryName(destination) == root, "Folder containment");
Check(ModList.FolderName("CF", 1559212036) == "@CF", "Do not append workshop ID to named folders");
Check(ModList.FolderName("@MMG - Mightys Military Gear", 2663169692) == "@MMG - Mightys Military Gear", "Preserve mod names and one @ prefix");
var migrationRoot = Path.Combine(root, "mods");
var legacyPath = ModList.Destination(migrationRoot, "@CF_1559212036");
Directory.CreateDirectory(legacyPath);
File.WriteAllText(Path.Combine(legacyPath, "downloaded.pbo"), "existing mod data");
var newPath = ModDirectory.Prepare(migrationRoot, "CF", 1559212036);
Check(Path.GetFileName(newPath) == "@CF" && !Directory.Exists(legacyPath), "Legacy directory renamed without ID suffix");
Check(File.ReadAllText(Path.Combine(newPath, "downloaded.pbo")) == "existing mod data", "Migration preserves downloaded data");
Check(ModDirectory.Prepare(migrationRoot, "CF", 1559212036) == newPath, "Repeat download reuses named directory");
try { ModDirectory.Prepare(migrationRoot, "CF", 123); throw new Exception("Same-name different-ID collision accepted"); } catch (IOException) { }
var unknown = ModList.Destination(migrationRoot, "@Unknown"); Directory.CreateDirectory(unknown);
File.WriteAllText(Path.Combine(unknown, "user.txt"), "preserve");
try { ModDirectory.Prepare(migrationRoot, "Unknown", 77, "@Unknown"); throw new Exception("Unidentified existing folder accepted"); } catch (IOException) { }
Check(File.ReadAllText(Path.Combine(unknown, "user.txt")) == "preserve", "Unknown user data remains untouched");
Reject(() => ModList.Destination(root, "..\\escape"), "traversal");
Reject(() => ModList.Destination(root, "C:\\escape"), "absolute folder");
Reject(() => ModList.Destination("relative", "@CF"), "relative root");
var file = Path.Combine(root, "settings.json");
SettingsStore.Save(new() { Username = "test", Mods = parsed.Mods }, file);
var json = File.ReadAllText(file);
Check(!json.Contains("Password") && !json.Contains("Token"), "No credential fields in settings");
Check(SettingsStore.Load(file).Mods.Count == 2, "Settings round trip");
Console.WriteLine("PASS: mod imports, zero IDs, deduplication, destination containment, credential-free settings.");
using (var gate = new DownloadGate())
{
    gate.Pause(); var blocked = gate.WaitAsync();
    await Task.Delay(60); Check(!blocked.IsCompleted, "Paused work must remain blocked");
    gate.Resume(); await blocked.WaitAsync(TimeSpan.FromSeconds(1));
    gate.Pause(); blocked = gate.WaitAsync(); gate.Cancel();
    try { await blocked; throw new Exception("Cancel must release paused work"); } catch (OperationCanceledException) { }
}
var stateItem = new ModItem { State = DownloadState.Queued };
Check(stateItem.CanPause && stateItem.CanCancel && !stateItem.CanStart, "Queued task action availability");
stateItem.State = DownloadState.Paused;
Check(stateItem.CanStart && stateItem.CanCancel && !stateItem.CanPause, "Paused task action availability");
Console.WriteLine("PASS: cooperative pause gate, cancellation while paused, task action states.");

if (args.Length > 0)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    int auths = 0, completed = 0, progress = 0;
    var messages = new System.Collections.Concurrent.ConcurrentQueue<WorkerEvent>();
    using var client = new BKDayzModDownloader.WorkerClient(message =>
    {
        messages.Enqueue(message);
        Check(!message.Message.Contains("not-a-real-password"), "Password appeared in engine output");
        if (message.Type == "complete") completed++;
        if (message.Type == "progress" && message.Percent == 50 && message.NetworkBytes == 25) progress++;
        return Task.CompletedTask;
    }, message => { auths++; return Task.FromResult<string?>("test-code"); }, Path.GetFullPath(args[0]), offlineTest: true);
    var request = new DownloadRequest("test", "not-a-real-password", 0, root, false, true, 8, 1);
    await client.LoginAsync(request, timeout.Token);
    Check(client.IsRunning && auths == 1, "Login / private auth response");
    await client.DownloadAsync(request with { ModId = 1, Password = "" }, timeout.Token);
    try { await client.DownloadAsync(request with { ModId = 2, Password = "" }, timeout.Token); throw new Exception("Failed job reported success"); }
    catch (IOException ex) when (ex.Message.Contains("离线模拟")) { }
    await client.DownloadAsync(request with { ModId = 3, Password = "" }, timeout.Token);
    Check(completed == 2 && progress == 2 && auths == 1, "Persistent session after per-item failure");
    var streaming = client.DownloadAsync(request with { ModId = 5, Password = "" }, timeout.Token);
    async Task WaitUntil(Func<bool> condition)
    {
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
    await WaitUntil(() => messages.Any(m => m.Message == "离线传输"));
    await client.ControlAsync("pause");
    await WaitUntil(() => messages.Any(m => m.Type == "paused"));
    var countAtPause = messages.Count(m => m.Type == "progress");
    await Task.Delay(180, timeout.Token);
    Check(messages.Count(m => m.Type == "progress") == countAtPause && !streaming.IsCompleted, "Pause must stop progress without completing the task");
    await client.ControlAsync("resume");
    await WaitUntil(() => messages.Any(m => m.Type == "resumed"));
    await WaitUntil(() => messages.Count(m => m.Type == "progress") > countAtPause);
    await client.ControlAsync("cancel");
    try { await streaming; throw new Exception("Canceled item reported success"); } catch (OperationCanceledException) { }
    Check(client.IsRunning, "Per-item cancellation must keep the authenticated worker alive");
    await client.DownloadAsync(request with { ModId = 3, Password = "" }, timeout.Token);
    Check(auths == 1, "Cancel then next task must not require reauthentication");
    using var cancel = new CancellationTokenSource(300);
    try { await client.DownloadAsync(request with { ModId = 4, Password = "" }, cancel.Token); throw new Exception("Cancellation failed"); }
    catch (OperationCanceledException) { }
    Check(!client.IsRunning, "Cancellation did not close the worker");
    Console.WriteLine("PASS: actual client/engine pipe transport, private authentication, persistent session, byte progress, failure recovery, cancellation and credential redaction.");
}
