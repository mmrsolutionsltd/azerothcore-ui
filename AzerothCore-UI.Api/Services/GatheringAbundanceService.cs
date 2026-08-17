using AzerothCore_UI.Api.Data;
using Dapper;

namespace AzerothCore_UI.Api.Services;

public sealed record GatheringAbundanceSettings(
    int HerbAbundancePercent,
    int MiningAbundancePercent);

public sealed class GatheringAbundanceService(
    AzerothCoreConnectionFactory connectionFactory)
{
    public const int MinimumPercentage = 25;
    public const int MaximumPercentage = 500;
    public const int PercentageStep = 5;

    // Lock.dbc skill-lock entries for the WotLK 3.3.5a Herbalism and Mining
    // lock types. Using locks identifies actual gatherable nodes and excludes
    // ordinary chests that happen to contain herbs or ore.
    private static readonly ushort[] HerbLockIds =
    [
        8, 9, 10, 11, 26, 27, 29, 30, 31, 32, 33, 34, 35, 45, 47, 48, 49,
        50, 51, 259, 439, 440, 441, 442, 443, 444, 519, 521, 1119, 1120,
        1121, 1122, 1123, 1124, 1639, 1641, 1642, 1643, 1644, 1645, 1646,
        1702, 1714, 1786, 1787, 1788, 1789, 1790, 1791, 1792, 1793
    ];

    private static readonly ushort[] MiningLockIds =
    [
        18, 19, 20, 21, 22, 25, 38, 39, 40, 41, 42, 379, 380, 399, 400,
        719, 939, 1632, 1649, 1650, 1651, 1652, 1713, 1771, 1775, 1782,
        1783, 1784, 1785, 1800, 1802, 1860
    ];

    public async Task<GatheringAbundanceSettings> GetAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateMaintenanceConnection();
        var command = new CommandDefinition("""
            SELECT herb_abundance_percent HerbAbundancePercent,
                   mining_abundance_percent MiningAbundancePercent
            FROM azerothcore_ui.gathering_abundance_settings
            WHERE id=1
            """, cancellationToken: cancellationToken);
        var value = await connection.QuerySingleOrDefaultAsync<SettingsRow>(command);
        return value is null
            ? new GatheringAbundanceSettings(100, 100)
            : new GatheringAbundanceSettings(
                value.HerbAbundancePercent, value.MiningAbundancePercent);
    }

    public async Task<GatheringAbundanceSettings> UpdateAsync(
        int herbAbundancePercent,
        int miningAbundancePercent,
        CancellationToken cancellationToken)
    {
        ValidatePercentage(herbAbundancePercent, "Herb abundance");
        ValidatePercentage(miningAbundancePercent, "Mining abundance");

        await using var connection = connectionFactory.CreateMaintenanceConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await CaptureBaselinesAsync(
                connection, transaction, "herb", HerbLockIds, cancellationToken);
            await CaptureBaselinesAsync(
                connection, transaction, "mining", MiningLockIds, cancellationToken);

            await ApplyPercentageAsync(
                connection, transaction, "herb", herbAbundancePercent,
                cancellationToken);
            await ApplyPercentageAsync(
                connection, transaction, "mining", miningAbundancePercent,
                cancellationToken);

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO azerothcore_ui.gathering_abundance_settings
                    (id, herb_abundance_percent, mining_abundance_percent,
                     updated_at_utc)
                VALUES (1, @HerbAbundancePercent, @MiningAbundancePercent,
                        UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE
                    herb_abundance_percent=VALUES(herb_abundance_percent),
                    mining_abundance_percent=VALUES(mining_abundance_percent),
                    updated_at_utc=VALUES(updated_at_utc)
                """, new
                {
                    HerbAbundancePercent = herbAbundancePercent,
                    MiningAbundancePercent = miningAbundancePercent
                }, transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new GatheringAbundanceSettings(
                herbAbundancePercent, miningAbundancePercent);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public static void ValidatePercentage(int value, string displayName)
    {
        if (value is < MinimumPercentage or > MaximumPercentage
            || value % PercentageStep != 0)
        {
            throw new ArgumentException(
                $"{displayName} must be between {MinimumPercentage}% and "
                + $"{MaximumPercentage}% in {PercentageStep}% increments.");
        }
    }

    private static async Task CaptureBaselinesAsync(
        MySqlConnector.MySqlConnection connection,
        MySqlConnector.MySqlTransaction transaction,
        string category,
        IReadOnlyCollection<ushort> lockIds,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO azerothcore_ui.gathering_spawn_baseline
                (guid, gameobject_entry, category, original_spawntimesecs)
            SELECT spawn.guid, spawn.id, @Category, spawn.spawntimesecs
            FROM acore_world.gameobject spawn
            INNER JOIN acore_world.gameobject_template template
                ON template.entry=spawn.id
            WHERE template.type=3 AND template.data0 IN @LockIds
            ON DUPLICATE KEY UPDATE
                original_spawntimesecs=IF(
                    gameobject_entry<>VALUES(gameobject_entry)
                    OR category<>VALUES(category),
                    VALUES(original_spawntimesecs), original_spawntimesecs),
                gameobject_entry=VALUES(gameobject_entry),
                category=VALUES(category)
            """, new { Category = category, LockIds = lockIds }, transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task ApplyPercentageAsync(
        MySqlConnector.MySqlConnection connection,
        MySqlConnector.MySqlTransaction transaction,
        string category,
        int percentage,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE acore_world.gameobject spawn
            INNER JOIN azerothcore_ui.gathering_spawn_baseline baseline
                ON baseline.guid=spawn.guid
               AND baseline.gameobject_entry=spawn.id
               AND baseline.category=@Category
            SET spawn.spawntimesecs=GREATEST(
                1, CAST(ROUND(
                    baseline.original_spawntimesecs * 100.0 / @Percentage)
                    AS UNSIGNED))
            """, new { Category = category, Percentage = percentage },
            transaction, cancellationToken: cancellationToken));
    }

    private sealed class SettingsRow
    {
        public int HerbAbundancePercent { get; init; }
        public int MiningAbundancePercent { get; init; }
    }
}
