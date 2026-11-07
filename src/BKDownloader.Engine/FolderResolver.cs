using BKDownloader.Core;
using DepotDownloader;

namespace BKDownloader.Engine;

internal static class FolderResolver
{
    public static string Root;
    public static string PreviousFolder;
    public static void Resolve(string name, ulong id)
    {
        if (string.IsNullOrWhiteSpace(Root)) return;
        var path = ModDirectory.Prepare(Root, name, id, PreviousFolder);
        ContentDownloader.Config.InstallDirectory = path;
        Bridge.Send(new("destination", path));
    }
}
