using System.IO.Compression;
using System.Text;
using AzerothCore_UI.Web.Services;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class ClientAddonPackageBuilderTests
{
    [Fact]
    public void BundledAddonIntegratesWithCarboniteAndKeepsQuestFallback()
    {
        var directory = ClientAddonPackageBuilder.ResolveAddonDirectory();
        var info = ClientAddonPackageBuilder.GetPackageInfo(directory);
        var script = File.ReadAllText(
            Path.Combine(directory, "AzerothCompanion.lua"));

        Assert.Equal("0.11.0", info.Version);
        Assert.Contains("local EXPECTED_PROTOCOL = 10", script);
        Assert.Contains("webadmin companion inspect-addon", script);
        Assert.Contains("WEBADMIN_COMPANION_MAINTENANCE_STATUS", script);
        Assert.Contains("SetDetailsExpanded", script);
        Assert.Contains("RefreshCarbonitePartyQuests", script);
        Assert.Contains("Nx.Que.PaQ[companionName]", script);
        Assert.Contains("Nx.Tim:Sta(\"QPartyUpdate\"", script);
        Assert.Contains("companion.questsInCarbonite", script);
        Assert.Contains(
            "not frame:IsShown() and not CarbonitePartyQuestDisplayAvailable()",
            script);
        Assert.Contains("Companion-only quests", script);
        Assert.Contains("CompanionObjectiveText", script);
        Assert.Contains("companionPlayer.questOrder", script);
        Assert.Contains("lastActivityAt = GetTime()", script);
        Assert.Contains(
            "if activeRequest and not activeRequest.completed then",
            script);
        Assert.Contains(
            "GetTime() - activeRequest.lastActivityAt",
            script);
        Assert.Contains("Caster Auto-Attack", File.ReadAllText(
            Path.Combine(directory, "CasterAuto.lua")));
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
            File.WriteAllText(Path.Combine(directory, "CasterAuto.lua"),
                "print('caster auto')");

            Assert.Equal(4, info.FileCount);
            using var archive = new ZipArchive(new MemoryStream(package));
            Assert.Equal(
                [
                    "AzerothCompanion/AzerothCompanion.lua",
                    "AzerothCompanion/AzerothCompanion.toc",
                    "AzerothCompanion/CasterAuto.lua",
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
            Path.Combine(directory, "CasterAuto.lua"),
            "print('test')",
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(directory, "README.md"),
            "# Test addon",
            Encoding.UTF8);
        return directory;
    }
}
