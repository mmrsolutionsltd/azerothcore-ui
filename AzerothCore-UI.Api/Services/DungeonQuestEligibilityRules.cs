namespace AzerothCore_UI.Api.Services;

internal static class DungeonQuestEligibilityRules
{
    public static bool MaskAllows(uint mask, byte value) =>
        mask == 0 || value is > 0 and <= 32 && (mask & (1u << (value - 1))) != 0;
}
