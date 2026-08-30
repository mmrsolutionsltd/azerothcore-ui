using System.IO.Compression;

namespace AzerothCore_UI.Web.Services;

public static class ClientAddonPackageBuilder
{
    public const string AddonName = "AzerothCompanion";
    private static readonly string[] IncludedFiles =
        ["AzerothCompanion.toc", "AzerothCompanion.lua", "CasterAuto.lua", "README.md"];

    public static string ResolveAddonDirectory() => Path.Combine(
        AppContext.BaseDirectory, "ClientAddons", AddonName);

    public static ClientAddonPackageInfo GetPackageInfo(string addonDirectory)
    {
        var tocPath = RequiredFile(addonDirectory, "AzerothCompanion.toc");
        RequiredFile(addonDirectory, "AzerothCompanion.lua");
        RequiredFile(addonDirectory, "CasterAuto.lua");
        var versionLine = File.ReadLines(tocPath).FirstOrDefault(line =>
            line.StartsWith("## Version:", StringComparison.OrdinalIgnoreCase));
        var version = versionLine?.Split(':', 2)[1].Trim();
        return new(AddonName,
            string.IsNullOrWhiteSpace(version) ? "unknown" : version,
            IncludedFiles.Length);
    }

    public static byte[] Build(string addonDirectory)
    {
        _ = GetPackageInfo(addonDirectory);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(
                   output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var fileName in IncludedFiles)
            {
                var sourcePath = RequiredFile(addonDirectory, fileName);
                var entry = archive.CreateEntry(
                    $"{AddonName}/{fileName}", CompressionLevel.Optimal);
                entry.LastWriteTime = File.GetLastWriteTimeUtc(sourcePath);
                using var source = File.OpenRead(sourcePath);
                using var destination = entry.Open();
                source.CopyTo(destination);
            }
        }
        return output.ToArray();
    }

    private static string RequiredFile(string directory, string fileName)
    {
        var root = Path.GetFullPath(directory);
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
            throw new FileNotFoundException(
                $"The {AddonName} package is missing {fileName}.", path);
        return path;
    }
}

public sealed record ClientAddonPackageInfo(
    string Name, string Version, int FileCount);
