using AzerothCore_UI.Api.Models;

namespace AzerothCore_UI.Api.Services;

internal static class DungeonReadinessEvaluator
{
    public static DungeonReadiness Evaluate(
        PartySnapshot party,
        DungeonDestination dungeon,
        IReadOnlyList<DungeonLockout> lockouts,
        IReadOnlyList<DungeonQuest> quests) =>
        new(
            party.Members.Any(member => HasRole(member, "tank")),
            party.Members.Any(member => HasRole(member, "heal")),
            party.Members.Count(member => HasRole(member, "damage") || HasRole(member, "dps")),
            party.MemberCount >= 5,
            party.Members.All(member => member.Level >= dungeon.MinimumLevel
                && member.Level <= dungeon.MaximumLevel),
            lockouts,
            quests);

    private static bool HasRole(PartyMember member, string role) =>
        member.Role.Contains(role, StringComparison.OrdinalIgnoreCase);
}
