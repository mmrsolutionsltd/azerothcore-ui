/*
 * AzerothCore-UI web administration commands.
 * GPL-2.0-or-later, matching AzerothCore.
 */

#include "Chat.h"
#include "Creature.h"
#include "DatabaseEnv.h"
#include "Group.h"
#include "GroupMgr.h"
#include "Guild.h"
#include "GuildMgr.h"
#include "DBCStores.h"
#include "LFGMgr.h"
#include "Map.h"
#include "MotionMaster.h"
#include "ObjectAccessor.h"
#include "ObjectMgr.h"
#include "Player.h"
#include "PlayerbotAI.h"
#include "PlayerbotMgr.h"
#include "RandomPlayerbotMgr.h"
#include "ScriptMgr.h"
#include "TemporarySummon.h"
#include "World.h"

#include <sstream>
#include <string>
#include <vector>
#include <algorithm>
#include <array>
#include <cstdlib>

using namespace Acore::ChatCommands;

namespace
{
class WebAdminCommandScript final : public CommandScript
{
public:
    WebAdminCommandScript() : CommandScript("WebAdminCommandScript") { }

    ChatCommandTable GetCommands() const override
    {
        static ChatCommandTable groupCommands =
        {
            { "inspect", HandleGroupInspectCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "add", HandleGroupAddCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "remove", HandleGroupRemoveCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "clear", HandleGroupClearCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "fill", HandleGroupFillCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "launch", HandleGroupLaunchCommand, SEC_ADMINISTRATOR, Console::Yes }
        };
        static ChatCommandTable dungeonCommands =
        {
            { "list", HandleDungeonListCommand, SEC_ADMINISTRATOR, Console::Yes }
        };
        static ChatCommandTable creatureCommands =
        {
            { "spawn", HandleCreatureSpawnCommand, SEC_ADMINISTRATOR, Console::Yes }
        };
        static ChatCommandTable weaponCommands =
        {
            { "inspect", HandleWeaponInspectCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "learn", HandleWeaponLearnCommand, SEC_ADMINISTRATOR, Console::Yes }
        };
        static ChatCommandTable questCommands =
        {
            { "return", HandleQuestReturnCommand, SEC_ADMINISTRATOR, Console::Yes }
        };
        static ChatCommandTable npcCommands =
        {
            { "teleport", HandleNpcTeleportCommand, SEC_ADMINISTRATOR, Console::Yes }
        };
        static ChatCommandTable guildCommands =
        {
            { "inspect", HandleGuildInspectCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "unlocktab", HandleGuildUnlockTabCommand, SEC_ADMINISTRATOR, Console::Yes }
        };
        static ChatCommandTable companionCommands =
        {
            { "inspect", HandleCompanionInspectCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "start", HandleCompanionStartCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "dismiss", HandleCompanionDismissCommand, SEC_ADMINISTRATOR, Console::Yes }
        };
        static ChatCommandTable webAdminCommands =
        {
            { "move", HandleMoveCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "speed", HandleSpeedCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "group", groupCommands },
            { "dungeon", dungeonCommands },
            { "creature", creatureCommands },
            { "weapon", weaponCommands },
            { "quest", questCommands },
            { "npc", npcCommands }
            ,{ "guild", guildCommands },
            { "companion", companionCommands }
        };
        static ChatCommandTable commands =
        {
            { "webadmin", webAdminCommands }
        };
        return commands;
    }

private:
    static bool ParseCompanionArguments(
        ChatHandler* handler, char const* args, Player*& leader,
        std::string& companionName, ObjectGuid& companionGuid)
    {
        std::istringstream input(args ? args : "");
        std::string leaderName, unexpected;
        if (!(input >> leaderName >> companionName) || (input >> unexpected))
        {
            handler->SendErrorMessage("Usage: webadmin companion <command> <leader> <companion>");
            return false;
        }
        leader = RequireOnlinePlayer(handler, leaderName, "Leader");
        if (!leader || sRandomPlayerbotMgr.IsRandomBot(leader))
        {
            handler->SendErrorMessage("The companion leader must be a real online player.");
            return false;
        }
        if (!normalizePlayerName(companionName))
        {
            handler->SendErrorMessage("The companion character was not found.");
            return false;
        }
        companionGuid = sCharacterCache->GetCharacterGuidByName(companionName);
        if (companionGuid.IsEmpty() || companionGuid == leader->GetGUID())
        {
            handler->SendErrorMessage("Select a different companion character.");
            return false;
        }
        return true;
    }

    static bool HandleCompanionStartCommand(ChatHandler* handler, char const* args)
    {
        Player* leader = nullptr;
        std::string companionName;
        ObjectGuid companionGuid;
        if (!ParseCompanionArguments(handler, args, leader, companionName, companionGuid))
            return false;
        if (ObjectAccessor::FindConnectedPlayer(companionGuid))
        {
            handler->SendErrorMessage("The companion is already online.");
            return false;
        }
        uint32 companionAccount = sCharacterCache->GetCharacterAccountIdByGuid(companionGuid);
        if (!companionAccount || companionAccount == leader->GetSession()->GetAccountId())
        {
            handler->SendErrorMessage(
                "The leader and companion must use different game accounts.");
            return false;
        }
        QueryResult result = CharacterDatabase.Query(
            "SELECT race FROM characters WHERE guid = {}", companionGuid.GetCounter());
        if (!result || Player::TeamIdForRace(result->Fetch()[0].Get<uint8>())
            != leader->GetTeamId(true))
        {
            handler->SendErrorMessage(
                "The leader and companion must belong to the same faction.");
            return false;
        }
        PlayerbotMgr* manager =
            PlayerbotsMgr::instance().GetPlayerbotMgr(leader);
        if (!manager)
        {
            handler->SendErrorMessage("PlayerBots is not available for the leader.");
            return false;
        }
        std::string command = "add " + companionName;
        std::vector<std::string> messages =
            manager->HandlePlayerbotCommand(command.data(), leader);
        for (std::string const& message : messages)
            handler->PSendSysMessage("WEBADMIN_COMPANION_RESULT\t{}", message);
        handler->PSendSysMessage(
            "Questing companion {} is logging in for {}.", companionName, leader->GetName());
        return true;
    }

    static bool HandleCompanionDismissCommand(ChatHandler* handler, char const* args)
    {
        Player* leader = nullptr;
        std::string companionName;
        ObjectGuid companionGuid;
        if (!ParseCompanionArguments(handler, args, leader, companionName, companionGuid))
            return false;
        PlayerbotMgr* manager =
            PlayerbotsMgr::instance().GetPlayerbotMgr(leader);
        if (!manager || !manager->GetPlayerBot(companionGuid))
        {
            handler->SendErrorMessage("That character is not this leader's active companion.");
            return false;
        }
        std::string command = "remove " + companionName;
        std::vector<std::string> messages =
            manager->HandlePlayerbotCommand(command.data(), leader);
        for (std::string const& message : messages)
            handler->PSendSysMessage("WEBADMIN_COMPANION_RESULT\t{}", message);
        handler->PSendSysMessage(
            "Questing companion {} is logging out.", companionName);
        return true;
    }

    static bool HandleCompanionInspectCommand(ChatHandler* handler, char const* args)
    {
        Player* leader = ParseLeader(handler, args);
        if (!leader) return false;
        PlayerbotMgr* manager =
            PlayerbotsMgr::instance().GetPlayerbotMgr(leader);
        if (!manager)
        {
            handler->SendErrorMessage("PlayerBots is not available for the leader.");
            return false;
        }
        unsigned count = 0;
        for (auto iterator = manager->GetPlayerBotsBegin();
             iterator != manager->GetPlayerBotsEnd(); ++iterator)
        {
            Player* bot = iterator->second;
            if (!bot || sRandomPlayerbotMgr.IsRandomBot(bot)) continue;
            handler->PSendSysMessage(
                "WEBADMIN_COMPANION\t{}\t{}\t{}\t{}",
                bot->GetName(), bot->GetLevel(), bot->getClass(),
                bot->GetGroup() == leader->GetGroup() ? 1 : 0);
            ++count;
        }
        handler->PSendSysMessage(
            "WEBADMIN_COMPANION_SUMMARY\t{}\t{}", leader->GetName(), count);
        return true;
    }

    static Guild* RequirePlayerGuild(ChatHandler* handler, Player* player)
    {
        Guild* guild = player ? sGuildMgr->GetGuildById(player->GetGuildId()) : nullptr;
        if (!guild)
            handler->SendErrorMessage("The player is not in a guild.");
        return guild;
    }

    static uint8 PurchasedGuildTabs(Guild const* guild)
    {
        QueryResult result = CharacterDatabase.Query(
            "SELECT COUNT(*) FROM guild_bank_tab WHERE guildid = {}", guild->GetId());
        return result ? std::min<uint8>(result->Fetch()[0].Get<uint8>(), GUILD_BANK_MAX_TABS) : 0;
    }

    static uint32 GuildTabPrice(uint8 tabId)
    {
        static ServerConfigs const configs[GUILD_BANK_MAX_TABS] = {
            CONFIG_GUILD_BANK_TAB_COST_0, CONFIG_GUILD_BANK_TAB_COST_1,
            CONFIG_GUILD_BANK_TAB_COST_2, CONFIG_GUILD_BANK_TAB_COST_3,
            CONFIG_GUILD_BANK_TAB_COST_4, CONFIG_GUILD_BANK_TAB_COST_5
        };
        return tabId < GUILD_BANK_MAX_TABS ? sWorld->getIntConfig(configs[tabId]) : 0;
    }

    static bool HandleGuildInspectCommand(ChatHandler* handler, char const* args)
    {
        Player* player = ParseOnlinePlayer(handler, args, "Player");
        if (!player) return false;
        Guild* guild = RequirePlayerGuild(handler, player);
        if (!guild) return false;
        uint8 tabs = PurchasedGuildTabs(guild);
        uint32 nextPrice = tabs < GUILD_BANK_MAX_TABS ? GuildTabPrice(tabs) : 0;
        handler->PSendSysMessage("WEBADMIN_GUILD\t{}\t{}\t{}\t{}\t{}\t{}",
            guild->GetId(), guild->GetName(), player->GetName(),
            guild->GetLeaderGUID() == player->GetGUID() ? 1 : 0, tabs, nextPrice);
        return true;
    }

    static bool HandleGuildUnlockTabCommand(ChatHandler* handler, char const* args)
    {
        Player* player = ParseOnlinePlayer(handler, args, "Player");
        if (!player || handler->HasLowerSecurity(player)) return false;
        Guild* guild = RequirePlayerGuild(handler, player);
        if (!guild) return false;
        if (guild->GetLeaderGUID() != player->GetGUID())
        {
            handler->SendErrorMessage("The selected character must be the guild master.");
            return false;
        }
        uint8 tabId = PurchasedGuildTabs(guild);
        if (tabId >= GUILD_BANK_MAX_TABS)
        {
            handler->SendErrorMessage("All guild bank tabs are already unlocked.");
            return false;
        }
        uint32 price = GuildTabPrice(tabId);
        uint32 originalMoney = player->GetMoney();
        if (!price || originalMoney > MAX_MONEY_AMOUNT - price)
        {
            handler->SendErrorMessage("The guild tab price cannot be safely covered.");
            return false;
        }
        player->SetMoney(originalMoney + price);
        guild->HandleBuyBankTab(player->GetSession(), tabId);
        player->SetMoney(originalMoney);
        handler->PSendSysMessage("Unlocked guild bank tab {} for {} without charging {}.",
            tabId + 1, guild->GetName(), player->GetName());
        return true;
    }

    struct WeaponTraining
    {
        char const* Key;
        char const* Name;
        uint32 SpellId;
        uint16 SkillId;
    };

    static auto const& WeaponTrainings()
    {
        static std::array<WeaponTraining, 15> const trainings = {{
            { "axes", "One-Handed Axes", 196, 44 }, { "two-axes", "Two-Handed Axes", 197, 172 },
            { "maces", "One-Handed Maces", 198, 54 }, { "two-maces", "Two-Handed Maces", 199, 160 },
            { "polearms", "Polearms", 200, 229 }, { "swords", "One-Handed Swords", 201, 43 },
            { "two-swords", "Two-Handed Swords", 202, 55 }, { "staves", "Staves", 227, 136 },
            { "bows", "Bows", 264, 45 }, { "guns", "Guns", 266, 46 }, { "daggers", "Daggers", 1180, 173 },
            { "thrown", "Thrown", 2567, 176 }, { "wands", "Wands", 5009, 228 },
            { "crossbows", "Crossbows", 5011, 226 }, { "fist", "Fist Weapons", 15590, 473 }
        }};
        return trainings;
    }

    static bool HandleWeaponInspectCommand(ChatHandler* handler, char const* args)
    {
        Player* player = ParseOnlinePlayer(handler, args, "Player");
        if (!player) return false;
        for (auto const& training : WeaponTrainings())
            handler->PSendSysMessage("WEBADMIN_WEAPON\t{}\t{}\t{}\t{}\t{}",
                training.Key, training.Name, player->HasSpell(training.SpellId) ? 1 : 0,
                player->GetPureSkillValue(training.SkillId), player->GetPureMaxSkillValue(training.SkillId));
        return true;
    }

    static bool HandleWeaponLearnCommand(ChatHandler* handler, char const* args)
    {
        std::istringstream input(args ? args : "");
        std::string playerName, key, unexpected;
        if (!(input >> playerName >> key) || (input >> unexpected))
        {
            handler->SendErrorMessage("Usage: webadmin weapon learn <onlinePlayer> <weaponKey>");
            return false;
        }
        Player* player = RequireOnlinePlayer(handler, playerName, "Player");
        if (!player || handler->HasLowerSecurity(player)) return false;
        auto const& trainings = WeaponTrainings();
        auto training = std::ranges::find_if(trainings, [&key](auto const& value) { return key == value.Key; });
        if (training == trainings.end())
        {
            handler->SendErrorMessage("Unknown weapon training type.");
            return false;
        }
        player->learnSpell(training->SpellId, false);
        uint16 maximum = std::max<uint16>(1, player->GetLevel() * 5);
        uint16 current = std::max<uint16>(1, player->GetPureSkillValue(training->SkillId));
        player->SetSkill(training->SkillId, 1, std::min(current, maximum), maximum);
        handler->PSendSysMessage("Granted {} training to {} ({}/{} skill).", training->Name,
            player->GetName(), player->GetPureSkillValue(training->SkillId), player->GetPureMaxSkillValue(training->SkillId));
        return true;
    }

    static bool HandleQuestReturnCommand(ChatHandler* handler, char const* args)
    {
        Player* player = ParseOnlinePlayer(handler, args, "Player");
        if (!player || handler->HasLowerSecurity(player)) return false;
        if (player->IsBeingTeleported())
        {
            handler->SendErrorMessage("The player is already being teleported.");
            return false;
        }
        if (player->IsInFlight())
        {
            player->GetMotionMaster()->MovementExpired();
            player->CleanupAfterTaxiFlight();
        }
        if (!player->TeleportTo(player->m_recallMap, player->m_recallX, player->m_recallY,
            player->m_recallZ, player->m_recallO, TELE_TO_GM_MODE))
        {
            handler->SendErrorMessage("AzerothCore rejected the return teleport.");
            return false;
        }
        handler->PSendSysMessage("Returned {} to the saved location.", player->GetName());
        return true;
    }

    static bool HandleNpcTeleportCommand(ChatHandler* handler, char const* args)
    {
        std::istringstream input(args ? args : "");
        std::string playerName, unexpected;
        ObjectGuid::LowType spawnId = 0;
        uint32 allowHostile = 0;
        if (!(input >> playerName >> spawnId >> allowHostile)
            || allowHostile > 1 || (input >> unexpected))
        {
            handler->SendErrorMessage(
                "Usage: webadmin npc teleport <onlinePlayer> <spawnId> <allowHostile:0|1>");
            return false;
        }
        Player* player = RequireOnlinePlayer(handler, playerName, "Player");
        if (!player || handler->HasLowerSecurity(player)) return false;
        if (sRandomPlayerbotMgr.IsRandomBot(player))
        {
            handler->SendErrorMessage("NPC teleports are limited to real players.");
            return false;
        }
        CreatureData const* spawn = sObjectMgr->GetCreatureData(spawnId);
        if (!spawn)
        {
            handler->SendErrorMessage("The NPC spawn does not exist.");
            return false;
        }
        CreatureTemplate const* creatureTemplate = sObjectMgr->GetCreatureTemplate(spawn->id);
        MapEntry const* map = sMapStore.LookupEntry(spawn->mapid);
        if (!creatureTemplate || !map || !map->IsWorldMap())
        {
            handler->SendErrorMessage("NPC teleports are limited to outdoor world maps.");
            return false;
        }
        FactionTemplateEntry const* npcFaction = sFactionTemplateStore.LookupEntry(creatureTemplate->faction);
        FactionTemplateEntry const* playerFaction = player->GetFactionTemplateEntry();
        if (npcFaction && playerFaction
            && (npcFaction->IsHostileTo(*playerFaction) || playerFaction->IsHostileTo(*npcFaction))
            && !allowHostile)
        {
            handler->SendErrorMessage(
                "That NPC is hostile to the selected player; explicit confirmation is required.");
            return false;
        }
        if (player->IsBeingTeleported())
        {
            handler->SendErrorMessage("The player is already being teleported.");
            return false;
        }
        if (player->IsInFlight())
        {
            player->GetMotionMaster()->MovementExpired();
            player->CleanupAfterTaxiFlight();
        }
        else
            player->SaveRecallPosition();
        if (!player->TeleportTo(spawn->mapid, spawn->posX, spawn->posY, spawn->posZ,
            spawn->orientation, TELE_TO_GM_MODE))
        {
            handler->SendErrorMessage("AzerothCore rejected the NPC teleport.");
            return false;
        }
        handler->PSendSysMessage("Teleported {} to {}.", player->GetName(), creatureTemplate->Name);
        return true;
    }

    static Player* ParseOnlinePlayer(ChatHandler* handler, char const* args, char const* label)
    {
        std::istringstream input(args ? args : "");
        std::string name, unexpected;
        if (!(input >> name) || (input >> unexpected)) return nullptr;
        return RequireOnlinePlayer(handler, name, label);
    }

    static bool HandleSpeedCommand(ChatHandler* handler, char const* args)
    {
        std::istringstream input(args ? args : "");
        std::string playerName, unexpected;
        float speed = 0.0f;
        if (!(input >> playerName >> speed) || (input >> unexpected) || speed < 0.5f || speed > 10.0f)
        {
            handler->SendErrorMessage("Usage: webadmin speed <onlinePlayer> <0.5-10>");
            return false;
        }
        Player* player = RequireOnlinePlayer(handler, playerName, "Player");
        if (!player || handler->HasLowerSecurity(player)) return false;
        player->SetSpeed(MOVE_WALK, speed, true);
        player->SetSpeed(MOVE_RUN, speed, true);
        player->SetSpeed(MOVE_SWIM, speed, true);
        player->SetSpeed(MOVE_FLIGHT, speed, true);
        handler->PSendSysMessage("Set {}'s movement speed to {}x.", player->GetName(), speed);
        return true;
    }

    static bool HandleCreatureSpawnCommand(ChatHandler* handler, char const* args)
    {
        std::istringstream input(args ? args : "");
        std::string anchorName, unexpected;
        uint32 entry = 0, level = 0, despawnMinutes = 0;
        if (!(input >> anchorName >> entry >> level >> despawnMinutes) || (input >> unexpected))
        {
            handler->SendErrorMessage("Usage: webadmin creature spawn <anchorPlayer> <creatureId> <level> <despawnMinutes>");
            return false;
        }

        Player* anchor = RequireOnlinePlayer(handler, anchorName, "Anchor");
        CreatureTemplate const* creatureTemplate = sObjectMgr->GetCreatureTemplate(entry);
        if (!anchor || !creatureTemplate)
        {
            if (!creatureTemplate) handler->SendErrorMessage("That creature template does not exist.");
            return false;
        }
        static std::array<uint32, 8> const utilityNpcEntries =
            { 3534, 4085, 5411, 12958, 12959, 14337, 19572, 22479 };
        bool const isUtilityNpc = std::ranges::find(utilityNpcEntries, entry) != utilityNpcEntries.end();
        if ((creatureTemplate->npcflag != 0 && !isUtilityNpc) || creatureTemplate->rank == 3)
        {
            handler->SendErrorMessage("Only allowlisted neutral service NPCs can be spawned by web administration.");
            return false;
        }
        if (level < 1 || level > 83 || despawnMinutes < 1 || despawnMinutes > 30)
        {
            handler->SendErrorMessage("Creature level must be 1-83 and despawn time must be 1-30 minutes.");
            return false;
        }
        Map* map = anchor->GetMap();
        if (!map || map->IsBattlegroundOrArena() || (!isUtilityNpc && map->IsDungeon()) || anchor->GetTransport()
            || anchor->IsInFlight() || anchor->IsInCombat() || anchor->IsBeingTeleported())
        {
            handler->SendErrorMessage("The anchor must be stationary, out of combat, outdoors, and outside instances and transports.");
            return false;
        }

        static std::vector<std::pair<ObjectGuid, ObjectGuid>> activeSpawns;
        std::erase_if(activeSpawns, [anchor](auto const& spawn)
        {
            return spawn.first == anchor->GetGUID() && !ObjectAccessor::GetCreature(*anchor, spawn.second);
        });
        if (std::ranges::count_if(activeSpawns, [anchor](auto const& spawn)
            { return spawn.first == anchor->GetGUID(); }) >= 3)
        {
            handler->SendErrorMessage("That player already has three active web-spawned creatures nearby.");
            return false;
        }

        Position position = anchor->GetFirstCollisionPosition(5.0f, 0.0f);
        TempSummon* creature = anchor->SummonCreature(entry, position, TEMPSUMMON_TIMED_OR_DEAD_DESPAWN,
            despawnMinutes * MINUTE * IN_MILLISECONDS);
        if (!creature)
        {
            handler->SendErrorMessage("AzerothCore could not create the temporary creature at that location.");
            return false;
        }
        activeSpawns.emplace_back(anchor->GetGUID(), creature->GetGUID());
        creature->SetLevel(level);
        CreatureBaseStats const* stats = sObjectMgr->GetCreatureBaseStats(level, creatureTemplate->unit_class);
        uint32 health = std::max<uint32>(1, stats->BaseHealth[creatureTemplate->expansion]);
        uint32 mana = stats->BaseMana;
        float baseDamage = std::max(1.0f, stats->BaseDamage[creatureTemplate->expansion]);
        creature->SetCreateHealth(health);
        creature->SetStatFlatModifier(UNIT_MOD_HEALTH, BASE_VALUE, float(health));
        creature->SetCreateMana(mana);
        creature->SetStatFlatModifier(UNIT_MOD_MANA, BASE_VALUE, float(mana));
        creature->SetBaseWeaponDamage(BASE_ATTACK, MINDAMAGE, baseDamage);
        creature->SetBaseWeaponDamage(BASE_ATTACK, MAXDAMAGE, baseDamage * 1.5f);
        creature->SetBaseWeaponDamage(OFF_ATTACK, MINDAMAGE, baseDamage);
        creature->SetBaseWeaponDamage(OFF_ATTACK, MAXDAMAGE, baseDamage * 1.5f);
        creature->SetBaseWeaponDamage(RANGED_ATTACK, MINDAMAGE, baseDamage);
        creature->SetBaseWeaponDamage(RANGED_ATTACK, MAXDAMAGE, baseDamage * 1.5f);
        creature->SetStatFlatModifier(UNIT_MOD_ATTACK_POWER, BASE_VALUE, stats->AttackPower);
        creature->SetStatFlatModifier(UNIT_MOD_ATTACK_POWER_RANGED, BASE_VALUE, stats->RangedAttackPower);
        creature->UpdateAllStats();
        creature->SetMaxHealth(health);
        creature->SetHealth(health);
        handler->PSendSysMessage("Spawned {} (entry {}, level {}) beside {} for up to {} minutes. Tameable: {}. Exotic: {}.",
            creatureTemplate->Name, entry, level, anchor->GetName(), despawnMinutes,
            creatureTemplate->IsTameable(true) ? "Yes" : "No", creatureTemplate->IsExotic() ? "Yes" : "No");
        return true;
    }

    static Player* RequireOnlinePlayer(ChatHandler* handler, std::string name, char const* label)
    {
        if (!normalizePlayerName(name))
        {
            handler->SendErrorMessage("{} character name is invalid.", label);
            return nullptr;
        }
        Player* player = ObjectAccessor::FindPlayerByName(name);
        if (!player)
            handler->SendErrorMessage("{} character must be online.", label);
        return player;
    }

    static bool IsSpecialGroup(Group const* group)
    {
        return group && (group->isRaidGroup() || group->isLFGGroup() || group->isBGGroup() || group->isBFGroup());
    }

    static std::string Role(Player* player)
    {
        if (PlayerbotAI::IsTank(player, true)) return "Tank";
        if (PlayerbotAI::IsHeal(player, true)) return "Healer";
        return PlayerbotAI::IsRanged(player, true) ? "RangedDps" : "MeleeDps";
    }

    static bool IsEligibleBot(Player* leader, Player* bot)
    {
        return bot && bot != leader && sRandomPlayerbotMgr.IsRandomBot(bot) && !bot->GetGroup()
            && bot->GetTeamId() == leader->GetTeamId() && !bot->InBattleground()
            && !bot->InBattlegroundQueue() && std::abs(int(bot->GetLevel()) - int(leader->GetLevel())) <= 5;
    }

    static std::vector<Player*> EligibleBots(Player* leader)
    {
        std::vector<Player*> bots;
        for (auto const& [guid, player] : ObjectAccessor::GetPlayers())
            if (IsEligibleBot(leader, player)) bots.push_back(player);
        std::ranges::sort(bots, [leader](Player* left, Player* right)
        {
            int leftDifference = std::abs(int(left->GetLevel()) - int(leader->GetLevel()));
            int rightDifference = std::abs(int(right->GetLevel()) - int(leader->GetLevel()));
            return leftDifference != rightDifference ? leftDifference < rightDifference : left->GetName() < right->GetName();
        });
        return bots;
    }

    static bool MoveBeside(ChatHandler* handler, Player* movingPlayer, Player* anchorPlayer)
    {
        Map* destinationMap = anchorPlayer->GetMap();
        if (!destinationMap || destinationMap->IsBattlegroundOrArena() || anchorPlayer->GetTransport()) return false;
        if (destinationMap->IsDungeon() && (movingPlayer->GetMapId() != anchorPlayer->GetMapId()
            || movingPlayer->GetInstanceId() != anchorPlayer->GetInstanceId())) return false;
        if (movingPlayer->IsInFlight())
        {
            movingPlayer->GetMotionMaster()->MovementExpired();
            movingPlayer->CleanupAfterTaxiFlight();
        }
        else movingPlayer->SaveRecallPosition();
        float x, y, z;
        anchorPlayer->GetClosePoint(x, y, z, movingPlayer->GetObjectSize());
        if (!movingPlayer->TeleportTo(anchorPlayer->GetMapId(), x, y, z, movingPlayer->GetOrientation(),
            TELE_TO_GM_MODE, anchorPlayer)) return false;
        movingPlayer->SetPhaseMask(anchorPlayer->GetPhaseMask() | 1, false);
        return true;
    }

    static Group* EnsureParty(Player* leader)
    {
        if (Group* group = leader->GetGroup()) return group;
        Group* group = new Group;
        if (!group->Create(leader)) { delete group; return nullptr; }
        sGroupMgr->AddGroup(group);
        return group;
    }

    static bool AddBot(ChatHandler* handler, Player* leader, Player* bot)
    {
        Group* group = leader->GetGroup();
        if (IsSpecialGroup(group) || (group && group->GetMembersCount() >= 5) || !IsEligibleBot(leader, bot)) return false;
        group = EnsureParty(leader);
        if (!group || !group->AddMember(bot)) return false;
        MoveBeside(handler, bot, leader);
        return true;
    }

    static bool ParseLeaderAndBot(ChatHandler* handler, char const* args, Player*& leader, Player*& bot)
    {
        std::istringstream input(args ? args : "");
        std::string leaderName, botName, unexpected;
        if (!(input >> leaderName >> botName) || (input >> unexpected)) return false;
        leader = RequireOnlinePlayer(handler, leaderName, "Leader");
        bot = RequireOnlinePlayer(handler, botName, "Bot");
        return leader && bot;
    }

    static Player* ParseLeader(ChatHandler* handler, char const* args)
    {
        std::istringstream input(args ? args : "");
        std::string leaderName, unexpected;
        if (!(input >> leaderName) || (input >> unexpected)) return nullptr;
        Player* leader = RequireOnlinePlayer(handler, leaderName, "Leader");
        if (leader && sRandomPlayerbotMgr.IsRandomBot(leader))
        {
            handler->SendErrorMessage("The party leader must be a real player.");
            return nullptr;
        }
        return leader;
    }

    static bool HandleGroupInspectCommand(ChatHandler* handler, char const* args)
    {
        Player* leader = ParseLeader(handler, args);
        if (!leader) return false;
        Group* group = leader->GetGroup();
        handler->PSendSysMessage("WEBADMIN_PARTY\t{}\t{}", leader->GetName(), group ? group->GetMembersCount() : 1);
        bool leaderEmitted = false;
        if (group)
            for (GroupReference* reference = group->GetFirstMember(); reference; reference = reference->next())
                if (Player* member = reference->GetSource())
                {
                    handler->PSendSysMessage("WEBADMIN_MEMBER\t{}\t{}\t{}\t{}", member->GetName(), member->GetLevel(),
                        Role(member), sRandomPlayerbotMgr.IsRandomBot(member) ? 1 : 0);
                    leaderEmitted = leaderEmitted || member == leader;
                }
        if (!leaderEmitted)
            handler->PSendSysMessage("WEBADMIN_MEMBER\t{}\t{}\t{}\t0", leader->GetName(), leader->GetLevel(), Role(leader));
        for (Player* bot : EligibleBots(leader))
            handler->PSendSysMessage("WEBADMIN_CANDIDATE\t{}\t{}\t{}\t{}", bot->GetName(), bot->GetLevel(), Role(bot), bot->getClass());
        return true;
    }

    static bool HandleGroupAddCommand(ChatHandler* handler, char const* args)
    {
        Player* leader = nullptr; Player* bot = nullptr;
        if (!ParseLeaderAndBot(handler, args, leader, bot) || sRandomPlayerbotMgr.IsRandomBot(leader)) return false;
        if (!AddBot(handler, leader, bot)) { handler->SendErrorMessage("The bot is not eligible or the party is full."); return false; }
        handler->PSendSysMessage("Added {} to {}'s party.", bot->GetName(), leader->GetName());
        return true;
    }

    static bool HandleGroupRemoveCommand(ChatHandler* handler, char const* args)
    {
        Player* leader = nullptr; Player* bot = nullptr;
        if (!ParseLeaderAndBot(handler, args, leader, bot) || !sRandomPlayerbotMgr.IsRandomBot(bot)) return false;
        Group* group = leader->GetGroup();
        if (!group || bot->GetGroup() != group) { handler->SendErrorMessage("That bot is not in the leader's party."); return false; }
        group->RemoveMember(bot->GetGUID());
        handler->PSendSysMessage("Removed {} from {}'s party.", bot->GetName(), leader->GetName());
        return true;
    }

    static bool HandleGroupClearCommand(ChatHandler* handler, char const* args)
    {
        Player* leader = ParseLeader(handler, args);
        if (!leader) return false;
        Group* group = leader->GetGroup();
        if (!group || IsSpecialGroup(group)) return false;
        std::vector<ObjectGuid> bots;
        for (GroupReference* reference = group->GetFirstMember(); reference; reference = reference->next())
            if (Player* member = reference->GetSource(); member && sRandomPlayerbotMgr.IsRandomBot(member)) bots.push_back(member->GetGUID());
        for (ObjectGuid guid : bots) group->RemoveMember(guid);
        handler->PSendSysMessage("Removed {} bots from {}'s party.", bots.size(), leader->GetName());
        return true;
    }

    static bool HandleGroupFillCommand(ChatHandler* handler, char const* args)
    {
        Player* leader = ParseLeader(handler, args);
        if (!leader || IsSpecialGroup(leader->GetGroup())) return false;
        unsigned tanks = 0, healers = 0;
        auto countRole = [&](Player* player) { if (PlayerbotAI::IsTank(player, true)) ++tanks; else if (PlayerbotAI::IsHeal(player, true)) ++healers; };
        if (Group* group = leader->GetGroup())
            for (GroupReference* reference = group->GetFirstMember(); reference; reference = reference->next())
                if (Player* member = reference->GetSource()) countRole(member);
        else countRole(leader);
        auto candidates = EligibleBots(leader);
        unsigned added = 0;
        while ((!leader->GetGroup() || leader->GetGroup()->GetMembersCount() < 5) && !candidates.empty())
        {
            auto match = candidates.begin();
            if (!tanks) match = std::find_if(candidates.begin(), candidates.end(), [](Player* p) { return PlayerbotAI::IsTank(p, true); });
            else if (!healers) match = std::find_if(candidates.begin(), candidates.end(), [](Player* p) { return PlayerbotAI::IsHeal(p, true); });
            else match = std::find_if(candidates.begin(), candidates.end(), [](Player* p) { return !PlayerbotAI::IsTank(p, true) && !PlayerbotAI::IsHeal(p, true); });
            if (match == candidates.end()) match = candidates.begin();
            Player* bot = *match; candidates.erase(match);
            if (AddBot(handler, leader, bot)) { countRole(bot); ++added; }
        }
        handler->PSendSysMessage("Added {} bots to {}'s party.", added, leader->GetName());
        return true;
    }

    static bool HandleDungeonListCommand(ChatHandler* handler, char const* /*args*/)
    {
        for (uint32 index = 0; index < sLFGDungeonStore.GetNumRows(); ++index)
        {
            LFGDungeonEntry const* entry = sLFGDungeonStore.LookupEntry(index);
            if (!entry || (entry->TypeID != lfg::LFG_TYPE_DUNGEON && entry->TypeID != lfg::LFG_TYPE_HEROIC)) continue;
            lfg::LFGDungeonData const* dungeon = sLFGMgr->GetLFGDungeon(entry->ID);
            if (!dungeon || (dungeon->x == 0.0f && dungeon->y == 0.0f && dungeon->z == 0.0f)) continue;
            handler->PSendSysMessage("WEBADMIN_DUNGEON\t{}\t{}\t{}\t{}\t{}\t{}", entry->ID,
                entry->Name[0], entry->MinLevel, entry->MaxLevel, entry->MapID,
                entry->TypeID == lfg::LFG_TYPE_HEROIC ? "Heroic" : "Normal");
        }
        return true;
    }

    static bool HandleGroupLaunchCommand(ChatHandler* handler, char const* args)
    {
        std::istringstream input(args ? args : "");
        std::string leaderName, unexpected;
        uint32 dungeonId = 0;
        if (!(input >> leaderName >> dungeonId) || (input >> unexpected))
        {
            handler->SendErrorMessage("Usage: webadmin group launch <leader> <dungeonId>");
            return false;
        }
        Player* leader = RequireOnlinePlayer(handler, leaderName, "Leader");
        if (!leader || sRandomPlayerbotMgr.IsRandomBot(leader)) return false;
        Group* group = leader->GetGroup();
        if (IsSpecialGroup(group))
        {
            handler->SendErrorMessage("Raid, LFG, battleground, and battlefield groups cannot be launched.");
            return false;
        }
        lfg::LFGDungeonData const* dungeon = sLFGMgr->GetLFGDungeon(dungeonId);
        if (!dungeon || (dungeon->type != lfg::LFG_TYPE_DUNGEON && dungeon->type != lfg::LFG_TYPE_HEROIC)
            || (dungeon->x == 0.0f && dungeon->y == 0.0f && dungeon->z == 0.0f))
        {
            handler->SendErrorMessage("The dungeon does not have a usable teleport destination.");
            return false;
        }

        std::vector<Player*> members;
        if (group)
        {
            for (GroupReference* reference = group->GetFirstMember(); reference; reference = reference->next())
            {
                Player* member = reference->GetSource();
                if (!member || member->IsBeingTeleported() || member->IsInCombat() || member->IsInFlight()
                    || member->InBattleground() || member->GetTransport())
                {
                    handler->SendErrorMessage("Every party member must be online, stationary, out of combat, and off transports.");
                    return false;
                }
                members.push_back(member);
            }
        }
        else members.push_back(leader);

        for (Player* member : members)
        {
            member->SaveRecallPosition();
            if (!member->TeleportTo(dungeon->map, dungeon->x, dungeon->y, dungeon->z, dungeon->o, TELE_TO_GM_MODE))
            {
                handler->SendErrorMessage("AzerothCore rejected the group teleport.");
                return false;
            }
        }
        handler->PSendSysMessage("Launched {} party members into {}.", members.size(), dungeon->name);
        return true;
    }

    static bool HandleMoveCommand(ChatHandler* handler, char const* args)
    {
        std::istringstream input(args ? args : "");
        std::string movingName;
        std::string anchorName;
        std::string unexpected;
        if (!(input >> movingName >> anchorName) || (input >> unexpected))
        {
            handler->SendErrorMessage("Usage: webadmin move <movingPlayer> <anchorPlayer>");
            return false;
        }

        if (!normalizePlayerName(movingName) || !normalizePlayerName(anchorName))
        {
            handler->SendErrorMessage("Both character names must be valid.");
            return false;
        }

        Player* movingPlayer = ObjectAccessor::FindPlayerByName(movingName);
        Player* anchorPlayer = ObjectAccessor::FindPlayerByName(anchorName);
        if (!movingPlayer || !anchorPlayer)
        {
            handler->SendErrorMessage("Both characters must be online.");
            return false;
        }
        if (movingPlayer == anchorPlayer)
        {
            handler->SendErrorMessage("The moving player and anchor player must be different.");
            return false;
        }
        if (handler->HasLowerSecurity(movingPlayer) || handler->HasLowerSecurity(anchorPlayer))
            return false;
        if (movingPlayer->IsBeingTeleported())
        {
            handler->SendErrorMessage("The moving character is already being teleported.");
            return false;
        }

        Map* destinationMap = anchorPlayer->GetMap();
        if (!destinationMap || destinationMap->IsBattlegroundOrArena())
        {
            handler->SendErrorMessage("Web administration cannot move characters into a battleground or arena.");
            return false;
        }
        if (destinationMap->IsDungeon()
            && (movingPlayer->GetMapId() != anchorPlayer->GetMapId()
                || movingPlayer->GetInstanceId() != anchorPlayer->GetInstanceId()))
        {
            handler->SendErrorMessage("Cross-instance movement is not allowed by this command.");
            return false;
        }
        if (anchorPlayer->GetTransport())
        {
            handler->SendErrorMessage("Movement to a character on a transport is not supported.");
            return false;
        }

        if (movingPlayer->IsInFlight())
        {
            movingPlayer->GetMotionMaster()->MovementExpired();
            movingPlayer->CleanupAfterTaxiFlight();
        }
        else
            movingPlayer->SaveRecallPosition();

        float x;
        float y;
        float z;
        anchorPlayer->GetClosePoint(x, y, z, movingPlayer->GetObjectSize());
        if (!movingPlayer->TeleportTo(anchorPlayer->GetMapId(), x, y, z,
            movingPlayer->GetOrientation(), TELE_TO_GM_MODE, anchorPlayer))
        {
            handler->SendErrorMessage("AzerothCore rejected the teleport.");
            return false;
        }

        movingPlayer->SetPhaseMask(anchorPlayer->GetPhaseMask() | 1, false);
        handler->PSendSysMessage("Moved {} to {}.", movingPlayer->GetName(), anchorPlayer->GetName());
        return true;
    }
};
}

void Addmod_web_adminScripts()
{
    new WebAdminCommandScript();
}
