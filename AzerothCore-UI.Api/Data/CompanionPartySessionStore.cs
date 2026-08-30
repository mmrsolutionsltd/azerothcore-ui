using AzerothCore_UI.Api.Models;
using Dapper;

namespace AzerothCore_UI.Api.Data;

public sealed class CompanionPartySessionStore(AzerothCoreConnectionFactory connections)
{
    public async Task AddCompanionAsync(
        string leaderName, string companionName,
        AdministrationUserIdentity? actor, CancellationToken cancellationToken)
    {
        await using var game = connections.CreateConnection();
        var characters = (await game.QueryAsync<CharacterRow>(new CommandDefinition("""
            SELECT c.guid Guid, c.name Name, c.account AccountId,
                   a.username Username, c.level Level, c.class CharacterClass,
                   c.race Race, c.online<>0 Online, c.health CurrentHealth
            FROM acore_characters.characters c
            JOIN acore_auth.account a ON a.id=c.account
            WHERE c.name IN (@LeaderName, @CompanionName)
            """, new { LeaderName = leaderName, CompanionName = companionName },
            cancellationToken: cancellationToken))).AsList();
        var leader = characters.FirstOrDefault(row =>
            row.Name.Equals(leaderName, StringComparison.OrdinalIgnoreCase));
        var companion = characters.FirstOrDefault(row =>
            row.Name.Equals(companionName, StringComparison.OrdinalIgnoreCase));
        if (leader is null || companion is null)
            throw new InvalidOperationException("The companion party characters could not be found.");

        await using var administration = connections.CreateAdministrationConnection();
        await administration.OpenAsync(cancellationToken);
        await using var transaction = await administration.BeginTransactionAsync(cancellationToken);
        await administration.ExecuteAsync(new CommandDefinition("""
            INSERT INTO companion_party_session
              (leader_guid, leader_name, leader_account_id,
               started_by_user_id, started_by_username, started_at_utc,
               last_leader_online_at_utc, offline_timeout_minutes, updated_at_utc)
            VALUES
              (@LeaderGuid, @LeaderName, @LeaderAccountId,
               @ActorId, @ActorName, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 5,
               UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              leader_name=VALUES(leader_name),
              leader_account_id=VALUES(leader_account_id),
              started_by_user_id=VALUES(started_by_user_id),
              started_by_username=VALUES(started_by_username),
              last_leader_online_at_utc=UTC_TIMESTAMP(6),
              updated_at_utc=UTC_TIMESTAMP(6);

            INSERT INTO companion_party_session_member
              (leader_guid, companion_guid, companion_name, added_at_utc)
            VALUES (@LeaderGuid, @CompanionGuid, @CompanionName, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              companion_name=VALUES(companion_name),
              added_at_utc=VALUES(added_at_utc);
            """, new
        {
            LeaderGuid = leader.Guid,
            LeaderName = leader.Name,
            LeaderAccountId = leader.AccountId,
            ActorId = actor?.Id,
            ActorName = actor?.Username ?? "web-service",
            CompanionGuid = companion.Guid,
            CompanionName = companion.Name
        }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveCompanionAsync(
        string leaderName, string companionName, CancellationToken cancellationToken)
    {
        await using var administration = connections.CreateAdministrationConnection();
        await administration.OpenAsync(cancellationToken);
        await using var transaction = await administration.BeginTransactionAsync(cancellationToken);
        await administration.ExecuteAsync(new CommandDefinition("""
            DELETE member
            FROM companion_party_session_member member
            JOIN companion_party_session session
              ON session.leader_guid=member.leader_guid
            WHERE session.leader_name=@LeaderName
              AND member.companion_name=@CompanionName;

            DELETE session
            FROM companion_party_session session
            WHERE session.leader_name=@LeaderName
              AND NOT EXISTS (
                SELECT 1 FROM companion_party_session_member member
                WHERE member.leader_guid=session.leader_guid);
            """, new { LeaderName = leaderName, CompanionName = companionName },
            transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetTimeoutAsync(
        string leaderName, int timeoutMinutes, CancellationToken cancellationToken)
    {
        if (timeoutMinutes is < 1 or > 120)
            throw new ArgumentOutOfRangeException(nameof(timeoutMinutes),
                "Offline retention must be between 1 and 120 minutes.");
        await using var administration = connections.CreateAdministrationConnection();
        var affected = await administration.ExecuteAsync(new CommandDefinition("""
            UPDATE companion_party_session
            SET offline_timeout_minutes=@TimeoutMinutes,
                updated_at_utc=UTC_TIMESTAMP(6)
            WHERE leader_name=@LeaderName;
            """, new { LeaderName = leaderName, TimeoutMinutes = timeoutMinutes },
            cancellationToken: cancellationToken));
        if (affected == 0)
            throw new InvalidOperationException("No remembered companion party exists for that leader.");
    }

    public async Task<IReadOnlyList<CompanionPartySession>> GetActiveAsync(
        bool allAccounts, IReadOnlyCollection<uint> allowedAccounts,
        CancellationToken cancellationToken)
    {
        await using var administration = connections.CreateAdministrationConnection();
        var sessions = (await administration.QueryAsync<SessionRow>(new CommandDefinition("""
            SELECT leader_guid LeaderGuid, leader_name LeaderName,
                   leader_account_id LeaderAccountId,
                   started_by_user_id StartedByUserId,
                   started_by_username StartedByUsername,
                   started_at_utc StartedAtUtc,
                   last_leader_online_at_utc LastLeaderOnlineAtUtc,
                   offline_timeout_minutes OfflineTimeoutMinutes
            FROM companion_party_session
            WHERE @AllAccounts OR leader_account_id IN @AllowedAccounts
            ORDER BY updated_at_utc DESC;
            """, new
        {
            AllAccounts = allAccounts,
            AllowedAccounts = allowedAccounts.ToArray()
        }, cancellationToken: cancellationToken))).AsList();
        if (sessions.Count == 0) return [];

        var leaderGuids = sessions.Select(session => session.LeaderGuid).ToArray();
        var members = (await administration.QueryAsync<MemberRow>(new CommandDefinition("""
            SELECT leader_guid LeaderGuid, companion_guid CompanionGuid,
                   companion_name CompanionName
            FROM companion_party_session_member
            WHERE leader_guid IN @LeaderGuids
            ORDER BY added_at_utc, companion_name;
            """, new { LeaderGuids = leaderGuids },
            cancellationToken: cancellationToken))).AsList();
        var allGuids = leaderGuids.Concat(members.Select(member => member.CompanionGuid))
            .Distinct().ToArray();
        await using var game = connections.CreateConnection();
        var characterRows = (await game.QueryAsync<CharacterRow>(new CommandDefinition("""
            SELECT c.guid Guid, c.name Name, c.account AccountId,
                   a.username Username, c.level Level, c.class CharacterClass,
                   c.race Race, c.online<>0 Online, c.health CurrentHealth,
                   stats.maxhealth MaximumHealth
            FROM acore_characters.characters c
            JOIN acore_auth.account a ON a.id=c.account
            LEFT JOIN acore_characters.character_stats stats ON stats.guid=c.guid
            WHERE c.guid IN @Guids;
            """, new { Guids = allGuids },
            cancellationToken: cancellationToken))).ToDictionary(row => row.Guid);

        var now = DateTime.UtcNow;
        var expired = new List<uint>();
        var onlineLeaders = new List<uint>();
        var result = new List<CompanionPartySession>();
        foreach (var session in sessions)
        {
            if (!characterRows.TryGetValue(session.LeaderGuid, out var leader))
            {
                expired.Add(session.LeaderGuid);
                continue;
            }
            if (leader.Online) onlineLeaders.Add(session.LeaderGuid);
            var lastOnline = leader.Online ? now : session.LastLeaderOnlineAtUtc;
            if (!leader.Online
                && lastOnline.AddMinutes(session.OfflineTimeoutMinutes) <= now)
            {
                expired.Add(session.LeaderGuid);
                continue;
            }
            var companions = members.Where(member => member.LeaderGuid == session.LeaderGuid)
                .Select(member => characterRows.GetValueOrDefault(member.CompanionGuid))
                .Where(character => character is not null)
                .Select(character => ToRoster(character!, false, true))
                .ToArray();
            result.Add(new(session.LeaderGuid, leader.Name, leader.AccountId,
                leader.Username, leader.Online, session.StartedByUserId,
                session.StartedByUsername, session.StartedAtUtc, lastOnline,
                session.OfflineTimeoutMinutes, companions));
        }

        if (onlineLeaders.Count > 0)
            await administration.ExecuteAsync(new CommandDefinition("""
                UPDATE companion_party_session
                SET last_leader_online_at_utc=UTC_TIMESTAMP(6),
                    updated_at_utc=UTC_TIMESTAMP(6)
                WHERE leader_guid IN @LeaderGuids;
                """, new { LeaderGuids = onlineLeaders },
                cancellationToken: cancellationToken));
        if (expired.Count > 0)
            await administration.ExecuteAsync(new CommandDefinition("""
                DELETE FROM companion_party_session WHERE leader_guid IN @LeaderGuids;
                """, new { LeaderGuids = expired },
                cancellationToken: cancellationToken));
        return result;
    }

    private static RealmRosterCharacter ToRoster(
        CharacterRow row, bool leader, bool companion) => new(
        row.Guid, row.Name, row.Username, row.Level, row.CharacterClass,
        row.Race, row.Online, leader, companion, row.CurrentHealth,
        row.MaximumHealth is > 0 ? row.MaximumHealth : null);

    internal sealed class CharacterRow
    {
        public uint Guid { get; init; }
        public string Name { get; init; } = "";
        public uint AccountId { get; init; }
        public string Username { get; init; } = "";
        public int Level { get; init; }
        public int CharacterClass { get; init; }
        public int Race { get; init; }
        public bool Online { get; init; }
        public uint CurrentHealth { get; init; }
        public uint? MaximumHealth { get; init; }
    }
    private sealed class SessionRow
    {
        public uint LeaderGuid { get; init; }
        public string LeaderName { get; init; } = "";
        public uint LeaderAccountId { get; init; }
        public ulong? StartedByUserId { get; init; }
        public string StartedByUsername { get; init; } = "";
        public DateTime StartedAtUtc { get; init; }
        public DateTime LastLeaderOnlineAtUtc { get; init; }
        public int OfflineTimeoutMinutes { get; init; }
    }
    private sealed class MemberRow
    {
        public uint LeaderGuid { get; init; }
        public uint CompanionGuid { get; init; }
        public string CompanionName { get; init; } = "";
    }
}
