using AzerothCore_UI.Api.Models;
using Dapper;

namespace AzerothCore_UI.Api.Data;

public sealed class CompanionLogisticsStore(
    AzerothCoreConnectionFactory connectionFactory)
{
    public async Task<(CompanionLogisticsSettings Settings,
        IReadOnlyList<StoredCompanionLogisticsRoute> Routes)> GetAsync(
        uint companionGuid, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateAdministrationConnection();
        var profile = await connection.QuerySingleOrDefaultAsync<ProfileRow>(
            new CommandDefinition("""
                SELECT trigger_free_slots TriggerFreeSlots,
                       target_free_slots TargetFreeSlots,
                       automatic_enabled AutomaticEnabled
                FROM companion_logistics_profile
                WHERE companion_guid=@CompanionGuid;
                """, new { CompanionGuid = companionGuid },
                cancellationToken: cancellationToken));
        var routes = (await connection.QueryAsync<RouteRow>(new CommandDefinition("""
            SELECT category_key CategoryKey, recipient_guid RecipientGuid,
                   keep_quantity KeepQuantity, enabled Enabled
            FROM companion_logistics_route
            WHERE companion_guid=@CompanionGuid
            ORDER BY category_key;
            """, new { CompanionGuid = companionGuid },
            cancellationToken: cancellationToken))).Select(row =>
                new StoredCompanionLogisticsRoute(
                    row.CategoryKey, row.RecipientGuid,
                    row.KeepQuantity, row.Enabled)).ToArray();
        return (profile is null
                ? new CompanionLogisticsSettings(4, 8, false)
                : new CompanionLogisticsSettings(
                    profile.TriggerFreeSlots, profile.TargetFreeSlots,
                    profile.AutomaticEnabled),
            routes);
    }

    public async Task SaveAsync(
        uint companionGuid, CompanionLogisticsSettings settings,
        IReadOnlyList<StoredCompanionLogisticsRoute> routes,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateAdministrationConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO companion_logistics_profile
              (companion_guid, trigger_free_slots, target_free_slots,
               automatic_enabled, updated_at_utc)
            VALUES
              (@CompanionGuid, @TriggerFreeSlots, @TargetFreeSlots,
               @AutomaticEnabled, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              trigger_free_slots=VALUES(trigger_free_slots),
              target_free_slots=VALUES(target_free_slots),
              automatic_enabled=VALUES(automatic_enabled),
              updated_at_utc=VALUES(updated_at_utc);
            DELETE FROM companion_logistics_route
            WHERE companion_guid=@CompanionGuid;
            """, new
            {
                CompanionGuid = companionGuid,
                settings.TriggerFreeSlots,
                settings.TargetFreeSlots,
                settings.AutomaticEnabled
            }, transaction, cancellationToken: cancellationToken));
        foreach (var route in routes)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO companion_logistics_route
                  (companion_guid, category_key, recipient_guid,
                   keep_quantity, enabled)
                VALUES
                  (@CompanionGuid, @CategoryKey, @RecipientGuid,
                   @KeepQuantity, @Enabled);
                """, new
                {
                    CompanionGuid = companionGuid,
                    route.CategoryKey,
                    route.RecipientGuid,
                    route.KeepQuantity,
                    route.Enabled
                }, transaction, cancellationToken: cancellationToken));
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed class ProfileRow
    {
        public int TriggerFreeSlots { get; init; }
        public int TargetFreeSlots { get; init; }
        public bool AutomaticEnabled { get; init; }
    }

    private sealed class RouteRow
    {
        public string CategoryKey { get; init; } = "";
        public uint RecipientGuid { get; init; }
        public int KeepQuantity { get; init; }
        public bool Enabled { get; init; }
    }
}

public sealed record StoredCompanionLogisticsRoute(
    string CategoryKey, uint RecipientGuid, int KeepQuantity, bool Enabled);
