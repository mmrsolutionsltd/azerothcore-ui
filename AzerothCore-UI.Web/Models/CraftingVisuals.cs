namespace AzerothCore_UI.Web.Models;

public static class CraftingVisuals
{
    public static string QualityClass(int quality) => $"quality-{Math.Clamp(quality, 0, 7)}";
    public static string AvailabilityClass(string value) => $"availability-{value.ToLowerInvariant()}";
    public static string AvailabilityLabel(string value) => value switch
    {
        "Owned" => "Already owned", "CraftNow" => "Craft now",
        "LearnNext" => "Learn next", "Progression" => "Skill journey", _ => value
    };
    public static string FormatNumber(double value) =>
        Math.Abs(value % 1) < .001 ? value.ToString("0") : value.ToString("0.0");
    public static double SkillPercent(CraftingUpgradeRecommendation item) =>
        Math.Clamp((item.CurrentSkill ?? 0) * 100d / Math.Max(1, item.RequiredSkill ?? 1), 0, 100);
    public static string SlotGlyph(int slot) => slot switch
    {
        0 => "♛", 1 => "◈", 2 => "◆", 3 => "⌁", 4 => "♜", 5 => "═",
        6 => "Ⅱ", 7 => "♢", 8 => "▤", 9 => "✦", 10 or 11 => "◉",
        12 or 13 => "✧", 14 => "◇", 15 => "⚔", 16 => "◒", 17 => "➶", 18 => "⚑", _ => "·"
    };
}
