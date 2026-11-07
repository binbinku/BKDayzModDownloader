using System.Text.RegularExpressions;

namespace BKDownloader.Core;

public static partial class ModList
{
    [GeneratedRegex(@"^(.*?)\s*workshop_download_item\s+221100\s+(\d+)(?:\s+validate)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CommandLine();
    [GeneratedRegex(@"^(.+?)\s+(\d+)\s*$")]
    private static partial Regex NamedLine();

    public static (List<ModItem> Mods, int Skipped) Parse(IEnumerable<string> lines)
    {
        List<ModItem> mods = [];
        HashSet<ulong> ids = [];
        string pendingName = "";
        int skipped = 0, number = 0;
        foreach (var raw in lines)
        {
            number++;
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0) continue;
            if (line.StartsWith("//")) { pendingName = line[2..].Trim(); continue; }
            if (line.StartsWith('#')) continue;
            if (line.StartsWith("@ShutdownOnFailedCommand ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("@NoPromptForPassword ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("login ", StringComparison.OrdinalIgnoreCase) || line.Equals("quit", StringComparison.OrdinalIgnoreCase)) continue;
            string name;
            string idText;
            var command = CommandLine().Match(line);
            var named = NamedLine().Match(line);
            if (!command.Success && line.Contains("workshop_download_item", StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"第 {number} 行不是有效的 DayZ 工坊下载命令（App ID 必须为 221100）。");
            if (command.Success)
            {
                name = command.Groups[1].Value.Trim();
                if (name.Length == 0) name = pendingName;
                idText = command.Groups[2].Value;
            }
            else if (ulong.TryParse(line, out _)) { name = ""; idText = line; }
            else if (named.Success) { name = named.Groups[1].Value.Trim(); idText = named.Groups[2].Value; }
            else throw new FormatException($"第 {number} 行无法识别：{line}");
            pendingName = "";
            if (!ulong.TryParse(idText, out var id)) throw new FormatException($"第 {number} 行 ID 超出范围。");
            if (id == 0 || !ids.Add(id)) { skipped++; continue; }
            if (name.Length == 0) name = $"Workshop {id}";
            mods.Add(new() { Name = name.TrimStart('@'), Id = id, Folder = FolderName(name, id) });
        }
        return (mods, skipped);
    }

    public static string FolderName(string name, ulong id)
    {
        var invalid = "<>:\"/\\|?*";
        var clean = new string(name.Trim().TrimStart('@').Where(c => c >= 32 && !invalid.Contains(c)).ToArray()).Trim(' ', '.');
        if (clean.Length > 90) clean = clean[..90].Trim(' ', '.');
        return $"@{(clean.Length == 0 ? $"Workshop {id}" : clean)}";
    }

    public static string Destination(string root, string folder)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new ArgumentException("请选择完整的下载目录，例如 D:\\DayZMods。");
        if (string.IsNullOrWhiteSpace(folder) || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folder is "." or ".." || folder.EndsWith('.') || folder.EndsWith(' '))
            throw new ArgumentException("模组文件夹名称不合法。");
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var target = Path.GetFullPath(Path.Combine(normalizedRoot, folder));
        if (!string.Equals(Path.GetDirectoryName(target), normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("模组目录必须位于下载目录内。");
        return target;
    }
}
