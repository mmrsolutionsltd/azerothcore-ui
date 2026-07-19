using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/training")]
public sealed class TrainingController(
    AzerothCoreConnectionFactory connectionFactory,
    SpellMetadataProvider spellMetadataProvider) : ControllerBase
{
    private const string PlayerBotAccountPrefix = "rndbot";

    private static readonly IReadOnlyDictionary<byte, string> ClassNames =
        new Dictionary<byte, string>
        {
            [1] = "Warrior", [2] = "Paladin", [3] = "Hunter", [4] = "Rogue",
            [5] = "Priest", [6] = "Death Knight", [7] = "Shaman", [8] = "Mage",
            [9] = "Warlock", [11] = "Druid"
        };

    private static readonly IReadOnlyDictionary<ushort, string> ProfessionNames =
        new Dictionary<ushort, string>
        {
            [129] = "First Aid", [164] = "Blacksmithing", [165] = "Leatherworking",
            [171] = "Alchemy", [182] = "Herbalism", [185] = "Cooking", [186] = "Mining",
            [197] = "Tailoring", [202] = "Engineering", [333] = "Enchanting",
            [356] = "Fishing", [393] = "Skinning", [755] = "Jewelcrafting",
            [773] = "Inscription"
        };

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CharacterTrainingSummary>>> GetAvailableTraining(
        CancellationToken cancellationToken)
    {
        const string classSql = """
            SELECT
                account.id AS AccountId,
                account.username AS Username,
                characters.guid AS CharacterGuid,
                characters.name AS CharacterName,
                characters.level AS CharacterLevel,
                characters.class AS ClassId,
                trainerSpell.SpellId,
                trainerSpell.ReqLevel AS RequiredLevel,
                MIN(trainerSpell.MoneyCost) AS TrainingCost
            FROM acore_auth.account AS account
            INNER JOIN acore_characters.characters AS characters ON characters.account = account.id
            INNER JOIN acore_world.trainer_spell AS trainerSpell
                ON trainerSpell.TrainerId = CASE characters.class
                    WHEN 1 THEN 1 WHEN 2 THEN 3 WHEN 3 THEN 7 WHEN 4 THEN 9
                    WHEN 5 THEN 11 WHEN 6 THEN 13 WHEN 7 THEN 14 WHEN 8 THEN 16
                    WHEN 9 THEN 31 WHEN 11 THEN 33 ELSE 0 END
            WHERE account.username NOT LIKE CONCAT(@PlayerBotPrefix, '%')
              AND trainerSpell.ReqLevel <= characters.level
              AND trainerSpell.ReqSkillLine = 0
              AND NOT EXISTS (
                  SELECT 1 FROM acore_characters.character_spell AS knownSpell
                  WHERE knownSpell.guid = characters.guid
                    AND knownSpell.spell = trainerSpell.SpellId
              )
              AND (trainerSpell.ReqAbility1 = 0 OR EXISTS (
                  SELECT 1 FROM acore_characters.character_spell AS ability1
                  WHERE ability1.guid = characters.guid AND ability1.spell = trainerSpell.ReqAbility1))
              AND (trainerSpell.ReqAbility2 = 0 OR EXISTS (
                  SELECT 1 FROM acore_characters.character_spell AS ability2
                  WHERE ability2.guid = characters.guid AND ability2.spell = trainerSpell.ReqAbility2))
              AND (trainerSpell.ReqAbility3 = 0 OR EXISTS (
                  SELECT 1 FROM acore_characters.character_spell AS ability3
                  WHERE ability3.guid = characters.guid AND ability3.spell = trainerSpell.ReqAbility3))
            GROUP BY account.id, account.username, characters.guid, characters.name,
                characters.level, characters.class, trainerSpell.SpellId, trainerSpell.ReqLevel;
            """;

        const string professionSql = """
            SELECT
                account.id AS AccountId,
                account.username AS Username,
                characters.guid AS CharacterGuid,
                characters.name AS CharacterName,
                characters.level AS CharacterLevel,
                trainerSpell.ReqSkillLine AS SkillId,
                trainerSpell.SpellId,
                trainerSpell.ReqLevel AS RequiredLevel,
                trainerSpell.ReqSkillRank AS RequiredSkillRank,
                skills.`max` AS CurrentMaximum,
                MIN(trainerSpell.MoneyCost) AS TrainingCost
            FROM acore_auth.account AS account
            INNER JOIN acore_characters.characters AS characters ON characters.account = account.id
            INNER JOIN acore_characters.character_skills AS skills ON skills.guid = characters.guid
            INNER JOIN acore_world.trainer_spell AS trainerSpell
                ON trainerSpell.ReqSkillLine = skills.skill
                AND trainerSpell.ReqSkillRank <= skills.`value`
            WHERE account.username NOT LIKE CONCAT(@PlayerBotPrefix, '%')
              AND skills.skill IN @SkillIds
              AND trainerSpell.ReqLevel <= characters.level
              AND NOT EXISTS (
                  SELECT 1 FROM acore_characters.character_spell AS knownSpell
                  WHERE knownSpell.guid = characters.guid
                    AND knownSpell.spell = trainerSpell.SpellId
              )
              AND (trainerSpell.ReqAbility1 = 0 OR EXISTS (
                  SELECT 1 FROM acore_characters.character_spell AS ability1
                  WHERE ability1.guid = characters.guid AND ability1.spell = trainerSpell.ReqAbility1))
              AND (trainerSpell.ReqAbility2 = 0 OR EXISTS (
                  SELECT 1 FROM acore_characters.character_spell AS ability2
                  WHERE ability2.guid = characters.guid AND ability2.spell = trainerSpell.ReqAbility2))
              AND (trainerSpell.ReqAbility3 = 0 OR EXISTS (
                  SELECT 1 FROM acore_characters.character_spell AS ability3
                  WHERE ability3.guid = characters.guid AND ability3.spell = trainerSpell.ReqAbility3))
            GROUP BY account.id, account.username, characters.guid, characters.name,
                characters.level, trainerSpell.ReqSkillLine, trainerSpell.SpellId,
                trainerSpell.ReqLevel, trainerSpell.ReqSkillRank, skills.`max`;
            """;

        var parameters = new
        {
            PlayerBotPrefix = PlayerBotAccountPrefix,
            SkillIds = ProfessionNames.Keys.ToArray()
        };

        await using var connection = connectionFactory.CreateConnection();
        var classRows = await connection.QueryAsync<ClassTrainingRow>(
            new CommandDefinition(classSql, parameters, cancellationToken: cancellationToken));
        var professionRows = await connection.QueryAsync<ProfessionTrainingRow>(
            new CommandDefinition(professionSql, parameters, cancellationToken: cancellationToken));

        var rows = classRows.Select(row => ToDashboardRow(
                row,
                "Class",
                ClassNames.GetValueOrDefault(row.ClassId, $"Class {row.ClassId}"),
                null))
            .Concat(professionRows
                .Where(row => !ProfessionTrainingRules.IsRankAlreadyLearned(
                    spellMetadataProvider.Find(row.SpellId)?.Name,
                    row.CurrentMaximum))
                .Select(row => ToDashboardRow(
                    row,
                    "Profession",
                    ProfessionNames.GetValueOrDefault(row.SkillId, $"Skill {row.SkillId}"),
                    row.RequiredSkillRank)))
            .ToArray();

        return Ok(rows
            .GroupBy(row => new
            {
                row.AccountId, row.Username, row.CharacterGuid,
                row.CharacterName, row.CharacterLevel
            })
            .Select(group => new CharacterTrainingSummary(
                group.Key.AccountId,
                group.Key.Username,
                group.Key.CharacterGuid,
                group.Key.CharacterName,
                group.Key.CharacterLevel,
                group.Select(row => row.Requirement)
                    .OrderBy(requirement => requirement.Category)
                    .ThenBy(requirement => requirement.Discipline)
                    .ThenBy(requirement => requirement.RequiredLevel)
                    .ThenBy(requirement => requirement.RequiredSkillRank)
                    .ThenBy(requirement => requirement.Name)
                    .ToArray()))
            .OrderBy(summary => summary.Username)
            .ThenBy(summary => summary.CharacterName)
            .ToArray());
    }

    private DashboardRow ToDashboardRow(
        TrainingRow row,
        string category,
        string discipline,
        ushort? requiredSkillRank)
    {
        var metadata = spellMetadataProvider.Find(row.SpellId);
        return new DashboardRow(
            row.AccountId,
            row.Username,
            row.CharacterGuid,
            row.CharacterName,
            row.CharacterLevel,
            new TrainingRequirement(
                category,
                discipline,
                row.SpellId,
                metadata?.Name,
                metadata?.Rank,
                row.RequiredLevel,
                requiredSkillRank,
                row.TrainingCost));
    }

    private abstract class TrainingRow
    {
        public uint AccountId { get; init; }
        public string Username { get; init; } = string.Empty;
        public uint CharacterGuid { get; init; }
        public string CharacterName { get; init; } = string.Empty;
        public byte CharacterLevel { get; init; }
        public uint SpellId { get; init; }
        public byte RequiredLevel { get; init; }
        public uint TrainingCost { get; init; }
    }

    private sealed class ClassTrainingRow : TrainingRow
    {
        public byte ClassId { get; init; }
    }

    private sealed class ProfessionTrainingRow : TrainingRow
    {
        public ushort SkillId { get; init; }
        public ushort RequiredSkillRank { get; init; }
        public ushort CurrentMaximum { get; init; }
    }

    private sealed record DashboardRow(
        uint AccountId,
        string Username,
        uint CharacterGuid,
        string CharacterName,
        byte CharacterLevel,
        TrainingRequirement Requirement);
}
