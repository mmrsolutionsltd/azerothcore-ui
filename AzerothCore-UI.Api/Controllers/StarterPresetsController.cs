using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/starter-presets")]
public sealed class StarterPresetsController(
    AzerothCoreConnectionFactory connectionFactory,
    AzerothCoreSoapClient soapClient,
    ILogger<StarterPresetsController> logger) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<ActionResult<StarterPresetPreview>> Preview(
        StarterPresetRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        try
        {
            StarterPresetPlanner.Validate(request);
            return Ok(await BuildPreviewAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new AdministrationResult(false, exception.Message));
        }
    }

    [HttpPost("apply")]
    public async Task<ActionResult<StarterPresetApplyResult>> Apply(
        StarterPresetRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        try { StarterPresetPlanner.Validate(request); }
        catch (ArgumentException exception)
        {
            return BadRequest(new AdministrationResult(false, exception.Message));
        }
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm applying the starter preset first."));

        StarterPresetPreview preview;
        try { preview = await BuildPreviewAsync(request, cancellationToken); }
        catch (ArgumentException exception)
        {
            return BadRequest(new AdministrationResult(false, exception.Message));
        }
        var characterResults = new List<StarterPresetCharacterResult>();
        foreach (var character in preview.Characters)
        {
            var actionResults = new List<StarterPresetActionResult>();
            foreach (var action in character.Actions)
            {
                if (action.Skipped)
                {
                    actionResults.Add(new(action.Description, true, true,
                        action.SkipReason ?? "Skipped."));
                    continue;
                }
                try
                {
                    string command;
                    if (action.Kind == "Money")
                    {
                        command = $"send money {AzerothCoreSoapClient.RequirePlayerName(character.PlayerName)} " +
                                  $"\"Family starter preset\" \"Starting money from the server administrator.\" {action.Quantity}";
                    }
                    else if (action.ItemId is { } itemId && action.Delivery == "Direct")
                    {
                        command = $"additem {AzerothCoreSoapClient.RequirePlayerName(character.PlayerName)} {itemId} {action.Quantity}";
                    }
                    else if (action.ItemId is { } mailedItemId)
                    {
                        command = $"send items {AzerothCoreSoapClient.RequirePlayerName(character.PlayerName)} " +
                                  $"\"Family starter preset\" \"Supplies from the server administrator.\" {mailedItemId}:{action.Quantity}";
                    }
                    else
                    {
                        throw new InvalidOperationException("The preset action is incomplete.");
                    }

                    await soapClient.ExecuteAsync(command, cancellationToken);
                    actionResults.Add(new(action.Description, true, false,
                        action.Delivery == "Direct" ? "Given directly." : "Sent by in-game mail."));
                    logger.LogInformation(
                        "Starter preset action: Player={Player}; Preset={Preset}; Kind={Kind}; Item={Item}; Quantity={Quantity}; Delivery={Delivery}",
                        character.PlayerName, request.Preset, action.Kind, action.ItemId, action.Quantity, action.Delivery);
                }
                catch (Exception exception)
                {
                    actionResults.Add(new(action.Description, false, false, exception.Message));
                }
            }
            characterResults.Add(new(
                character.PlayerName,
                actionResults.All(result => result.Success),
                actionResults));
        }

        var success = characterResults.All(result => result.Success);
        var successfulCharacters = characterResults.Count(result => result.Success);
        return Ok(new StarterPresetApplyResult(
            success,
            success
                ? $"Starter preset completed for all {successfulCharacters} selected characters."
                : $"Starter preset completed fully for {successfulCharacters} of {characterResults.Count} characters. Review individual actions.",
            characterResults));
    }

    private async Task<StarterPresetPreview> BuildPreviewAsync(
        StarterPresetRequest request, CancellationToken cancellationToken)
    {
        var normalizedNames = request.PlayerNames
            .Select(AzerothCoreSoapClient.RequirePlayerName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await using var connection = connectionFactory.CreateConnection();
        var characters = (await connection.QueryAsync<CharacterRow>(new CommandDefinition("""
            SELECT characterData.guid AS Guid, characterData.name AS PlayerName,
                   characterData.level AS Level, characterData.race AS Race,
                   characterData.class AS Class, characterData.online AS OnlineValue
            FROM acore_characters.characters characterData
            INNER JOIN acore_auth.account account ON account.id = characterData.account
            WHERE characterData.name IN @PlayerNames
              AND account.username NOT LIKE 'rndbot%'
              AND account.username <> 'AHBOT';
            """, new { PlayerNames = normalizedNames }, cancellationToken: cancellationToken))).ToArray();
        if (characters.Length != normalizedNames.Length)
            throw new ArgumentException("One or more selected names are not real-player characters.");

        var itemRows = await connection.QueryAsync<OwnedItemRow>(new CommandDefinition("""
            SELECT owned.Guid, owned.ItemId, SUM(owned.Quantity) AS Quantity
            FROM (
                SELECT instance.owner_guid AS Guid, instance.itemEntry AS ItemId,
                       instance.count AS Quantity
                FROM acore_characters.item_instance instance
                WHERE instance.owner_guid IN @Guids
                  AND NOT EXISTS (
                      SELECT 1 FROM acore_characters.mail_items mailed
                      WHERE mailed.item_guid = instance.guid)
                UNION ALL
                SELECT mailed.receiver AS Guid, instance.itemEntry AS ItemId,
                       instance.count AS Quantity
                FROM acore_characters.mail_items mailed
                INNER JOIN acore_characters.item_instance instance ON instance.guid = mailed.item_guid
                WHERE mailed.receiver IN @Guids
            ) owned
            GROUP BY owned.Guid, owned.ItemId;
            """, new { Guids = characters.Select(character => character.Guid).ToArray() },
            cancellationToken: cancellationToken));
        var ownedByCharacter = itemRows
            .GroupBy(item => item.Guid)
            .ToDictionary(group => group.Key,
                group => (IReadOnlyDictionary<uint, int>)group.ToDictionary(
                    item => item.ItemId, item => item.Quantity));

        var previews = normalizedNames.Select(name =>
        {
            var character = characters.Single(item =>
                item.PlayerName.Equals(name, StringComparison.OrdinalIgnoreCase));
            var owned = ownedByCharacter.GetValueOrDefault(character.Guid)
                ?? new Dictionary<uint, int>();
            return new StarterPresetCharacterPreview(
                character.PlayerName, character.Level, character.Race, character.Class,
                character.OnlineValue != 0,
                StarterPresetPlanner.Plan(
                    request.Preset, character.Class, character.Level, character.OnlineValue != 0, owned,
                    request.BagCount, request.IncludeHeirlooms, request.IncludeHearthstone,
                    request.IncludeFoodAndDrink, request.IncludeClassSupplies, request.MoneyGold));
        }).ToArray();
        return new(request.Preset, previews);
    }

    private bool IsLocalRequest() => HttpContext.Connection.RemoteIpAddress is null
        || System.Net.IPAddress.IsLoopback(HttpContext.Connection.RemoteIpAddress);

    private sealed class CharacterRow
    {
        public uint Guid { get; init; }
        public string PlayerName { get; init; } = "";
        public byte Level { get; init; }
        public byte Race { get; init; }
        public byte Class { get; init; }
        public byte OnlineValue { get; init; }
    }

    private sealed class OwnedItemRow
    {
        public uint Guid { get; init; }
        public uint ItemId { get; init; }
        public int Quantity { get; init; }
    }
}
