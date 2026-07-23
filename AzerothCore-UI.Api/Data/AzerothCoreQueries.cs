namespace AzerothCore_UI.Api.Data;

internal static class AzerothCoreQueries
{
    // AzerothCore stores only learned spells in character_spell. Unlike older
    // TrinityCore schemas, the table has no active or disabled columns.
    internal const string CharacterLearnedSpells = """
        SELECT spell FROM acore_characters.character_spell
        WHERE guid = @CharacterGuid;
        """;
}
