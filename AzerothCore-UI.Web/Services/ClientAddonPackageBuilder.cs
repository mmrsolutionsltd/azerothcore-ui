using System.IO.Compression;

namespace AzerothCore_UI.Web.Services;

public static class ClientAddonPackageBuilder
{
    public static string ResolveAddonDirectory(string addonName) => Path.Combine(
        AppContext.BaseDirectory, "ClientAddons", addonName);

    public static ClientAddonPackageInfo GetPackageInfo(string addonName, string addonDirectory)
    {
        var root = RequireDirectory(addonName, addonDirectory);
        var tocPath = RequireTocFile(addonName, root);
        var versionLine = File.ReadLines(tocPath).FirstOrDefault(line =>
            line.StartsWith("## Version:", StringComparison.OrdinalIgnoreCase));
        var version = versionLine?.Split(':', 2)[1].Trim();
        var fileCount = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count();
        return new(addonName, string.IsNullOrWhiteSpace(version) ? "unknown" : version, fileCount);
    }

    public static byte[] Build(string addonName, string addonDirectory)
    {
        var root = RequireDirectory(addonName, addonDirectory);
        RequireTocFile(addonName, root);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sourcePaths = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
            foreach (var sourcePath in sourcePaths)
            {
                var relative = Path.GetRelativePath(root, sourcePath).Replace('\\', '/');
                var entry = archive.CreateEntry($"{addonName}/{relative}", CompressionLevel.Optimal);
                entry.LastWriteTime = File.GetLastWriteTimeUtc(sourcePath);
                using var source = File.OpenRead(sourcePath);
                using var destination = entry.Open();
                source.CopyTo(destination);
            }
        }
        return output.ToArray();
    }

    private static string RequireDirectory(string addonName, string addonDirectory)
    {
        var root = Path.GetFullPath(addonDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"The {addonName} addon directory was not found.");
        return root;
    }

    private static string RequireTocFile(string addonName, string root)
    {
        var tocPath = Directory.EnumerateFiles(root, "*.toc", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (tocPath is null)
            throw new FileNotFoundException($"The {addonName} package is missing a .toc file.", root);
        return tocPath;
    }
}

public sealed record ClientAddonPackageInfo(
    string Name, string Version, int FileCount);
