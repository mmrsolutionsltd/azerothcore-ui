using AzerothCore_UI.Api.Services;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class CharacterServiceCommandBuilderTests
{
    public static TheoryData<string, int?, string> Commands => new()
    {
        { "rename", null, "character rename Hundead" },
        { "customize", null, "character customize Hundead" },
        { "race", null, "character changerace Hundead" },
        { "faction", null, "character changefaction Hundead" },
        { "talents", null, "reset talents Hundead" },
        { "spells", null, "reset spells Hundead" },
        { "revive", null, "revive Hundead" },
        { "unstuck", null, "unstuck Hundead inn" },
        { "level", 10, "character level Hundead 10" }
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public void Build_UsesAzerothCoreConsoleSyntax(string service, int? level, string expected)
    {
        var result = CharacterServiceCommandBuilder.Build("Hundead", service, level);

        Assert.Equal(expected, result.Command);
    }

    [Fact]
    public void Build_RequiresOnlineCharacterForSpellReset() =>
        Assert.True(CharacterServiceCommandBuilder.Build("Hundead", "spells", null).RequiresOnlineCharacter);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(81)]
    public void Build_RejectsInvalidLevel(int? level) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CharacterServiceCommandBuilder.Build("Hundead", "level", level));

    [Fact]
    public void Build_RejectsUnknownService() =>
        Assert.Throws<ArgumentException>(
            () => CharacterServiceCommandBuilder.Build("Hundead", "delete", null));
}
