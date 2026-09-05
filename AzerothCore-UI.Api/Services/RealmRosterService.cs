using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Dapper;

namespace AzerothCore_UI.Api.Services;

public sealed class RealmRosterService(
    AzerothCoreConnectionFactory connections,
    CompanionPartySessionStore companionParties)
{
    public async Task<RealmRosterSnapshot> GetAsync(
        bool allAccounts, IReadOnlyCollection<uint> allowedAccounts,
        string? preferredUsername, bool includeBots,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        var rows = (await connection.QueryAsync<OnlineCharacterRow>(new CommandDefinition("""
            SELECT c.guid Guid, c.name Name, c.account AccountId,
                   a.username Username,
                   c.level Level, c.class CharacterClass, c.race Race,
                   c.online<>0 Online, c.health CurrentHealth,
                   a.username LIKE CONCAT(@BotPrefix, '%') IsPlayerBot,
                   stats.maxhealth MaximumHealth,
                   member.guid GroupGuid, realm_group.leaderGuid GroupLeaderGuid
            FROM acore_characters.characters c
            JOIN acore_auth.account a ON a.id=c.account
            LEFT JOIN acore_characters.group_member member ON member.memberGuid=c.guid
            LEFT JOIN acore_characters.`groups` realm_group
              ON realm_group.guid=member.guid
            LEFT JOIN acore_characters.character_stats stats ON stats.guid=c.guid
            WHERE (@IncludeBots OR a.username NOT LIKE CONCAT(@BotPrefix, '%'))
              AND UPPER(a.username)<>'AHBOT'
              AND (@AllAccounts OR c.account IN @AllowedAccounts OR c.online<>0)
            ORDER BY a.username, c.name;
            """, new
        {
            AllAccounts = allAccounts,
            AllowedAccounts = allowedAccounts.ToArray(),
            IncludeBots = includeBots,
            BotPrefix = "rndbot"
        }, cancellationToken: cancellationToken))).AsList();
        var sessions = await companionParties.GetActiveAsync(
            allAccounts, allowedAccounts, cancellationToken);
        var companionNames = sessions.SelectMany(session => session.Companions)
            .Select(companion => companion.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sessionLeaderNames = sessions.Select(session => session.LeaderName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RealmRosterCharacter Map(OnlineCharacterRow row, bool isLeader) => new(
            row.Guid, row.Name, row.Username, row.Level, row.CharacterClass,
            row.Race, row.Online, isLeader,
            companionNames.Contains(row.Name), row.CurrentHealth,
            row.MaximumHealth is > 0 ? row.MaximumHealth : null,
            row.IsPlayerBot);

        var onlineRows = rows.Where(row => row.Online && !row.IsPlayerBot).ToArray();
        var parties = onlineRows.Where(row => row.GroupGuid.HasValue)
            .GroupBy(row => row.GroupGuid!.Value)
            .Select(group =>
            {
                var leaderGuid = group.First().GroupLeaderGuid;
                var members = group.Select(row => Map(row, row.Guid == leaderGuid)).ToArray();
                var leader = members.FirstOrDefault(member => member.IsLeader)
                    ?? members.First();
                var remembered = members.Any(member =>
                    sessionLeaderNames.Contains(member.Name)
                    || companionNames.Contains(member.Name));
                var matchingSession = sessions.FirstOrDefault(session =>
                    members.Any(member => member.Name.Equals(session.LeaderName,
                        StringComparison.OrdinalIgnoreCase)));
                return new RealmRosterParty(
                    $"group:{group.Key}", group.Key, leader.Name, true,
                    remembered, matchingSession?.OfflineTimeoutMinutes ?? 5,
                    matchingSession?.LastLeaderOnlineAtUtc, members);
            }).ToList();
        var livePartyNames = parties.SelectMany(party => party.Members)
            .Select(member => member.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions.Where(session =>
                     !livePartyNames.Contains(session.LeaderName)))
        {
            var leader = rows.FirstOrDefault(row => row.Guid == session.LeaderGuid);
            var members = new List<RealmRosterCharacter>
            {
                leader is null
                    ? new(session.LeaderGuid, session.LeaderName,
                        session.LeaderUsername, 0, 0, 0, false, true, false)
                    : Map(leader, true)
            };
            members.AddRange(session.Companions.Select(companion =>
            {
                var current = rows.FirstOrDefault(row => row.Guid == companion.Guid);
                return current is null ? companion : Map(current, false);
            }));
            parties.Add(new($"session:{session.LeaderGuid}", null,
                session.LeaderName, false, true, session.OfflineTimeoutMinutes,
                session.LastLeaderOnlineAtUtc, members.DistinctBy(member => member.Guid).ToArray()));
        }
        var groupedNames = parties.Where(party => party.Live)
            .SelectMany(party => party.Members).Select(member => member.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var solo = onlineRows.Where(row => !groupedNames.Contains(row.Name))
            .Select(row => Map(row, false)).ToArray();
        var leaderGuids = parties.Where(party => party.Live)
            .SelectMany(party => party.Members.Where(member => member.IsLeader))
            .Select(member => member.Guid).ToHashSet();
        var preferredPrefix = preferredUsername?.Trim() ?? "";
        var preferredAccountIds = allowedAccounts.ToHashSet();
        var availableHeroes = rows
            .OrderBy(row => IsPreferredAccount(row.AccountId, row.Username,
                preferredAccountIds, preferredPrefix) ? 0 : 1)
            .ThenBy(row => row.IsPlayerBot)
            .ThenByDescending(row => row.Online)
            .ThenBy(row => row.Username)
            .ThenBy(row => row.Name)
            .Select(row => Map(row, leaderGuids.Contains(row.Guid)))
            .ToArray();
        return new(DateTime.UtcNow,
            parties.OrderByDescending(party => party.Live)
                .ThenBy(party => party.LeaderName).ToArray(),
            solo, sessions, availableHeroes);
    }

    private static bool IsPreferredAccount(
        uint accountId, string account, IReadOnlySet<uint> preferredAccountIds,
        string preferredPrefix) =>
        preferredAccountIds.Contains(accountId)
        || (preferredPrefix.Length > 0
            && (account.Equals(preferredPrefix, StringComparison.OrdinalIgnoreCase)
                || account.StartsWith(preferredPrefix,
                    StringComparison.OrdinalIgnoreCase)));

    private sealed class OnlineCharacterRow
    {
        public uint Guid { get; init; }
        public uint AccountId { get; init; }
        public string Name { get; init; } = "";
        public string Username { get; init; } = "";
        public int Level { get; init; }
        public int CharacterClass { get; init; }
        public int Race { get; init; }
        public bool Online { get; init; }
        public bool IsPlayerBot { get; init; }
        public uint CurrentHealth { get; init; }
        public uint? MaximumHealth { get; init; }
        public uint? GroupGuid { get; init; }
        public uint? GroupLeaderGuid { get; init; }
    }
}
