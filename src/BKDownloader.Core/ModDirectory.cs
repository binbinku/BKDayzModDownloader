using System.Globalization;
using System.Text.RegularExpressions;

namespace BKDownloader.Core;

public static partial class ModDirectory
{
    private const string Marker = ".bk-workshop-id";
    [GeneratedRegex(@"(?m)^\s*publishedid\s*=\s*(\d+)\s*;")]
    private static partial Regex PublishedId();

    public static string Prepare(string root, string name, ulong id, string? previousFolder = null)
    {
        if (id == 0 || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("无法确定模组名称或工坊 ID。");
        var target = ModList.Destination(root, ModList.FolderName(name, id));
        bool migrated = false;
        Directory.CreateDirectory(root);
        var legacy = new List<string>();
        if (!string.IsNullOrWhiteSpace(previousFolder)) legacy.Add(ModList.Destination(root, previousFolder));
        legacy.AddRange(Directory.EnumerateDirectories(root, $"@*_{id}", SearchOption.TopDirectoryOnly));
        if (!System.IO.Directory.Exists(target))
        {
            var candidate = legacy.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !string.Equals(path, target, StringComparison.OrdinalIgnoreCase) && System.IO.Directory.Exists(path))
                .FirstOrDefault(path => ReadId(path) is { } found ? found == id :
                    Path.GetFileName(path).EndsWith($"_{id}", StringComparison.Ordinal) ||
                    string.Equals(Path.GetFileName(path), $"@Workshop {id}", StringComparison.OrdinalIgnoreCase));
            if (candidate is not null)
            {
                // Both paths are immediate children of the explicitly selected root.
                _ = ModList.Destination(root, Path.GetFileName(candidate));
                System.IO.Directory.Move(candidate, target);
                migrated = true;
            }
        }
        if (System.IO.Directory.Exists(target))
        {
            var existingId = ReadId(target);
            if (existingId is { } known && known != id)
                throw new IOException($"目录 {Path.GetFileName(target)} 已属于另一模组（{known}），请使用不同保存目录。");
            if (existingId is null && System.IO.Directory.EnumerateFileSystemEntries(target).Any() && !migrated)
                throw new IOException($"目录 {Path.GetFileName(target)} 已有文件，但无法确认模组 ID；请先检查该目录。");
        }
        System.IO.Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, Marker), id.ToString(CultureInfo.InvariantCulture));
        return target;
    }

    private static ulong? ReadId(string directory)
    {
        var marker = Path.Combine(directory, Marker);
        if (File.Exists(marker) && ulong.TryParse(File.ReadAllText(marker).Trim(), out var markerId) && markerId != 0) return markerId;
        var meta = Path.Combine(directory, "meta.cpp");
        if (File.Exists(meta))
        {
            var match = PublishedId().Match(File.ReadAllText(meta));
            if (match.Success && ulong.TryParse(match.Groups[1].Value, out var id) && id != 0) return id;
        }
        return null;
    }
}
