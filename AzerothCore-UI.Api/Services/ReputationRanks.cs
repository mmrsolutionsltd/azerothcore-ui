namespace AzerothCore_UI.Api.Services;

internal static class ReputationRanks
{
    private static readonly string[] Names =
        ["Hated", "Hostile", "Unfriendly", "Neutral", "Friendly", "Honored", "Revered", "Exalted"];

    private static readonly int[] MinimumStandingByRank =
        [-42000, -6000, -3000, 0, 3000, 9000, 21000, 42000];

    public static byte GetRank(int standing) => standing switch
    {
        < -6000 => 0, < -3000 => 1, < 0 => 2, < 3000 => 3,
        < 9000 => 4, < 21000 => 5, < 42000 => 6, _ => 7
    };

    public static string Name(byte rank) => Names[Math.Clamp(rank, (byte)0, (byte)7)];

    public static int MinimumStandingForRank(byte rank) => MinimumStandingByRank[Math.Clamp(rank, (byte)0, (byte)7)];
}
