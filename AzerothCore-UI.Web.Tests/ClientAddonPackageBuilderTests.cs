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
        var directory = ClientAddonPackageBuilder.ResolveAddonDirectory("AzerothCompanion");
        var info = ClientAddonPackageBuilder.GetPackageInfo("AzerothCompanion", directory);
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
    public void PackageContainsTheVersionedAddonFolderIncludingNestedFiles()
    {
        var directory = CreateAddonDirectory();
        try
        {
            var info = ClientAddonPackageBuilder.GetPackageInfo("TestAddon", directory);
            var package = ClientAddonPackageBuilder.Build("TestAddon", directory);

            Assert.Equal("0.1.0", info.Version);
            Assert.Equal(5, info.FileCount);
            using var archive = new ZipArchive(new MemoryStream(package));
            Assert.Equal(
                [
                    "TestAddon/README.md",
                    "TestAddon/TestAddon.lua",
                    "TestAddon/TestAddon.toc",
                    "TestAddon/widgets/Sub.lua",
                    "TestAddon/widgets/binary.blp"
                ],
                archive.Entries.Select(entry => entry.FullName)
                    .OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PackagePreservesBinaryFileContentByteForByte()
    {
        var directory = CreateAddonDirectory();
        try
        {
            var expectedBytes = File.ReadAllBytes(
                Path.Combine(directory, "widgets", "binary.blp"));
            var package = ClientAddonPackageBuilder.Build("TestAddon", directory);

            using var archive = new ZipArchive(new MemoryStream(package));
            var entry = archive.GetEntry("TestAddon/widgets/binary.blp");
            Assert.NotNull(entry);
            using var entryStream = entry!.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);

            Assert.Equal(expectedBytes, buffer.ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PackageRejectsAnAddonMissingATocFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"azeroth-addon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "TestAddon.lua"),
                "print('test')",
                Encoding.UTF8);

            Assert.Throws<FileNotFoundException>(() =>
                ClientAddonPackageBuilder.Build("TestAddon", directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PackageReportsAMissingAddonDirectoryClearlyInsteadOfAPartialArchive()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"azeroth-addon-test-missing-{Guid.NewGuid():N}");

        Assert.Throws<DirectoryNotFoundException>(() =>
            ClientAddonPackageBuilder.Build("TestAddon", directory));
        Assert.Throws<DirectoryNotFoundException>(() =>
            ClientAddonPackageBuilder.GetPackageInfo("TestAddon", directory));
    }

    private static string CreateAddonDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"azeroth-addon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "widgets"));
        File.WriteAllText(
            Path.Combine(directory, "TestAddon.toc"),
            "## Interface: 30300\n## Version: 0.1.0",
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(directory, "TestAddon.lua"),
            "print('test')",
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(directory, "README.md"),
            "# Test addon",
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(directory, "widgets", "Sub.lua"),
            "print('nested')",
            Encoding.UTF8);
        File.WriteAllBytes(
            Path.Combine(directory, "widgets", "binary.blp"),
            [0x00, 0x42, 0xFF, 0x10, 0x7A, 0x00, 0x01]);
        return directory;
    }
}
