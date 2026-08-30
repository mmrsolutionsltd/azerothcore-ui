namespace AzerothCore_UI.Api.Services;

internal static class ArmorProficiencyRules
{
    public static int[] WeaponSubclasses(int characterClass) => characterClass switch
    {
        1 => [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 13, 15, 16, 18],
        2 => [0, 1, 4, 5, 6, 7, 8],
        3 => [0, 1, 2, 3, 6, 7, 8, 10, 13, 15, 18],
        4 => [0, 2, 3, 4, 7, 13, 15, 16, 18],
        5 => [4, 10, 15, 19],
        6 => [0, 1, 4, 5, 6, 7, 8],
        7 => [0, 1, 4, 5, 10, 13, 15],
        8 or 9 => [7, 10, 15, 19],
        11 => [4, 5, 6, 10, 13, 15],
        _ => [0]
    };

    /// WotLK 3.3.5a (patch 3.0.8+) grants mail/plate proficiency from level 1, not
    /// level 40 as in Classic, and only the class's primary armor type is a sensible
    /// upgrade recommendation - not every type it's technically legal to equip.
    public static int[] ArmorSubclasses(int characterClass) => characterClass switch
    {
        1 => [0, 4, 6],        // Warrior: Plate, Shield
        2 => [0, 4, 6, 7],     // Paladin: Plate, Shield, Libram
        3 => [0, 3],           // Hunter: Mail
        4 => [0, 2],           // Rogue: Leather
        5 or 8 or 9 => [0, 1], // Priest, Mage, Warlock: Cloth
        6 => [0, 4, 10],       // Death Knight: Plate, Sigil
        7 => [0, 3, 6, 9],     // Shaman: Mail, Shield, Totem
        11 => [0, 2, 8],       // Druid: Leather, Idol
        _ => [0, 1]
    };
}
