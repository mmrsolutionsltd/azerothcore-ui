using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/training")]
public sealed class TrainingController(
    AzerothCoreConnectionFactory connectionFactory,
    SpellMetadataProvider spellMetadataProvider,
    AzerothCoreSoapClient soapClient,
    ILogger<TrainingController> logger) : ControllerBase
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

    [HttpPost("professions/grant")]
    public async Task<ActionResult<AdministrationResult>> GrantProfessionTraining(
        GrantProfessionTrainingRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm the profession training first."));

        const string sql = """
            SELECT
                characters.name AS CharacterName,
                characters.online AS Online,
                trainerSpell.ReqSkillLine AS SkillId,
                skills.`max` AS CurrentMaximum
            FROM acore_auth.account AS account
            INNER JOIN acore_characters.characters AS characters ON characters.account = account.id
            INNER JOIN acore_characters.character_skills AS skills ON skills.guid = characters.guid
            INNER JOIN acore_world.trainer_spell AS trainerSpell
                ON trainerSpell.ReqSkillLine = skills.skill
                AND trainerSpell.ReqSkillRank <= skills.`value`
            WHERE characters.guid = @CharacterGuid
              AND trainerSpell.SpellId = @SpellId
              AND account.username NOT LIKE CONCAT(@PlayerBotPrefix, '%')
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
            LIMIT 1;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<GrantProfessionTrainingRow>(
            new CommandDefinition(sql, new
            {
                request.CharacterGuid,
                request.SpellId,
                PlayerBotPrefix = PlayerBotAccountPrefix,
                SkillIds = ProfessionNames.Keys.ToArray()
            }, cancellationToken: cancellationToken));

        if (row is null)
            return NotFound(new AdministrationResult(false,
                "That profession training is no longer available to this character."));

        var metadata = spellMetadataProvider.Find(request.SpellId);
        if (ProfessionTrainingRules.IsRankAlreadyLearned(metadata?.Name, row.CurrentMaximum))
            return Conflict(new AdministrationResult(false,
                "That profession rank is already covered by the character's current skill maximum."));

        if (!row.Online)
            return Conflict(new AdministrationResult(false,
                $"{row.CharacterName} must be online to receive profession training."));

        var player = AzerothCoreSoapClient.RequirePlayerName(row.CharacterName);
        var learnedSpellId = ProfessionTrainingRules.ResolveLearnedSpellId(
            request.SpellId, metadata);
        var output = await soapClient.ExecuteAsync(
            $"player learn {player} {learnedSpellId}", cancellationToken);
        var profession = ProfessionNames.GetValueOrDefault(row.SkillId, $"Skill {row.SkillId}");
        var trainingName = metadata?.Name ?? $"spell {request.SpellId}";

        logger.LogInformation(
            "Profession training granted to {Character}: {Profession}, {SpellId} ({TrainingName})",
            player, profession, learnedSpellId, trainingName);

        return Ok(new AdministrationResult(
            true,
            $"{player} learned {trainingName} ({profession}).",
            output));
    }

    [HttpGet("professions/starters")]
    public async Task<ActionResult<IReadOnlyList<ProfessionStarterCharacter>>> GetProfessionStarters(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                characters.guid AS CharacterGuid,
                characters.name AS CharacterName,
                characters.level AS CharacterLevel,
                characters.online AS Online,
                skills.skill AS SkillId
            FROM acore_auth.account AS account
            INNER JOIN acore_characters.characters AS characters ON characters.account = account.id
            LEFT JOIN acore_characters.character_skills AS skills
                ON skills.guid = characters.guid
                AND skills.skill IN @SkillIds
            WHERE account.username NOT LIKE CONCAT(@PlayerBotPrefix, '%')
            ORDER BY characters.name, skills.skill;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProfessionStarterRow>(
            new CommandDefinition(sql, new
            {
                PlayerBotPrefix = PlayerBotAccountPrefix,
                SkillIds = ProfessionCatalog.All.Keys.ToArray()
            }, cancellationToken: cancellationToken));

        return Ok(rows
            .GroupBy(row => new
            {
                row.CharacterGuid,
                row.CharacterName,
                row.CharacterLevel,
                row.Online
            })
            .Select(group =>
            {
                IReadOnlySet<ushort> knownSkills = group
                    .Where(row => row.SkillId.HasValue)
                    .Select(row => row.SkillId!.Value)
                    .ToHashSet();
                var available = ProfessionCatalog.All.Values
                    .Where(profession => ProfessionCatalog.CanLearn(
                        profession, group.Key.CharacterLevel, knownSkills))
                    .OrderBy(profession => profession.Category == "Primary" ? 0 : 1)
                    .ThenBy(profession => profession.Name)
                    .Select(profession => new AvailableProfession(
                        profession.SkillId,
                        profession.Name,
                        profession.Category,
                        profession.ApprenticeSpellId,
                        profession.RequiredLevel,
                        ProfessionPairingGuide.GetPairings(profession.SkillId)
                            .Select(pairing => pairing.Name)
                            .ToArray()))
                    .ToArray();
                var primaryCount = knownSkills.Count(skillId =>
                    ProfessionCatalog.All.TryGetValue(skillId, out var profession)
                    && profession.Category == "Primary");

                return new ProfessionStarterCharacter(
                    group.Key.CharacterGuid,
                    group.Key.CharacterName,
                    group.Key.CharacterLevel,
                    group.Key.Online,
                    primaryCount,
                    available);
            })
            .Where(character => character.AvailableProfessions.Count > 0)
            .OrderByDescending(character => character.Online)
            .ThenBy(character => character.CharacterName)
            .ToArray());
    }

    [HttpPost("professions/learn")]
    public async Task<ActionResult<AdministrationResult>> LearnProfession(
        LearnProfessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm learning the profession first."));
        if (!ProfessionCatalog.All.TryGetValue(request.SkillId, out var profession))
            return BadRequest(new AdministrationResult(false, "Unknown profession."));

        const string sql = """
            SELECT
                characters.guid AS CharacterGuid,
                characters.name AS CharacterName,
                characters.level AS CharacterLevel,
                characters.online AS Online,
                skills.skill AS SkillId
            FROM acore_auth.account AS account
            INNER JOIN acore_characters.characters AS characters ON characters.account = account.id
            LEFT JOIN acore_characters.character_skills AS skills
                ON skills.guid = characters.guid
                AND skills.skill IN @SkillIds
            WHERE characters.guid = @CharacterGuid
              AND account.username NOT LIKE CONCAT(@PlayerBotPrefix, '%');
            """;

        await using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<ProfessionStarterRow>(
            new CommandDefinition(sql, new
            {
                request.CharacterGuid,
                PlayerBotPrefix = PlayerBotAccountPrefix,
                SkillIds = ProfessionCatalog.All.Keys.ToArray()
            }, cancellationToken: cancellationToken))).AsList();

        if (rows.Count == 0)
            return NotFound(new AdministrationResult(false, "That player character does not exist."));

        var character = rows[0];
        IReadOnlySet<ushort> knownSkills = rows
            .Where(row => row.SkillId.HasValue)
            .Select(row => row.SkillId!.Value)
            .ToHashSet();

        if (!ProfessionCatalog.CanLearn(profession, character.CharacterLevel, knownSkills))
            return Conflict(new AdministrationResult(false,
                profession.Category == "Primary"
                && knownSkills.Count(skillId =>
                    ProfessionCatalog.All.TryGetValue(skillId, out var known)
                    && known.Category == "Primary") >= ProfessionCatalog.MaximumPrimaryProfessions
                    ? $"{character.CharacterName} already has two primary professions."
                    : $"{character.CharacterName} cannot learn {profession.Name}."));

        if (!character.Online)
            return Conflict(new AdministrationResult(false,
                $"{character.CharacterName} must be online to learn a profession."));

        var player = AzerothCoreSoapClient.RequirePlayerName(character.CharacterName);
        var output = await soapClient.ExecuteAsync(
            $"player learn {player} {profession.ApprenticeSpellId}", cancellationToken);

        logger.LogInformation(
            "Profession learned by {Character}: {Profession} ({SkillId}, spell {SpellId})",
            player, profession.Name, profession.SkillId, profession.ApprenticeSpellId);

        return Ok(new AdministrationResult(
            true,
            $"{player} learned Apprentice {profession.Name}.",
            output));
    }

    [HttpGet("professions/manage")]
    public async Task<ActionResult<IReadOnlyList<ProfessionManagementCharacter>>> GetProfessionManagement(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                characters.guid AS CharacterGuid,
                characters.name AS CharacterName,
                characters.online AS Online,
                skills.skill AS SkillId,
                skills.`value` AS CurrentSkill,
                skills.`max` AS MaximumSkill
            FROM acore_auth.account AS account
            INNER JOIN acore_characters.characters AS characters ON characters.account = account.id
            INNER JOIN acore_characters.character_skills AS skills ON skills.guid = characters.guid
            WHERE account.username NOT LIKE CONCAT(@PlayerBotPrefix, '%')
              AND skills.skill IN @SkillIds
            ORDER BY characters.name, skills.skill;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProfessionManagementRow>(
            new CommandDefinition(sql, new
            {
                PlayerBotPrefix = PlayerBotAccountPrefix,
                SkillIds = ProfessionCatalog.All.Keys.ToArray()
            }, cancellationToken: cancellationToken));

        return Ok(rows
            .GroupBy(row => new
            {
                row.CharacterGuid,
                row.CharacterName,
                row.Online
            })
            .Select(group => new ProfessionManagementCharacter(
                group.Key.CharacterGuid,
                group.Key.CharacterName,
                group.Key.Online,
                group.Select(row =>
                    {
                        var definition = ProfessionCatalog.All[row.SkillId];
                        return new ManagedProfession(
                            row.SkillId,
                            definition.Name,
                            definition.Category,
                            row.CurrentSkill,
                            row.MaximumSkill,
                            ProfessionPairingGuide.GetPairings(row.SkillId)
                                .Select(pairing => pairing.Name)
                                .ToArray());
                    })
                    .OrderBy(profession => profession.Category == "Primary" ? 0 : 1)
                    .ThenBy(profession => profession.Name)
                    .ToArray()))
            .OrderByDescending(character => character.Online)
            .ThenBy(character => character.CharacterName)
            .ToArray());
    }

    [HttpPost("professions/unlearn")]
    public async Task<ActionResult<AdministrationResult>> UnlearnProfession(
        UnlearnProfessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm unlearning the profession first."));
        if (!ProfessionCatalog.All.TryGetValue(request.SkillId, out var profession))
            return BadRequest(new AdministrationResult(false, "Unknown profession."));

        const string sql = """
            SELECT
                characters.name AS CharacterName,
                characters.online AS Online
            FROM acore_auth.account AS account
            INNER JOIN acore_characters.characters AS characters ON characters.account = account.id
            INNER JOIN acore_characters.character_skills AS skills ON skills.guid = characters.guid
            WHERE characters.guid = @CharacterGuid
              AND skills.skill = @SkillId
              AND account.username NOT LIKE CONCAT(@PlayerBotPrefix, '%')
            LIMIT 1;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var character = await connection.QuerySingleOrDefaultAsync<UnlearnProfessionRow>(
            new CommandDefinition(sql, new
            {
                request.CharacterGuid,
                request.SkillId,
                PlayerBotPrefix = PlayerBotAccountPrefix
            }, cancellationToken: cancellationToken));

        if (character is null)
            return NotFound(new AdministrationResult(false,
                "That character does not currently know this profession."));
        if (!character.Online)
            return Conflict(new AdministrationResult(false,
                $"{character.CharacterName} must be online to unlearn a profession."));

        var player = AzerothCoreSoapClient.RequirePlayerName(character.CharacterName);
        var output = await soapClient.ExecuteAsync(
            ProfessionTrainingRules.BuildUnlearnCommand(player, profession),
            cancellationToken);

        logger.LogWarning(
            "Profession unlearned by {Character}: {Profession} ({SkillId}, spell {SpellId})",
            player, profession.Name, profession.SkillId, profession.ApprenticeSpellId);

        return Ok(new AdministrationResult(
            true,
            $"{player} unlearned {profession.Name}. Its skill progress and recipes were removed.",
            output));
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

    private sealed class GrantProfessionTrainingRow
    {
        public string CharacterName { get; init; } = string.Empty;
        public bool Online { get; init; }
        public ushort SkillId { get; init; }
        public ushort CurrentMaximum { get; init; }
    }

    private sealed class ProfessionStarterRow
    {
        public uint CharacterGuid { get; init; }
        public string CharacterName { get; init; } = string.Empty;
        public byte CharacterLevel { get; init; }
        public bool Online { get; init; }
        public ushort? SkillId { get; init; }
    }

    private sealed class ProfessionManagementRow
    {
        public uint CharacterGuid { get; init; }
        public string CharacterName { get; init; } = string.Empty;
        public bool Online { get; init; }
        public ushort SkillId { get; init; }
        public ushort CurrentSkill { get; init; }
        public ushort MaximumSkill { get; init; }
    }

    private sealed class UnlearnProfessionRow
    {
        public string CharacterName { get; init; } = string.Empty;
        public bool Online { get; init; }
    }

    private sealed record DashboardRow(
        uint AccountId,
        string Username,
        uint CharacterGuid,
        string CharacterName,
        byte CharacterLevel,
        TrainingRequirement Requirement);
}
