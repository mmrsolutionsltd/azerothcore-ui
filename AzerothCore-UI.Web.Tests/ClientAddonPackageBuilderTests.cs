using System.IO.Compression;
using System.Text;
using AzerothCore_UI.Web.Services;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class ClientAddonPackageBuilderTests
{
    [Fact]
    public void BundledAddonDisplaysCompanionOnlyQuestProgress()
    {
        var directory = ClientAddonPackageBuilder.ResolveAddonDirectory();
        var info = ClientAddonPackageBuilder.GetPackageInfo(directory);
        var script = File.ReadAllText(
            Path.Combine(directory, "AzerothCompanion.lua"));

        Assert.Equal("0.7.0", info.Version);
        Assert.Contains("Companion-only quests", script);
        Assert.Contains("CompanionObjectiveText", script);
        Assert.Contains("companionPlayer.questOrder", script);
    }

    [Fact]
    public void PackageContainsVersionedAddonFolderAndOnlyAllowlistedFiles()
    {
        var directory = CreateAddonDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "do-not-package.txt"), "secret");

            var info = ClientAddonPackageBuilder.GetPackageInfo(directory);
            var package = ClientAddonPackageBuilder.Build(directory);

            Assert.Equal("0.1.0", info.Version);
            Assert.Equal(3, info.FileCount);
            using var archive = new ZipArchive(new MemoryStream(package));
            Assert.Equal(
                [
                    "AzerothCompanion/AzerothCompanion.lua",
                    "AzerothCompanion/AzerothCompanion.toc",
                    "AzerothCompanion/README.md"
                ],
                archive.Entries.Select(entry => entry.FullName)
                    .OrderBy(name => name).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PackageRejectsAnIncompleteAddon()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"azeroth-addon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "AzerothCompanion.toc"),
                "## Version: 0.1.0",
                Encoding.UTF8);

            Assert.Throws<FileNotFoundException>(() =>
                ClientAddonPackageBuilder.Build(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateAddonDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"azeroth-addon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "AzerothCompanion.toc"),
            "## Interface: 30300\n## Version: 0.1.0",
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(directory, "AzerothCompanion.lua"),
            "print('test')",
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(directory, "README.md"),
            "# Test addon",
            Encoding.UTF8);
        return directory;
    }
}
