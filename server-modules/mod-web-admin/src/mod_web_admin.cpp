/*
 * AzerothCore-UI web administration commands.
 * GPL-2.0-or-later, matching AzerothCore.
 */

#include "AllCreatureScript.h"
#include "AllGameObjectScript.h"
#include "AiObjectContext.h"
#include "Bag.h"
#include "Chat.h"
#include "CharacterCache.h"
#include "ChatHelper.h"
#include "Creature.h"
#include "DatabaseEnv.h"
#include "GameObject.h"
#include "Group.h"
#include "GroupMgr.h"
#include "Guild.h"
#include "GuildMgr.h"
#include "DBCStores.h"
#include "Item.h"
#include "ItemTemplate.h"
#include "ItemUsageValue.h"
#include "LFGMgr.h"
#include "LootObjectStack.h"
#include "Map.h"
#include "Mail.h"
#include "MotionMaster.h"
#include "ObjectAccessor.h"
#include "ObjectMgr.h"
#include "Pet.h"
#include "Player.h"
#include "PlayerScript.h"
#include "PlayerbotAI.h"
#include "PlayerbotMgr.h"
#include "Playerbots.h"
#include "QuestDef.h"
#include "Random.h"
#include "RandomItemMgr.h"
#include "RandomPlayerbotMgr.h"
#include "ScriptMgr.h"
#include "SpellInfo.h"
#include "SpellMgr.h"
#include "TemporarySummon.h"
#include "Trainer.h"
#include "World.h"
#include "WorldSession.h"

#include <sstream>
#include <string>
#include <vector>
#include <algorithm>
#include <array>
#include <ctime>
#include <cstdlib>
#include <map>

using namespace Acore::ChatCommands;

namespace
{
static constexpr uint32 CompanionProtocolVersion = 7;
static constexpr float CompanionGatherRadius = 40.0f;
static constexpr uint8 CompanionVendorSellTypesPerPass = 6;
static constexpr uint32 SummonImpSpell = 688;
static constexpr uint32 SummonVoidwalkerSpell = 697;
static constexpr uint32 ImpEntry = 416;
static constexpr uint32 VoidwalkerEntry = 1860;
static constexpr uint32 DualDemonSummonProperties = 61;

struct WarlockDualDemonState
{
    ObjectGuid GuardianGuid;
    uint32 GuardianEntry = 0;
    uint32 UpdateTimer = 0;
    uint32 AbilityTimer = 0;
    uint32 RespawnTimer = 0;
};

static std::map<ObjectGuid, WarlockDualDemonState> WarlockDualDemons;

static uint32 ImpFireboltForLevel(uint8 level)
{
    if (level >= 69) return 47964;
    if (level >= 56) return 27267;
    if (level >= 48) return 11763;
    if (level >= 40) return 11762;
    if (level >= 32) return 7802;
    if (level >= 24) return 7801;
    if (level >= 16) return 7800;
    if (level >= 8) return 7799;
    return 3110;
}

static uint32 VoidwalkerTormentForLevel(uint8 level)
{
    if (level >= 70) return 47984;
    if (level >= 60) return 27270;
    if (level >= 50) return 11775;
    if (level >= 40) return 11774;
    if (level >= 30) return 7811;
    if (level >= 20) return 7810;
    if (level >= 10) return 7809;
    return 3716;
}

static void DespawnWarlockDualDemon(
    Player* player, WarlockDualDemonState& state)
{
    if (player && !state.GuardianGuid.IsEmpty())
    {
        if (Creature* guardian =
                ObjectAccessor::GetCreature(*player, state.GuardianGuid))
            guardian->DespawnOrUnsummon();
    }
    state.GuardianGuid.Clear();
    state.GuardianEntry = 0;
    state.AbilityTimer = 0;
}

static Creature* SummonWarlockDualDemon(Player* player, uint32 entry)
{
    SummonPropertiesEntry const* properties =
        sSummonPropertiesStore.LookupEntry(DualDemonSummonProperties);
    if (!player || !properties)
        return nullptr;

    float x;
    float y;
    float z;
    player->GetClosePoint(x, y, z, player->GetObjectSize(), 2.0f);
    Position position;
    position.Relocate(x, y, z, player->GetOrientation());
    TempSummon* guardian = player->GetMap()->SummonCreature(
        entry, position, properties, 0, player);
    if (!guardian)
        return nullptr;

    guardian->SetReactState(REACT_DEFENSIVE);
    guardian->GetMotionMaster()->MoveFollow(
        player, PET_FOLLOW_DIST, PET_FOLLOW_ANGLE, MOTION_SLOT_ACTIVE);
    return guardian;
}

static Unit* SelectWarlockDualDemonTarget(
    Player* player, Pet* primary, Creature* guardian)
{
    Unit* target = primary ? primary->GetVictim() : nullptr;
    if ((!target || !target->IsAlive()) && player)
        target = player->GetVictim();
    if ((!target || !target->IsAlive()) && player
        && !player->getAttackers().empty())
        target = *player->getAttackers().begin();
    return target && guardian->IsValidAttackTarget(target) ? target : nullptr;
}

static void UpdateWarlockDualDemon(Player* player, uint32 diff)
{
    if (!player)
        return;

    WarlockDualDemonState& state = WarlockDualDemons[player->GetGUID()];
    state.RespawnTimer = state.RespawnTimer > diff
        ? state.RespawnTimer - diff : 0;
    state.AbilityTimer = state.AbilityTimer > diff
        ? state.AbilityTimer - diff : 0;
    if (state.UpdateTimer > diff)
    {
        state.UpdateTimer -= diff;
        return;
    }
    state.UpdateTimer = 500;

    Pet* primary = player->GetPet();
    bool const eligible = player->getClass() == CLASS_WARLOCK
        && player->HasSpell(SummonImpSpell)
        && player->HasSpell(SummonVoidwalkerSpell)
        && player->IsAlive() && primary && primary->IsAlive();
    uint32 expectedEntry = 0;
    if (eligible && primary->GetEntry() == ImpEntry)
        expectedEntry = VoidwalkerEntry;
    else if (eligible && primary->GetEntry() == VoidwalkerEntry)
        expectedEntry = ImpEntry;

    if (!expectedEntry)
    {
        DespawnWarlockDualDemon(player, state);
        return;
    }

    Creature* guardian = state.GuardianGuid.IsEmpty() ? nullptr
        : ObjectAccessor::GetCreature(*player, state.GuardianGuid);
    if (state.GuardianEntry != expectedEntry)
    {
        DespawnWarlockDualDemon(player, state);
        state.RespawnTimer = 0;
        guardian = nullptr;
    }
    else if (guardian && !guardian->IsAlive())
    {
        DespawnWarlockDualDemon(player, state);
        state.RespawnTimer = 30000;
        guardian = nullptr;
    }
    else if (!guardian && !state.GuardianGuid.IsEmpty())
    {
        state.GuardianGuid.Clear();
        state.GuardianEntry = 0;
    }

    if (!guardian)
    {
        if (player->IsInCombat() || state.RespawnTimer)
            return;
        guardian = SummonWarlockDualDemon(player, expectedEntry);
        if (!guardian)
            return;
        state.GuardianGuid = guardian->GetGUID();
        state.GuardianEntry = expectedEntry;
        state.AbilityTimer = 0;
    }

    if (guardian->GetDistance(player) > 60.0f)
    {
        float x;
        float y;
        float z;
        player->GetClosePoint(x, y, z, guardian->GetObjectSize(), 2.0f);
        guardian->NearTeleportTo(x, y, z, player->GetOrientation());
    }

    Unit* target = SelectWarlockDualDemonTarget(player, primary, guardian);
    if (!target)
    {
        if (guardian->GetVictim())
        {
            guardian->AttackStop();
            guardian->CombatStop(true);
        }
        if (guardian->GetMotionMaster()->GetCurrentMovementGeneratorType()
            != FOLLOW_MOTION_TYPE)
        {
            guardian->GetMotionMaster()->MoveFollow(
                player, PET_FOLLOW_DIST, PET_FOLLOW_ANGLE,
                MOTION_SLOT_ACTIVE);
        }
        return;
    }

    if (expectedEntry == ImpEntry)
    {
        if (guardian->GetVictim() != target)
            guardian->Attack(target, false);
        if (guardian->GetMotionMaster()->GetCurrentMovementGeneratorType()
            != CHASE_MOTION_TYPE)
            guardian->GetMotionMaster()->MoveChase(target, 20.0f);
        if (!state.AbilityTimer && guardian->IsWithinDistInMap(target, 30.0f))
        {
            guardian->CastSpell(
                target, ImpFireboltForLevel(player->GetLevel()), false);
            state.AbilityTimer = 2500;
        }
    }
    else
    {
        if (guardian->GetVictim() != target && guardian->IsAIEnabled)
            guardian->AI()->AttackStart(target);
        if (!state.AbilityTimer && guardian->IsWithinDistInMap(target, 5.0f))
        {
            guardian->CastSpell(
                target, VoidwalkerTormentForLevel(player->GetLevel()), false);
            state.AbilityTimer = 5000;
        }
    }
}

struct CompanionLogisticsRoute
{
    std::string Category;
    ObjectGuid RecipientGuid;
    std::string RecipientName;
    uint32 KeepQuantity = 0;
};

struct CompanionEquipmentChange
{
    std::time_t ChangedAt = 0;
    std::string Description;
};

struct QuestingCompanionRegistration
{
    ObjectGuid LeaderGuid;
    ObjectGuid CompanionGuid;
    bool InitialSyncPending = true;
    bool LeaderWasDead = false;
    uint32 QuestObjectCheckTimer = 0;
    uint32 SpecialQuestCheckTimer = 0;
    uint32 BagEquipCheckTimer = 0;
    uint32 TrashSellCheckTimer = 0;
    uint32 ProfessionTrainingCheckTimer = 0;
    uint32 LogisticsCheckTimer = 0;
    std::string QuestObjectStatus = "Waiting for quest-object scan.";
    std::string BehaviorPreset = "questing";
    std::string Role = "auto";
    std::string Movement = "follow";
    std::string CombatFocus = "assist";
    float FollowDistance = 3.0f;
    bool LootEnabled = true;
    bool GatherEnabled = true;
    bool AutoSellTrash = true;
    bool AutoRepair = true;
    uint32 LogisticsTriggerFreeSlots = 4;
    uint32 LogisticsTargetFreeSlots = 8;
    bool AutomaticLogistics = false;
    std::vector<CompanionLogisticsRoute> LogisticsRoutes;
    std::string LogisticsStatus = "Logistics is not configured.";
    std::string MaintenanceStatus = "No maintenance action yet.";
    ObjectGuid PrioritizedLeaderTarget;
    bool EquipmentSnapshotInitialized = false;
    std::array<uint32, EQUIPMENT_SLOT_END> EquipmentEntries{};
    std::array<std::string, EQUIPMENT_SLOT_END> EquipmentNames{};
    std::array<ObjectGuid, EQUIPMENT_SLOT_END> ProtectedEquipment{};
    std::vector<CompanionEquipmentChange> EquipmentChanges;
    bool InventorySnapshotInitialized = false;
    std::map<uint32, uint32> InventoryCounts;
    std::vector<CompanionEquipmentChange> InventoryChanges;
};

static void ConfigureAutomaticCompanionLogistics(
    QuestingCompanionRegistration& registration, Player* leader);
static bool HasCompanionLogisticsRouteForItem(
    Player* companion, Item* item, std::string const& category);

static std::vector<QuestingCompanionRegistration> QuestingCompanions;

static void RegisterQuestingCompanion(ObjectGuid leaderGuid, ObjectGuid companionGuid)
{
    for (QuestingCompanionRegistration& registration : QuestingCompanions)
    {
        if (registration.CompanionGuid == companionGuid)
        {
            if (registration.LeaderGuid == leaderGuid)
                return;

            registration = QuestingCompanionRegistration{
                leaderGuid, companionGuid, true };
            ConfigureAutomaticCompanionLogistics(
                registration, ObjectAccessor::FindConnectedPlayer(leaderGuid));
            return;
        }
    }

    QuestingCompanions.push_back({ leaderGuid, companionGuid, true });
    ConfigureAutomaticCompanionLogistics(
        QuestingCompanions.back(),
        ObjectAccessor::FindConnectedPlayer(leaderGuid));
}

static void UnregisterQuestingCompanion(ObjectGuid companionGuid)
{
    QuestingCompanions.erase(
        std::remove_if(
            QuestingCompanions.begin(), QuestingCompanions.end(),
            [companionGuid](QuestingCompanionRegistration const& registration)
            {
                return registration.CompanionGuid == companionGuid;
            }),
        QuestingCompanions.end());
}

static bool IsActiveQuestingCompanion(Player* leader, Player* companion)
{
    if (!leader || !companion || !leader->GetGroup()
        || companion->GetGroup() != leader->GetGroup())
        return false;

    PlayerbotAI* companionAI =
        PlayerbotsMgr::instance().GetPlayerbotAI(companion);
    return companionAI && companionAI->GetMaster() == leader;
}

static std::string CompanionProtocolText(std::string text)
{
    std::replace_if(
        text.begin(), text.end(),
        [](char character)
        {
            return character == '\t' || character == '\r' || character == '\n';
        },
        ' ');
    return text;
}

static bool HasCompanionLootStrategy(PlayerbotAI* companionAI)
{
    if (!companionAI)
        return false;

    std::vector<std::string> strategies =
        companionAI->GetStrategies(BOT_STATE_NON_COMBAT);
    return std::find(strategies.begin(), strategies.end(), "loot")
        != strategies.end();
}

static std::string ResolveCompanionRole(
    QuestingCompanionRegistration const& registration, Player* companion)
{
    if (registration.Role != "auto")
        return registration.Role;
    if (PlayerbotAI::IsTank(companion, true))
        return "tank";
    if (PlayerbotAI::IsHeal(companion, true))
        return "healer";
    return "damage";
}

static void ConfigureCompanionActivity(
    QuestingCompanionRegistration& registration, Player* companion)
{
    if (PlayerbotAI* companionAI =
            PlayerbotsMgr::instance().GetPlayerbotAI(companion))
    {
        if (registration.Movement == "stay")
        {
            companionAI->DoSpecificAction(
                "stay", Event("webadmin companion behaviour"), true);
        }
        else
        {
            companionAI->DoSpecificAction(
                "follow", Event("webadmin companion behaviour"), true);
        }

        companionAI->GetAiObjectContext()
            ->GetValue<float>("range", "follow")
            ->Set(registration.FollowDistance);
        companionAI->ChangeStrategy(
            (registration.LootEnabled ? "+loot" : "-loot"),
            BOT_STATE_NON_COMBAT);
        companionAI->ChangeStrategy(
            (registration.GatherEnabled ? "+gather" : "-gather"),
            BOT_STATE_NON_COMBAT);
        std::string const role = ResolveCompanionRole(registration, companion);
        companionAI->ChangeStrategy(
            role == "tank"
                ? "+tank assist,-dps assist"
                : "+dps assist,-tank assist",
            BOT_STATE_COMBAT);
        registration.PrioritizedLeaderTarget.Clear();
    }
}

static void UpdateCompanionCombatFocus(
    QuestingCompanionRegistration& registration, Player* leader,
    Player* companion)
{
    PlayerbotAI* companionAI =
        PlayerbotsMgr::instance().GetPlayerbotAI(companion);
    if (!leader || !companionAI)
        return;

    ObjectGuid desiredTarget = registration.CombatFocus == "assist"
        ? leader->GetTarget() : ObjectGuid::Empty;
    if (desiredTarget == registration.PrioritizedLeaderTarget)
        return;

    auto* prioritizedTargets = companionAI->GetAiObjectContext()
        ->GetValue<GuidVector>("prioritized targets");
    if (!desiredTarget.IsEmpty())
        prioritizedTargets->Set(GuidVector{ desiredTarget });
    else
        prioritizedTargets->Reset();
    registration.PrioritizedLeaderTarget = desiredTarget;
}

static bool IsCompanionProfessionTool(ItemTemplate const* itemTemplate)
{
    if (!itemTemplate)
        return false;

    // Profession tools such as mining picks, skinning knives, blacksmith
    // hammers, enchanting rods and engineering spanners use a totem category.
    // Fishing poles are the exception and are identified by weapon subclass.
    return itemTemplate->TotemCategory != 0
        || (itemTemplate->Class == ITEM_CLASS_WEAPON
            && itemTemplate->SubClass == ITEM_SUBCLASS_WEAPON_FISHING_POLE);
}

static bool IsPermanentlyUnusableEquipment(
    Player* companion, Item* item)
{
    if (!companion || !item || !item->GetTemplate())
        return false;

    ItemTemplate const* itemTemplate = item->GetTemplate();
    if (IsCompanionProfessionTool(itemTemplate)
        || (itemTemplate->Class != ITEM_CLASS_ARMOR
            && itemTemplate->Class != ITEM_CLASS_WEAPON)
        || companion->HasQuestForItem(item->GetEntry()))
        return false;

    InventoryResult usability = companion->BotCanUseItem(itemTemplate);
    if (usability == EQUIP_ERR_YOU_CAN_NEVER_USE_THAT_ITEM
        || usability == EQUIP_ERR_YOU_CAN_NEVER_USE_THAT_ITEM2)
        return true;

    if (itemTemplate->Class == ITEM_CLASS_WEAPON)
        return !sRandomItemMgr.CanEquipWeapon(
            itemTemplate, companion->getClass());

    if (itemTemplate->SubClass >= ITEM_SUBCLASS_ARMOR_CLOTH
        && itemTemplate->SubClass <= ITEM_SUBCLASS_ARMOR_PLATE)
    {
        uint32 maximumArmorSubclass = ITEM_SUBCLASS_ARMOR_CLOTH;
        switch (companion->getClass())
        {
            case CLASS_DRUID:
            case CLASS_ROGUE:
                maximumArmorSubclass = ITEM_SUBCLASS_ARMOR_LEATHER;
                break;
            case CLASS_HUNTER:
            case CLASS_SHAMAN:
                maximumArmorSubclass = ITEM_SUBCLASS_ARMOR_MAIL;
                break;
            case CLASS_DEATH_KNIGHT:
            case CLASS_PALADIN:
            case CLASS_WARRIOR:
                maximumArmorSubclass = ITEM_SUBCLASS_ARMOR_PLATE;
                break;
            default:
                break;
        }
        return itemTemplate->SubClass > maximumArmorSubclass;
    }

    if (itemTemplate->SubClass == ITEM_SUBCLASS_ARMOR_SHIELD)
    {
        switch (companion->getClass())
        {
            case CLASS_PALADIN:
            case CLASS_SHAMAN:
            case CLASS_WARRIOR:
                return false;
            default:
                return true;
        }
    }
    return false;
}

static ItemUsage GetFreshCompanionItemUsage(
    Player* companion, Item* item, std::string const& valueName)
{
    PlayerbotAI* companionAI = companion
        ? PlayerbotsMgr::instance().GetPlayerbotAI(companion) : nullptr;
    if (!companionAI || !item)
        return ITEM_USAGE_NONE;

    std::string qualifier = std::to_string(item->GetEntry()) + ","
        + std::to_string(
            item->GetInt32Value(ITEM_FIELD_RANDOM_PROPERTIES_ID));
    auto* usageValue = companionAI->GetAiObjectContext()
        ->GetValue<ItemUsage>(valueName, qualifier);
    usageValue->Reset();
    return usageValue->Get();
}

static bool IsCompanionWhiteEquipmentVendorCandidate(
    Player* companion, Item* item)
{
    if (!companion || !item || !item->GetTemplate())
        return false;

    ItemTemplate const* itemTemplate = item->GetTemplate();
    if (itemTemplate->Quality != ITEM_QUALITY_NORMAL
        || itemTemplate->SellPrice == 0
        || (itemTemplate->Class != ITEM_CLASS_ARMOR
            && itemTemplate->Class != ITEM_CLASS_WEAPON)
        || IsCompanionProfessionTool(itemTemplate)
        || companion->HasQuestForItem(item->GetEntry()))
        return false;

    if (IsPermanentlyUnusableEquipment(companion, item))
        return true;

    ItemUsage upgrade = GetFreshCompanionItemUsage(
        companion, item, "item upgrade");
    return upgrade != ITEM_USAGE_EQUIP
        && upgrade != ITEM_USAGE_REPLACE
        && upgrade != ITEM_USAGE_BROKEN_EQUIP;
}

static bool IsCompanionUnusableGreenEquipment(
    Player* companion, Item* item)
{
    ItemTemplate const* itemTemplate = item ? item->GetTemplate() : nullptr;
    return itemTemplate
        && itemTemplate->Quality == ITEM_QUALITY_UNCOMMON
        && IsPermanentlyUnusableEquipment(companion, item);
}

static bool HasCompanionProtectedLogisticsRoute(
    Player* companion, Item* item,
    std::vector<CompanionLogisticsRoute> const& routes)
{
    return std::any_of(
        routes.begin(), routes.end(),
        [companion, item](CompanionLogisticsRoute const& route)
        {
            return route.Category == "catchall"
                || HasCompanionLogisticsRouteForItem(
                    companion, item, route.Category);
        });
}

static bool IsCompanionVendorCleanupCandidate(
    Player* companion, Item* item,
    std::vector<CompanionLogisticsRoute> const& routes,
    bool underBagPressure)
{
    ItemTemplate const* itemTemplate = item ? item->GetTemplate() : nullptr;
    if (!companion || !itemTemplate || itemTemplate->SellPrice == 0
        || IsCompanionProfessionTool(itemTemplate)
        || companion->HasQuestForItem(item->GetEntry()))
        return false;

    if (itemTemplate->Quality == ITEM_QUALITY_POOR
        || IsCompanionWhiteEquipmentVendorCandidate(companion, item))
        return true;

    bool const hasDisenchantRoute = std::any_of(
        routes.begin(), routes.end(),
        [companion, item](CompanionLogisticsRoute const& route)
        {
            return route.Category == "disenchant"
                && HasCompanionLogisticsRouteForItem(
                    companion, item, route.Category);
        });
    if (itemTemplate->Quality >= ITEM_QUALITY_UNCOMMON
        && itemTemplate->Quality <= ITEM_QUALITY_RARE
        && IsPermanentlyUnusableEquipment(companion, item)
        && !hasDisenchantRoute)
        return true;

    if (!underBagPressure
        || HasCompanionProtectedLogisticsRoute(companion, item, routes)
        || itemTemplate->Class == ITEM_CLASS_CONTAINER
        || itemTemplate->Class == ITEM_CLASS_QUIVER
        || itemTemplate->Class == ITEM_CLASS_KEY
        || itemTemplate->Class == ITEM_CLASS_QUEST
        || itemTemplate->Class == ITEM_CLASS_RECIPE)
        return false;

    ItemUsage usage = GetFreshCompanionItemUsage(
        companion, item, "item usage");
    return usage == ITEM_USAGE_VENDOR || usage == ITEM_USAGE_AH;
}

static bool EquipOneCompanionBag(Player* companion)
{
    if (!companion || !companion->IsAlive() || companion->IsInCombat())
        return false;

    Item* candidate = nullptr;
    auto considerBag = [&candidate](Item* item)
    {
        ItemTemplate const* itemTemplate = item ? item->GetTemplate() : nullptr;
        if (!itemTemplate || itemTemplate->Class != ITEM_CLASS_CONTAINER
            || itemTemplate->InventoryType != INVTYPE_BAG)
            return;

        if (!candidate
            || itemTemplate->ContainerSlots
                > candidate->GetTemplate()->ContainerSlots)
            candidate = item;
    };

    for (uint8 slot = INVENTORY_SLOT_ITEM_START;
         slot < INVENTORY_SLOT_ITEM_END; ++slot)
        considerBag(companion->GetItemByPos(INVENTORY_SLOT_BAG_0, slot));
    for (uint8 bagSlot = INVENTORY_SLOT_BAG_START;
         bagSlot < INVENTORY_SLOT_BAG_END; ++bagSlot)
    {
        Bag* bag = companion->GetBagByPos(bagSlot);
        if (!bag)
            continue;
        for (uint32 slot = 0; slot < bag->GetBagSize(); ++slot)
            considerBag(bag->GetItemByPos(slot));
    }

    if (!candidate)
        return false;

    uint8 destination = NULL_SLOT;
    uint32 smallestEmptyBagSize = UINT32_MAX;
    for (uint8 bagSlot = INVENTORY_SLOT_BAG_START;
         bagSlot < INVENTORY_SLOT_BAG_END; ++bagSlot)
    {
        Bag* equippedBag = companion->GetBagByPos(bagSlot);
        if (!equippedBag)
        {
            destination = bagSlot;
            break;
        }

        if (equippedBag->IsEmpty()
            && equippedBag->GetBagSize() < smallestEmptyBagSize)
        {
            destination = bagSlot;
            smallestEmptyBagSize = equippedBag->GetBagSize();
        }
    }

    if (destination == NULL_SLOT)
        return false;

    Bag* replacedBag = companion->GetBagByPos(destination);
    if (replacedBag
        && candidate->GetTemplate()->ContainerSlots
            <= replacedBag->GetBagSize())
        return false;

    uint16 sourcePosition =
        (candidate->GetBagSlot() << 8) | candidate->GetSlot();
    uint16 destinationPosition =
        (INVENTORY_SLOT_BAG_0 << 8) | destination;
    companion->SwapItem(sourcePosition, destinationPosition);
    return companion->GetBagByPos(destination) == candidate;
}

static std::string FindCompanionVendorCleanupSellItem(
    Player* companion,
    std::vector<CompanionLogisticsRoute> const& routes,
    bool underBagPressure, uint32& selectedEntry)
{
    selectedEntry = 0;
    std::string itemLinks;
    auto selectItem = [companion, &routes, underBagPressure,
                          &itemLinks, &selectedEntry](Item* item)
    {
        if (!itemLinks.empty() || !item || !item->GetTemplate())
            return;

        if (!IsCompanionVendorCleanupCandidate(
                companion, item, routes, underBagPressure))
            return;

        // PlayerBots' item-link parser uses an 8-bit scan position. Passing a
        // combined list longer than 255 characters can wrap that position and
        // lock the world update thread in an infinite loop. Select only one
        // item type per action; SellAction still sells every stack of the
        // selected entry, and the maintenance loop can safely run more actions.
        itemLinks += ChatHelper::FormatItem(item->GetTemplate());
        selectedEntry = item->GetEntry();
    };

    for (uint8 slot = INVENTORY_SLOT_ITEM_START;
         slot < INVENTORY_SLOT_ITEM_END && itemLinks.empty(); ++slot)
        selectItem(companion->GetItemByPos(INVENTORY_SLOT_BAG_0, slot));
    for (uint8 bagSlot = INVENTORY_SLOT_BAG_START;
         bagSlot < INVENTORY_SLOT_BAG_END && itemLinks.empty(); ++bagSlot)
    {
        Bag* bag = companion->GetBagByPos(bagSlot);
        if (!bag)
            continue;
        for (uint32 slot = 0;
             slot < bag->GetBagSize() && itemLinks.empty(); ++slot)
            selectItem(bag->GetItemByPos(slot));
    }
    return itemLinks;
}

static void MaintainCompanionAtVendor(
    QuestingCompanionRegistration& registration, Player* leader,
    Player* companion)
{
    if (!leader || !companion || !leader->IsAlive() || !companion->IsAlive()
        || leader->IsInCombat() || companion->IsInCombat())
        return;

    PlayerbotAI* companionAI =
        PlayerbotsMgr::instance().GetPlayerbotAI(companion);
    if (!companionAI)
        return;

    GuidVector nearbyNpcs = companionAI->GetAiObjectContext()
        ->GetValue<GuidVector>("nearest npcs")->Get();
    bool canUseVendor = std::any_of(
        nearbyNpcs.begin(), nearbyNpcs.end(),
        [companion](ObjectGuid const& guid)
        {
            return companion->GetNPCIfCanInteractWith(
                guid, UNIT_NPC_FLAG_VENDOR) != nullptr;
        });
    bool canUseRepairer = std::any_of(
        nearbyNpcs.begin(), nearbyNpcs.end(),
        [companion](ObjectGuid const& guid)
        {
            return companion->GetNPCIfCanInteractWith(
                guid, UNIT_NPC_FLAG_REPAIR) != nullptr;
        });

    if (registration.AutoSellTrash && canUseVendor)
    {
        uint32 soldItems = 0;
        for (uint8 pass = 0;
             pass < CompanionVendorSellTypesPerPass; ++pass)
        {
            bool const underBagPressure = companion->GetFreeInventorySpace()
                <= registration.LogisticsTargetFreeSlots;
            uint32 selectedEntry = 0;
            std::string vendorCleanup =
                FindCompanionVendorCleanupSellItem(
                    companion, registration.LogisticsRoutes,
                    underBagPressure, selectedEntry);
            uint32 const countBefore = selectedEntry
                ? companion->GetItemCount(selectedEntry) : 0;
            if (vendorCleanup.empty()
                || !companionAI->DoSpecificAction(
                "sell", Event(
                    "webadmin companion auto sell cleanup",
                    vendorCleanup), true))
                break;
            uint32 const countAfter = companion->GetItemCount(selectedEntry);
            soldItems += countBefore > countAfter ? countBefore - countAfter : 0;
        }
        if (soldItems)
        {
            registration.MaintenanceStatus = "Sold "
                + std::to_string(soldItems)
                + (soldItems == 1 ? " item" : " items") + "; "
                + std::to_string(companion->GetFreeInventorySpace())
                + " bag slots free.";
            registration.InventorySnapshotInitialized = false;
        }
    }
    if (registration.AutoRepair && canUseRepairer)
    {
        companionAI->DoSpecificAction(
            "repair", Event("webadmin companion auto repair"), true);
    }
}

static bool IsKnownCompanionProfessionSpell(
    Player* companion, Trainer::Spell const& trainerSpell)
{
    static std::array<uint32, 14> const professionSkills = {
        SKILL_ALCHEMY, SKILL_BLACKSMITHING, SKILL_COOKING,
        SKILL_ENCHANTING, SKILL_ENGINEERING, SKILL_FIRST_AID,
        SKILL_FISHING, SKILL_HERBALISM, SKILL_INSCRIPTION,
        SKILL_JEWELCRAFTING, SKILL_LEATHERWORKING, SKILL_MINING,
        SKILL_SKINNING, SKILL_TAILORING
    };

    SpellInfo const* trainerSpellInfo =
        sSpellMgr->GetSpellInfo(trainerSpell.SpellId);
    if (!trainerSpellInfo)
        return false;

    for (uint32 skillId : professionSkills)
    {
        if (!companion->HasSkill(skillId))
            continue;

        if (trainerSpell.ReqSkillLine == skillId
            || IsPartOfSkillLine(skillId, trainerSpell.SpellId))
            return true;

        for (SpellEffectInfo const& effect : trainerSpellInfo->GetEffects())
        {
            if (effect.IsEffect(SPELL_EFFECT_LEARN_SPELL)
                && effect.TriggerSpell
                && IsPartOfSkillLine(skillId, effect.TriggerSpell))
                return true;
        }
    }

    return false;
}

static void TrainCompanionAtProfessionTrainer(
    Player* leader, Player* companion)
{
    if (!leader || !companion || !leader->IsAlive() || !companion->IsAlive()
        || leader->IsInCombat() || companion->IsInCombat())
        return;

    PlayerbotAI* companionAI =
        PlayerbotsMgr::instance().GetPlayerbotAI(companion);
    if (!companionAI)
        return;

    GuidVector nearbyNpcs = companionAI->GetAiObjectContext()
        ->GetValue<GuidVector>("nearest npcs")->Get();
    for (ObjectGuid const& guid : nearbyNpcs)
    {
        Creature* trainerNpc = companion->GetNPCIfCanInteractWith(
            guid, UNIT_NPC_FLAG_TRAINER);
        if (!trainerNpc)
            continue;

        Trainer::Trainer* trainer =
            sObjectMgr->GetTrainer(trainerNpc->GetEntry());
        if (!trainer
            || trainer->GetTrainerType() != Trainer::Type::Tradeskill
            || !trainer->IsTrainerValidForPlayer(companion))
            continue;

        uint32 learnedCount = 0;
        for (Trainer::Spell const& trainerSpell : trainer->GetSpells())
        {
            if (!IsKnownCompanionProfessionSpell(companion, trainerSpell)
                || !trainer->CanTeachSpell(companion, &trainerSpell))
                continue;

            int32 const moneyCost = int32(
                trainerSpell.MoneyCost
                * companion->GetReputationPriceDiscount(trainerNpc));
            if (!companion->HasEnoughMoney(moneyCost))
                continue;

            trainer->TeachSpell(
                trainerNpc, companion, trainerSpell.SpellId);
            ++learnedCount;
        }

        if (learnedCount)
        {
            ChatHandler(leader->GetSession()).PSendSysMessage(
                "{} learned {} profession training {} from {}.",
                companion->GetName(), learnedCount,
                learnedCount == 1 ? "ability" : "abilities",
                trainerNpc->GetName());
        }
    }
}

static char const* CompanionEquipmentSlotName(uint8 slot)
{
    static std::array<char const*, EQUIPMENT_SLOT_END> const names = {
        "Head", "Neck", "Shoulders", "Shirt", "Chest", "Waist", "Legs",
        "Feet", "Wrists", "Hands", "Finger 1", "Finger 2", "Trinket 1",
        "Trinket 2", "Back", "Main hand", "Off hand", "Ranged", "Tabard"
    };
    return slot < names.size() ? names[slot] : "Equipment";
}

static QuestingCompanionRegistration* FindCompanionRegistration(
    ObjectGuid companionGuid)
{
    auto registration = std::find_if(
        QuestingCompanions.begin(), QuestingCompanions.end(),
        [companionGuid](QuestingCompanionRegistration const& value)
        {
            return value.CompanionGuid == companionGuid;
        });
    return registration != QuestingCompanions.end() ? &*registration : nullptr;
}

static bool IsCompanionLogisticsCategory(std::string const& category)
{
    static std::array<std::string, 11> const categories = {
        "cloth", "leather", "metal", "herbs", "enchanting",
        "jewelcrafting", "disenchant", "meat", "elemental", "parts",
        "catchall"
    };
    return std::find(categories.begin(), categories.end(), category)
        != categories.end();
}

static bool MatchesCompanionLogisticsCategory(
    ItemTemplate const* itemTemplate, std::string const& category)
{
    if (!itemTemplate || itemTemplate->Class != ITEM_CLASS_TRADE_GOODS)
        return false;

    uint32 subclass = itemTemplate->SubClass;
    return (category == "cloth" && subclass == ITEM_SUBCLASS_CLOTH)
        || (category == "leather" && subclass == ITEM_SUBCLASS_LEATHER)
        || (category == "metal" && subclass == ITEM_SUBCLASS_METAL_STONE)
        || (category == "herbs" && subclass == ITEM_SUBCLASS_HERB)
        || (category == "enchanting" && subclass == ITEM_SUBCLASS_ENCHANTING)
        || (category == "jewelcrafting" && subclass == ITEM_SUBCLASS_JEWELCRAFTING)
        || (category == "meat" && subclass == ITEM_SUBCLASS_MEAT)
        || (category == "elemental" && subclass == ITEM_SUBCLASS_ELEMENTAL)
        || (category == "parts" && subclass == ITEM_SUBCLASS_PARTS);
}

static bool HasCompanionLogisticsRouteForItem(
    Player* companion, Item* item, std::string const& category)
{
    if (!item || !item->GetTemplate())
        return false;
    if (category == "disenchant")
        return IsCompanionUnusableGreenEquipment(companion, item);
    return MatchesCompanionLogisticsCategory(item->GetTemplate(), category);
}

static void ConfigureAutomaticCompanionLogistics(
    QuestingCompanionRegistration& registration, Player* leader)
{
    if (!leader || !leader->GetSession())
        return;

    QueryResult result = CharacterDatabase.Query(
        "SELECT c.guid, c.name, c.race, cs.skill "
        "FROM characters c "
        "JOIN character_skills cs ON cs.guid = c.guid "
        "WHERE c.account = {} "
        "AND cs.skill IN (164, 165, 171, 197, 333, 755, 773) "
        "ORDER BY cs.value DESC, c.level DESC, c.name ASC",
        leader->GetSession()->GetAccountId());
    if (!result)
        return;

    std::map<std::string, CompanionLogisticsRoute> selected;
    auto selectRoute = [&selected](
        std::string const& category, uint32 keepQuantity,
        ObjectGuid recipientGuid, std::string const& recipientName)
    {
        if (selected.find(category) == selected.end())
        {
            selected.emplace(category, CompanionLogisticsRoute{
                category, recipientGuid, recipientName, keepQuantity });
        }
    };

    do
    {
        Field* fields = result->Fetch();
        ObjectGuid recipientGuid =
            ObjectGuid::Create<HighGuid::Player>(fields[0].Get<uint32>());
        std::string recipientName = fields[1].Get<std::string>();
        uint8 race = fields[2].Get<uint8>();
        uint16 skill = fields[3].Get<uint16>();
        if (recipientGuid == registration.CompanionGuid
            || Player::TeamIdForRace(race) != leader->GetTeamId(true))
            continue;

        switch (skill)
        {
            case SKILL_TAILORING:
                selectRoute("cloth", 20, recipientGuid, recipientName);
                break;
            case SKILL_LEATHERWORKING:
                selectRoute("leather", 0, recipientGuid, recipientName);
                break;
            case SKILL_BLACKSMITHING:
                selectRoute("metal", 0, recipientGuid, recipientName);
                break;
            case SKILL_JEWELCRAFTING:
                selectRoute("metal", 0, recipientGuid, recipientName);
                selectRoute("jewelcrafting", 0, recipientGuid, recipientName);
                break;
            case SKILL_ALCHEMY:
            case SKILL_INSCRIPTION:
                selectRoute("herbs", 0, recipientGuid, recipientName);
                break;
            case SKILL_ENCHANTING:
                selectRoute("enchanting", 0, recipientGuid, recipientName);
                selectRoute("disenchant", 0, recipientGuid, recipientName);
                break;
            default:
                break;
        }
    } while (result->NextRow());

    static std::array<std::string, 7> const routeOrder = {
        "cloth", "leather", "metal", "herbs", "enchanting",
        "jewelcrafting", "disenchant"
    };
    for (std::string const& category : routeOrder)
    {
        auto route = selected.find(category);
        if (route != selected.end())
            registration.LogisticsRoutes.push_back(route->second);
    }
    if (registration.LogisticsRoutes.empty())
        return;

    registration.LogisticsTriggerFreeSlots = 8;
    registration.LogisticsTargetFreeSlots = 12;
    registration.AutomaticLogistics = true;
    registration.LogisticsStatus =
        "Automatic profession routes configured; waiting for bag pressure or a manual run.";
}

static bool HasNearbyCompanionMailbox(Player* companion)
{
    PlayerbotAI* companionAI = companion
        ? PlayerbotsMgr::instance().GetPlayerbotAI(companion) : nullptr;
    if (!companionAI)
        return false;

    GuidVector gameObjects = companionAI->GetAiObjectContext()
        ->GetValue<GuidVector>("nearest game objects")->Get();
    for (ObjectGuid const& guid : gameObjects)
    {
        GameObject* gameObject = companionAI->GetGameObject(guid);
        if (gameObject && gameObject->isSpawned()
            && gameObject->GetGoType() == GAMEOBJECT_TYPE_MAILBOX
            && companion->IsWithinDistInMap(gameObject, INTERACTION_DISTANCE))
            return true;
    }
    return false;
}

static bool HasNearbyCompanionVendor(Player* companion)
{
    PlayerbotAI* companionAI = companion
        ? PlayerbotsMgr::instance().GetPlayerbotAI(companion) : nullptr;
    if (!companionAI)
        return false;

    GuidVector nearbyNpcs = companionAI->GetAiObjectContext()
        ->GetValue<GuidVector>("nearest npcs")->Get();
    return std::any_of(
        nearbyNpcs.begin(), nearbyNpcs.end(),
        [companion](ObjectGuid const& guid)
        {
            return companion->GetNPCIfCanInteractWith(
                guid, UNIT_NPC_FLAG_VENDOR) != nullptr;
        });
}

static void AddCompanionBagItems(Player* companion, std::vector<Item*>& items)
{
    for (uint8 slot = INVENTORY_SLOT_ITEM_START;
         slot < INVENTORY_SLOT_ITEM_END; ++slot)
    {
        if (Item* item = companion->GetItemByPos(INVENTORY_SLOT_BAG_0, slot))
            items.push_back(item);
    }
    for (uint8 bagSlot = INVENTORY_SLOT_BAG_START;
         bagSlot < INVENTORY_SLOT_BAG_END; ++bagSlot)
    {
        Bag* bag = companion->GetBagByPos(bagSlot);
        if (!bag)
            continue;
        for (uint32 slot = 0; slot < bag->GetBagSize(); ++slot)
            if (Item* item = bag->GetItemByPos(slot))
                items.push_back(item);
    }
}

static bool IsSafeCompanionLogisticsItem(
    Player* companion, Item* item, std::string const& category,
    std::vector<CompanionLogisticsRoute> const& routes)
{
    if (!companion || !item || !item->GetTemplate()
        || !item->CanBeTraded(true)
        || item->IsConjuredConsumable()
        || item->IsNotEmptyBag()
        || item->GetUInt32Value(ITEM_FIELD_DURATION)
        || companion->HasQuestForItem(item->GetEntry()))
        return false;

    ItemTemplate const* itemTemplate = item->GetTemplate();
    if (IsCompanionProfessionTool(itemTemplate))
        return false;
    if (category != "catchall")
        return HasCompanionLogisticsRouteForItem(companion, item, category);

    if (IsCompanionWhiteEquipmentVendorCandidate(companion, item)
        || IsCompanionUnusableGreenEquipment(companion, item)
        || itemTemplate->Quality == ITEM_QUALITY_POOR
        || itemTemplate->Class == ITEM_CLASS_CONSUMABLE
        || itemTemplate->Class == ITEM_CLASS_CONTAINER
        || itemTemplate->Class == ITEM_CLASS_QUIVER
        || (itemTemplate->Class == ITEM_CLASS_GLYPH
            && companion->BotCanUseItem(itemTemplate) == EQUIP_ERR_OK))
        return false;

    bool hasExplicitRoute = std::any_of(
        routes.begin(), routes.end(),
        [companion, item](CompanionLogisticsRoute const& route)
        {
            return route.Category != "catchall"
                && HasCompanionLogisticsRouteForItem(
                    companion, item, route.Category);
        });
    if (hasExplicitRoute)
        return false;

    PlayerbotAI* companionAI =
        PlayerbotsMgr::instance().GetPlayerbotAI(companion);
    if (!companionAI)
        return false;
    ItemUsage usage = companionAI->GetAiObjectContext()
        ->GetValue<ItemUsage>("item usage", item->GetEntry())->Get();
    return usage == ITEM_USAGE_VENDOR || usage == ITEM_USAGE_AH;
}

static std::vector<Item*> SelectCompanionLogisticsAttachments(
    Player* companion, CompanionLogisticsRoute const& route,
    std::vector<CompanionLogisticsRoute> const& routes,
    uint32 maximumStacks)
{
    std::vector<Item*> bagItems;
    AddCompanionBagItems(companion, bagItems);
    std::map<uint32, uint32> remainingCounts;
    for (Item* item : bagItems)
        if (IsSafeCompanionLogisticsItem(
                companion, item, route.Category, routes))
            remainingCounts[item->GetEntry()] += item->GetCount();

    std::ranges::sort(bagItems, [](Item* left, Item* right)
    {
        return left && right ? left->GetCount() > right->GetCount()
            : left != nullptr;
    });

    std::vector<Item*> attachments;
    uint32 attachmentLimit = std::min<uint32>(MAX_MAIL_ITEMS, maximumStacks);
    for (Item* item : bagItems)
    {
        if (attachments.size() >= attachmentLimit
            || !IsSafeCompanionLogisticsItem(
                companion, item, route.Category, routes))
            continue;
        uint32& remaining = remainingCounts[item->GetEntry()];
        if (remaining < item->GetCount()
            || remaining - item->GetCount() < route.KeepQuantity)
            continue;
        remaining -= item->GetCount();
        attachments.push_back(item);
    }
    return attachments;
}

static uint32 MailCompanionLogisticsRoute(
    Player* companion, CompanionLogisticsRoute const& route,
    std::vector<CompanionLogisticsRoute> const& routes,
    uint32 maximumStacks, std::string& failure)
{
    if (!companion || maximumStacks == 0)
        return 0;

    CharacterCacheEntry const* recipient =
        sCharacterCache->GetCharacterCacheByGuid(route.RecipientGuid);
    if (!recipient)
    {
        failure = route.RecipientName + " no longer exists.";
        return 0;
    }
    if (recipient->MailCount > 100)
    {
        failure = route.RecipientName + " has reached the mailbox limit.";
        return 0;
    }
    if (sCharacterCache->GetCharacterTeamByGuid(route.RecipientGuid)
        != companion->GetTeamId())
    {
        failure = "Cross-faction material mail is not allowed.";
        return 0;
    }

    std::vector<Item*> attachments = SelectCompanionLogisticsAttachments(
        companion, route, routes, maximumStacks);
    if (attachments.empty())
        return 0;

    uint32 postage = 30 * static_cast<uint32>(attachments.size());
    if (!companion->HasEnoughMoney(postage))
    {
        failure = companion->GetName() + " cannot afford the mail postage.";
        return 0;
    }

    MailDraft draft(
        route.Category == "catchall" ? "Companion bag overflow" : "Profession materials",
        route.Category == "catchall"
            ? "Unused items routed by companion logistics."
            : "Automatically routed by companion logistics.");
    CharacterDatabaseTransaction transaction = CharacterDatabase.BeginTransaction();
    for (Item* item : attachments)
    {
        companion->MoveItemFromInventory(
            item->GetBagSlot(), item->GetSlot(), true);
        item->DeleteFromInventoryDB(transaction);
        if (item->GetState() == ITEM_UNCHANGED)
            item->FSetState(ITEM_CHANGED);
        item->SetOwnerGUID(route.RecipientGuid);
        item->SaveToDB(transaction);
        draft.AddItem(item);
    }

    companion->ModifyMoney(-static_cast<int32>(postage));
    uint32 receiverAccount =
        sCharacterCache->GetCharacterAccountIdByGuid(route.RecipientGuid);
    uint32 deliveryDelay = receiverAccount == companion->GetSession()->GetAccountId()
        ? 0 : sWorld->getIntConfig(CONFIG_MAIL_DELIVERY_DELAY);
    Player* connectedRecipient =
        ObjectAccessor::FindConnectedPlayer(route.RecipientGuid);
    draft.SendMailTo(
        transaction,
        MailReceiver(connectedRecipient, route.RecipientGuid.GetCounter()),
        MailSender(companion), MAIL_CHECK_MASK_HAS_BODY, deliveryDelay);
    companion->SaveInventoryAndGoldToDB(transaction);
    CharacterDatabase.CommitTransaction(transaction);
    return static_cast<uint32>(attachments.size());
}

static void ProcessCompanionLogistics(
    QuestingCompanionRegistration& registration, Player* companion,
    bool force)
{
    if (!companion || !companion->IsAlive() || companion->IsInCombat())
        return;
    if (registration.LogisticsRoutes.empty())
    {
        registration.LogisticsStatus = "No bag routes are configured.";
        return;
    }
    uint32 freeSlots = companion->GetFreeInventorySpace();
    if (!force && (!registration.AutomaticLogistics
        || freeSlots > registration.LogisticsTriggerFreeSlots))
        return;
    if (!HasNearbyCompanionMailbox(companion))
    {
        registration.LogisticsStatus =
            "Bag threshold reached; waiting for a nearby mailbox.";
        return;
    }

    uint32 totalSent = 0;
    std::string failure;
    for (CompanionLogisticsRoute const& route : registration.LogisticsRoutes)
    {
        uint32 maximumStacks = force
            ? MAX_MAIL_ITEMS
            : registration.LogisticsTargetFreeSlots > freeSlots
                ? registration.LogisticsTargetFreeSlots - freeSlots : 0;
        if (!force && maximumStacks == 0)
            break;
        uint32 sent = MailCompanionLogisticsRoute(
            companion, route, registration.LogisticsRoutes,
            maximumStacks, failure);
        totalSent += sent;
        freeSlots = companion->GetFreeInventorySpace();
    }

    if (totalSent)
    {
        registration.LogisticsStatus = "Mailed " + std::to_string(totalSent)
            + (totalSent == 1 ? " item stack." : " item stacks.");
        registration.MaintenanceStatus = registration.LogisticsStatus;
        registration.InventorySnapshotInitialized = false;
    }
    else if (!failure.empty())
        registration.LogisticsStatus = failure;
    else
        registration.LogisticsStatus =
            "Nothing eligible exceeded the configured reserves or catch-all rules.";
}

static void EnforceCompanionEquipmentProtection(
    QuestingCompanionRegistration& registration, Player* companion)
{
    if (!companion || !companion->IsAlive() || companion->IsInCombat())
        return;

    for (uint8 slot = EQUIPMENT_SLOT_START; slot < EQUIPMENT_SLOT_END; ++slot)
    {
        ObjectGuid protectedGuid = registration.ProtectedEquipment[slot];
        if (protectedGuid.IsEmpty())
            continue;

        Item* protectedItem = companion->GetItemByGuid(protectedGuid);
        if (!protectedItem)
        {
            registration.ProtectedEquipment[slot].Clear();
            continue;
        }

        Item* equipped = companion->GetItemByPos(INVENTORY_SLOT_BAG_0, slot);
        if (equipped == protectedItem)
            continue;

        uint16 destination = 0;
        if (companion->CanEquipItem(slot, destination, protectedItem, true)
            == EQUIP_ERR_OK)
        {
            companion->SwapItem(protectedItem->GetPos(), destination);
        }
    }
}

static void TrackCompanionEquipment(
    QuestingCompanionRegistration& registration, Player* companion)
{
    if (!companion)
        return;

    for (uint8 slot = EQUIPMENT_SLOT_START; slot < EQUIPMENT_SLOT_END; ++slot)
    {
        Item* item = companion->GetItemByPos(INVENTORY_SLOT_BAG_0, slot);
        uint32 entry = item ? item->GetEntry() : 0;
        std::string name = item && item->GetTemplate()
            ? item->GetTemplate()->Name1
            : "Empty";

        if (registration.EquipmentSnapshotInitialized
            && entry != registration.EquipmentEntries[slot])
        {
            std::ostringstream description;
            description << CompanionEquipmentSlotName(slot) << ": "
                        << registration.EquipmentNames[slot] << " -> " << name;
            registration.EquipmentChanges.insert(
                registration.EquipmentChanges.begin(),
                { std::time(nullptr), description.str() });
            if (registration.EquipmentChanges.size() > 5)
                registration.EquipmentChanges.resize(5);
        }

        registration.EquipmentEntries[slot] = entry;
        registration.EquipmentNames[slot] = name;
    }
    registration.EquipmentSnapshotInitialized = true;
}

static void AddCompanionInventoryCount(
    Item* item, std::map<uint32, uint32>& counts,
    std::map<uint32, std::string>& names)
{
    if (!item || !item->GetTemplate())
        return;

    counts[item->GetEntry()] += item->GetCount();
    names[item->GetEntry()] = item->GetTemplate()->Name1;
}

static void TrackCompanionInventory(
    QuestingCompanionRegistration& registration, Player* companion)
{
    if (!companion)
        return;

    std::map<uint32, uint32> currentCounts;
    std::map<uint32, std::string> currentNames;
    for (uint8 slot = INVENTORY_SLOT_ITEM_START;
         slot < INVENTORY_SLOT_ITEM_END; ++slot)
    {
        AddCompanionInventoryCount(
            companion->GetItemByPos(INVENTORY_SLOT_BAG_0, slot),
            currentCounts, currentNames);
    }
    for (uint8 bagSlot = INVENTORY_SLOT_BAG_START;
         bagSlot < INVENTORY_SLOT_BAG_END; ++bagSlot)
    {
        Bag* bag = companion->GetBagByPos(bagSlot);
        if (!bag)
            continue;
        for (uint32 slot = 0; slot < bag->GetBagSize(); ++slot)
        {
            AddCompanionInventoryCount(
                bag->GetItemByPos(slot), currentCounts, currentNames);
        }
    }

    if (registration.InventorySnapshotInitialized)
    {
        for (auto const& [entry, count] : currentCounts)
        {
            uint32 previous = registration.InventoryCounts[entry];
            if (count <= previous)
                continue;

            std::ostringstream description;
            description << "Added " << currentNames[entry] << " x"
                        << (count - previous);
            registration.InventoryChanges.insert(
                registration.InventoryChanges.begin(),
                { std::time(nullptr), description.str() });
        }
        if (registration.InventoryChanges.size() > 8)
            registration.InventoryChanges.resize(8);
    }

    registration.InventoryCounts = std::move(currentCounts);
    registration.InventorySnapshotInitialized = true;
}

static bool IsNeededCompanionQuestObject(Player* companion, GameObject* gameObject)
{
    if (!companion || !gameObject || !gameObject->isSpawned()
        || gameObject->GetGoState() != GO_STATE_READY
        || gameObject->GetGoType() != GAMEOBJECT_TYPE_CHEST)
        return false;

    GameObjectQuestItemList const* items =
        sObjectMgr->GetGameObjectQuestItemList(gameObject->GetEntry());
    if (!items)
        return false;

    for (uint32 itemId : *items)
    {
        if (itemId && companion->HasQuestForItem(itemId))
            return true;
    }
    return false;
}

static void CollectCompanionQuestObject(
    QuestingCompanionRegistration& registration, Player* companion)
{
    if (!registration.GatherEnabled)
    {
        registration.QuestObjectStatus = "Quest-object gathering is disabled.";
        return;
    }

    PlayerbotAI* companionAI =
        PlayerbotsMgr::instance().GetPlayerbotAI(companion);
    if (!companionAI || companion->IsInCombat())
        return;

    AiObjectContext* context = companionAI->GetAiObjectContext();
    GuidVector gameObjects =
        context->GetValue<GuidVector>("nearest game objects")->Get();
    GameObject* nearest = nullptr;
    float nearestDistance = CompanionGatherRadius;
    for (ObjectGuid const& guid : gameObjects)
    {
        GameObject* candidate = companionAI->GetGameObject(guid);
        if (!IsNeededCompanionQuestObject(companion, candidate))
            continue;

        float distance = companion->GetDistance(candidate);
        if (distance < nearestDistance)
        {
            nearest = candidate;
            nearestDistance = distance;
        }
    }

    if (!nearest)
    {
        registration.QuestObjectStatus =
            "No needed quest object within 40 metres.";
        return;
    }

    LootObject loot(companion, nearest->GetGUID());
    if (loot.IsEmpty())
    {
        registration.QuestObjectStatus =
            "Found " + nearest->GetName() + ", but PlayerBots rejected it.";
        return;
    }
    if (!loot.IsLootPossible(companion))
    {
        registration.QuestObjectStatus =
            "Found " + nearest->GetName() + ", but it is not currently usable.";
        return;
    }

    context->GetValue<LootObjectStack*>("available loot")->Get()->Add(
        nearest->GetGUID());
    context->GetValue<LootObject>("loot target")->Set(loot);

    std::ostringstream status;
    if (nearestDistance >= INTERACTION_DISTANCE - 2.0f)
    {
        bool moving = companionAI->DoSpecificAction(
            "move to loot", Event("webadmin companion quest object"), true);
        status << (moving ? "Moving to " : "Could not move to ")
               << nearest->GetName() << " (" << int(nearestDistance) << "m).";
    }
    else
    {
        bool opening = companionAI->DoSpecificAction(
            "open loot", Event("webadmin companion quest object"), true);
        status << (opening ? "Opening " : "Could not open ")
               << nearest->GetName() << ".";
    }
    registration.QuestObjectStatus = status.str();
}

static uint32 GetCompanionInventoryCapacity(Player* player)
{
    uint32 capacity = INVENTORY_SLOT_ITEM_END - INVENTORY_SLOT_ITEM_START;
    for (uint8 slot = INVENTORY_SLOT_BAG_START;
         slot < INVENTORY_SLOT_BAG_END; ++slot)
    {
        if (Bag* bag = player->GetBagByPos(slot))
            capacity += bag->GetBagSize();
    }
    return capacity;
}

static void ReportCompanionLogisticsPreview(
    ChatHandler* handler, Player* companion,
    std::vector<CompanionLogisticsRoute> const& routes,
    bool autoSellTrash, uint32 targetFreeSlots)
{
    std::vector<Item*> bagItems;
    AddCompanionBagItems(companion, bagItems);
    std::map<Item*, CompanionLogisticsRoute const*> mailedItems;
    for (CompanionLogisticsRoute const& route : routes)
    {
        for (Item* item : SelectCompanionLogisticsAttachments(
                 companion, route, routes,
                 MAX_MAIL_ITEMS))
            mailedItems[item] = &route;
    }

    bool mailboxNearby = HasNearbyCompanionMailbox(companion);
    bool vendorNearby = HasNearbyCompanionVendor(companion);
    bool const underBagPressure = companion->GetFreeInventorySpace()
        <= targetFreeSlots;
    uint32 sellStacks = 0;
    for (Item* item : bagItems)
        if (autoSellTrash && IsCompanionVendorCleanupCandidate(
                companion, item, routes, underBagPressure))
            ++sellStacks;

    uint32 currentFreeSlots = companion->GetFreeInventorySpace();
    uint32 capacity = GetCompanionInventoryCapacity(companion);
    uint32 mailedStacks = static_cast<uint32>(mailedItems.size());
    uint32 potentialFreeSlots = std::min<uint32>(
        capacity, currentFreeSlots + mailedStacks + sellStacks);
    handler->PSendSysMessage(
        "WEBADMIN_LOGISTICS_PREVIEW\t{}\t{}\t{}\t{}\t{}\t{}\t{}",
        companion->GetName(), currentFreeSlots, capacity, potentialFreeSlots,
        mailedStacks * 30, mailboxNearby ? 1 : 0,
        vendorNearby ? 1 : 0);

    for (Item* item : bagItems)
    {
        if (!item || !item->GetTemplate())
            continue;
        ItemTemplate const* itemTemplate = item->GetTemplate();
        std::string action = "Keep";
        std::string destination = "Companion bags";
        std::string reason = "PlayerBots does not consider this item routing surplus.";

        auto mailed = mailedItems.find(item);
        if (mailed != mailedItems.end())
        {
            action = "Mail";
            destination = mailed->second->RecipientName;
            reason = mailed->second->Category == "catchall"
                ? "Catch-all route: PlayerBots considers this auction or vendor surplus."
                : "Matches the " + mailed->second->Category
                    + " route above its configured reserve.";
        }
        else if (companion->HasQuestForItem(item->GetEntry()))
        {
            action = "Protected";
            reason = "Needed by an active quest.";
        }
        else if (IsCompanionProfessionTool(itemTemplate))
        {
            action = "Protected";
            reason = "Profession tool retained for gathering or crafting.";
        }
        else if (autoSellTrash
                 && IsCompanionVendorCleanupCandidate(
                     companion, item, routes, underBagPressure))
        {
            action = "Sell";
            destination = vendorNearby ? "Nearby vendor" : "Vendor when nearby";
            if (itemTemplate->Quality == ITEM_QUALITY_POOR)
                reason = "Grey-quality vendor item.";
            else if (itemTemplate->Quality == ITEM_QUALITY_NORMAL
                     && (itemTemplate->Class == ITEM_CLASS_ARMOR
                         || itemTemplate->Class == ITEM_CLASS_WEAPON))
                reason = "White equipment is unusable or not an upgrade.";
            else if (IsPermanentlyUnusableEquipment(companion, item))
                reason = "Equipment is permanently unusable and has no disenchant route.";
            else
                reason = "Bag space is low and PlayerBots considers this vendor or auction surplus.";
        }
        else if (!item->CanBeTraded(true))
        {
            action = "Protected";
            reason = "The item cannot be traded.";
        }
        else if (item->IsConjuredConsumable())
        {
            action = "Protected";
            reason = "Conjured items cannot be routed safely.";
        }
        else if (item->IsNotEmptyBag())
        {
            action = "Protected";
            reason = "Non-empty bags cannot be mailed.";
        }
        else if (item->GetUInt32Value(ITEM_FIELD_DURATION))
        {
            action = "Protected";
            reason = "Temporary-duration items are not routed.";
        }
        else
        {
            CompanionLogisticsRoute const* matchingRoute = nullptr;
            for (CompanionLogisticsRoute const& route : routes)
            {
                if ((route.Category != "catchall"
                        && HasCompanionLogisticsRouteForItem(
                            companion, item, route.Category))
                    || (route.Category == "catchall"
                        && IsSafeCompanionLogisticsItem(
                            companion, item, route.Category,
                            routes)))
                {
                    matchingRoute = &route;
                    break;
                }
            }
            if (matchingRoute)
                reason = matchingRoute->Category == "catchall"
                    ? "Kept because the catch-all mail attachment limit was reached."
                    : "Kept by the " + matchingRoute->Category
                        + " reserve or mail attachment limit.";
            else if (!autoSellTrash
                     && IsCompanionVendorCleanupCandidate(
                         companion, item, routes, underBagPressure))
                reason = "Vendor cleanup is disabled, so this item is kept.";
        }

        handler->PSendSysMessage(
            "WEBADMIN_LOGISTICS_PREVIEW_ITEM\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}",
            item->GetEntry(), item->GetCount(),
            static_cast<uint32>(itemTemplate->Quality), item->GetBagSlot(),
            item->GetSlot(), action,
            CompanionProtocolText(destination), CompanionProtocolText(reason),
            CompanionProtocolText(itemTemplate->Name1));
    }
}

static void ReportCompanionItem(
    ChatHandler* handler, Player* player, Item* item,
    char const* location, uint8 bag, uint8 slot,
    QuestingCompanionRegistration const* registration)
{
    if (!handler || !player || !item || !item->GetTemplate())
        return;

    ItemTemplate const* itemTemplate = item->GetTemplate();
    bool protectedItem = location == std::string("equipment")
        && registration && slot < EQUIPMENT_SLOT_END
        && registration->ProtectedEquipment[slot] == item->GetGUID();
    handler->PSendSysMessage(
        "WEBADMIN_COMPANION_ITEM\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}",
        player->GetName(), location, bag, slot, item->GetEntry(), item->GetCount(),
        static_cast<uint32>(itemTemplate->Quality), itemTemplate->ItemLevel,
        item->GetUInt32Value(ITEM_FIELD_DURABILITY),
        item->GetUInt32Value(ITEM_FIELD_MAXDURABILITY), protectedItem ? 1 : 0,
        CompanionProtocolText(itemTemplate->Name1));
}

static void ReportCompanionInventory(
    ChatHandler* handler, Player* player,
    QuestingCompanionRegistration const* registration)
{
    for (uint8 slot = EQUIPMENT_SLOT_START; slot < EQUIPMENT_SLOT_END; ++slot)
    {
        ReportCompanionItem(
            handler, player,
            player->GetItemByPos(INVENTORY_SLOT_BAG_0, slot),
            "equipment", INVENTORY_SLOT_BAG_0, slot, registration);
    }

    for (uint8 slot = INVENTORY_SLOT_ITEM_START;
         slot < INVENTORY_SLOT_ITEM_END; ++slot)
    {
        ReportCompanionItem(
            handler, player,
            player->GetItemByPos(INVENTORY_SLOT_BAG_0, slot),
            "bag", INVENTORY_SLOT_BAG_0, slot, registration);
    }

    for (uint8 bagSlot = INVENTORY_SLOT_BAG_START;
         bagSlot < INVENTORY_SLOT_BAG_END; ++bagSlot)
    {
        Bag* bag = player->GetBagByPos(bagSlot);
        if (!bag)
            continue;

        for (uint32 slot = 0; slot < bag->GetBagSize(); ++slot)
        {
            ReportCompanionItem(
                handler, player, bag->GetItemByPos(slot), "bag", bagSlot,
                static_cast<uint8>(slot), registration);
        }
    }

    bool autoSell = registration && registration->AutoSellTrash;
    bool autoRepair = registration && registration->AutoRepair;
    handler->PSendSysMessage(
        "WEBADMIN_COMPANION_MAINTENANCE\t{}\t{}\t{}", player->GetName(),
        autoSell ? 1 : 0, autoRepair ? 1 : 0);
    if (registration)
    {
        handler->PSendSysMessage(
            "WEBADMIN_COMPANION_BEHAVIOR\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}\t{}",
            player->GetName(), registration->BehaviorPreset,
            registration->Role, registration->Movement,
            registration->CombatFocus, registration->FollowDistance,
            registration->LootEnabled ? 1 : 0,
            registration->GatherEnabled ? 1 : 0,
            registration->AutoSellTrash ? 1 : 0,
            registration->AutoRepair ? 1 : 0);
        handler->PSendSysMessage(
            "WEBADMIN_COMPANION_MAINTENANCE_STATUS\t{}\t{}",
            player->GetName(),
            CompanionProtocolText(registration->MaintenanceStatus));
        for (CompanionEquipmentChange const& change :
             registration->EquipmentChanges)
        {
            handler->PSendSysMessage(
                "WEBADMIN_COMPANION_EQUIPMENT_CHANGE\t{}\t{}\t{}",
                player->GetName(), static_cast<int64>(change.ChangedAt),
                CompanionProtocolText(change.Description));
        }
        for (CompanionEquipmentChange const& change :
             registration->InventoryChanges)
        {
            handler->PSendSysMessage(
                "WEBADMIN_COMPANION_INVENTORY_CHANGE\t{}\t{}\t{}",
                player->GetName(), static_cast<int64>(change.ChangedAt),
                CompanionProtocolText(change.Description));
        }
    }
}

static std::string CompanionObjectiveName(Quest const* quest, uint8 index)
{
    if (!quest->ObjectiveText[index].empty())
        return CompanionProtocolText(quest->ObjectiveText[index]);

    int32 entry = quest->RequiredNpcOrGo[index];
    if (entry > 0)
    {
        if (CreatureTemplate const* creature =
                sObjectMgr->GetCreatureTemplate(static_cast<uint32>(entry)))
            return CompanionProtocolText(creature->Name);
    }
    else if (entry < 0)
    {
        if (GameObjectTemplate const* gameObject =
                sObjectMgr->GetGameObjectTemplate(static_cast<uint32>(-entry)))
            return CompanionProtocolText(gameObject->name);
    }
    return entry > 0 ? "Creature" : "Object";
}

static void ReportCompanionQuestProgress(ChatHandler* handler, Player* player)
{
    for (uint8 slot = 0; slot < MAX_QUEST_LOG_SIZE; ++slot)
    {
        uint32 questId = player->GetQuestSlotQuestId(slot);
        Quest const* quest = questId
            ? sObjectMgr->GetQuestTemplate(questId)
            : nullptr;
        if (!quest)
            continue;

        handler->PSendSysMessage(
            "WEBADMIN_COMPANION_QUEST\t{}\t{}\t{}\t{}",
            player->GetName(), questId,
            player->GetQuestStatus(questId) == QUEST_STATUS_COMPLETE ? 1 : 0,
            CompanionProtocolText(quest->GetTitle()));

        for (uint8 index = 0; index < QUEST_ITEM_OBJECTIVES_COUNT; ++index)
        {
            uint32 itemId = quest->RequiredItemId[index];
            uint32 required = quest->RequiredItemCount[index];
            if (!itemId || !required)
                continue;

            ItemTemplate const* item = sObjectMgr->GetItemTemplate(itemId);
            handler->PSendSysMessage(
                "WEBADMIN_COMPANION_OBJECTIVE\t{}\t{}\titem\t{}\t{}\t{}\t{}",
                player->GetName(), questId, itemId,
                std::min<uint32>(player->GetItemCount(itemId, false), required),
                required,
                item ? CompanionProtocolText(item->Name1) : "Quest item");
        }

        for (uint8 index = 0; index < QUEST_OBJECTIVES_COUNT; ++index)
        {
            int32 signedEntry = quest->RequiredNpcOrGo[index];
            uint32 required = quest->RequiredNpcOrGoCount[index];
            if (!signedEntry || !required)
                continue;

            uint32 entry = signedEntry > 0
                ? static_cast<uint32>(signedEntry)
                : static_cast<uint32>(-signedEntry);
            handler->PSendSysMessage(
                "WEBADMIN_COMPANION_OBJECTIVE\t{}\t{}\t{}\t{}\t{}\t{}\t{}",
                player->GetName(), questId,
                signedEntry > 0 ? "creature" : "gameobject", entry,
                std::min<uint32>(
                    player->GetReqKillOrCastCurrentCount(questId, signedEntry),
                    required),
                required, CompanionObjectiveName(quest, index));
        }
    }
}

enum class CompanionQuestAcceptResult
{
    Accepted,
    AlreadyKnown,
    RequirementsNotMet,
    QuestLogFull
};

static CompanionQuestAcceptResult AcceptCompanionQuest(
    Player* leader, Player* companion, Quest const* quest, Object* questGiver,
    bool reportFailure)
{
    if (!quest || companion->GetQuestStatus(quest->GetQuestId()) != QUEST_STATUS_NONE
        || companion->GetQuestRewardStatus(quest->GetQuestId()))
        return CompanionQuestAcceptResult::AlreadyKnown;

    if (!companion->CanTakeQuest(quest, false))
    {
        if (reportFailure)
        {
            ChatHandler(leader->GetSession()).PSendSysMessage(
                "Questing companion {} cannot mirror [{}]: its race, class, level, "
                "reputation, or prerequisites do not meet the requirements.",
                companion->GetName(), quest->GetTitle());
        }
        return CompanionQuestAcceptResult::RequirementsNotMet;
    }

    if (!companion->CanAddQuest(quest, false))
    {
        if (reportFailure)
        {
            ChatHandler(leader->GetSession()).PSendSysMessage(
                "Questing companion {} cannot mirror [{}]: its quest log is full.",
                companion->GetName(), quest->GetTitle());
        }
        return CompanionQuestAcceptResult::QuestLogFull;
    }

    companion->AddQuest(quest, questGiver);
    if (companion->CanCompleteQuest(quest->GetQuestId()))
        companion->CompleteQuest(quest->GetQuestId());
    if (quest->GetSrcSpell() > 0)
        companion->CastSpell(companion, quest->GetSrcSpell(), true);

    ChatHandler(leader->GetSession()).PSendSysMessage(
        "Questing companion {} accepted [{}].",
        companion->GetName(), quest->GetTitle());
    return CompanionQuestAcceptResult::Accepted;
}

static void SyncLeaderQuestLog(Player* leader, Player* companion)
{
    for (uint8 slot = 0; slot < MAX_QUEST_LOG_SIZE; ++slot)
    {
        uint32 questId = leader->GetQuestSlotQuestId(slot);
        if (questId)
        {
            AcceptCompanionQuest(
                leader, companion, sObjectMgr->GetQuestTemplate(questId), leader, true);
        }
    }
}

static bool IsClassOrProfessionQuestFor(Quest const* quest, Player* companion)
{
    if (!quest || !companion)
        return false;

    uint32 classMask = quest->GetRequiredClasses();
    if (classMask && (classMask & (1u << (companion->getClass() - 1))))
        return true;

    int32 zoneOrSort = quest->GetZoneOrSort();
    if (zoneOrSort < 0)
    {
        int32 questSort = -zoneOrSort;
        uint8 questClass = ClassByQuestSort(questSort);
        if (questClass)
            return questClass == companion->getClass();

        uint32 questSkill = SkillByQuestSort(questSort);
        if (questSkill)
            return true;
    }

    return quest->GetRequiredSkill() != 0;
}

static void AcceptEligibleCompanionSpecialQuests(
    Player* leader, Player* companion, WorldObject* questGiver,
    QuestRelationBounds questRelations)
{
    if (!leader || !companion || !questGiver
        || !IsActiveQuestingCompanion(leader, companion)
        || !questGiver->IsWithinDistInMap(companion, INTERACTION_DISTANCE))
        return;

    for (auto relation = questRelations.first;
         relation != questRelations.second; ++relation)
    {
        Quest const* quest = sObjectMgr->GetQuestTemplate(relation->second);
        if (IsClassOrProfessionQuestFor(quest, companion))
        {
            AcceptCompanionQuest(
                leader, companion, quest, questGiver, false);
        }
    }
}

static void AcceptCompanionSpecialQuests(
    Player* leader, WorldObject* questGiver, QuestRelationBounds questRelations)
{
    if (!leader || !leader->GetSession() || leader->GetSession()->IsBot())
        return;

    for (QuestingCompanionRegistration const& registration : QuestingCompanions)
    {
        if (registration.LeaderGuid != leader->GetGUID())
            continue;

        Player* companion =
            ObjectAccessor::FindConnectedPlayer(registration.CompanionGuid);
        AcceptEligibleCompanionSpecialQuests(
            leader, companion, questGiver, questRelations);
    }
}

static void AcceptNearbyCompanionSpecialQuests(
    Player* leader, Player* companion)
{
    if (!leader || !companion || !leader->IsAlive() || !companion->IsAlive()
        || leader->IsInCombat() || companion->IsInCombat())
        return;

    PlayerbotAI* companionAI =
        PlayerbotsMgr::instance().GetPlayerbotAI(companion);
    if (!companionAI)
        return;

    AiObjectContext* context = companionAI->GetAiObjectContext();
    GuidVector nearbyNpcs = context->GetValue<GuidVector>("nearest npcs")->Get();
    for (ObjectGuid const& guid : nearbyNpcs)
    {
        Creature* questGiver = companion->GetNPCIfCanInteractWith(
            guid, UNIT_NPC_FLAG_QUESTGIVER);
        if (!questGiver)
            continue;

        AcceptEligibleCompanionSpecialQuests(
            leader, companion, questGiver,
            sObjectMgr->GetCreatureQuestRelationBounds(questGiver->GetEntry()));
    }

    GuidVector nearbyObjects =
        context->GetValue<GuidVector>("nearest game objects")->Get();
    for (ObjectGuid const& guid : nearbyObjects)
    {
        GameObject* questGiver = companionAI->GetGameObject(guid);
        if (!questGiver || !questGiver->isSpawned())
            continue;

        AcceptEligibleCompanionSpecialQuests(
            leader, companion, questGiver,
            sObjectMgr->GetGOQuestRelationBounds(questGiver->GetEntry()));
    }
}

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
            { "inspect", HandleCompanionInspectCommand, SEC_PLAYER, Console::Yes },
            { "start", HandleCompanionStartCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "dismiss", HandleCompanionDismissCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "reset", HandleCompanionResetCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "protect", HandleCompanionProtectCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "behavior", HandleCompanionBehaviorCommand, SEC_PLAYER, Console::Yes },
            { "preset", HandleCompanionPresetCommand, SEC_PLAYER, Console::Yes },
            { "regroup", HandleCompanionRegroupCommand, SEC_PLAYER, Console::Yes },
            { "logistics", HandleCompanionLogisticsCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "logistics-preview", HandleCompanionLogisticsPreviewCommand, SEC_ADMINISTRATOR, Console::Yes },
            { "logistics-run", HandleCompanionLogisticsRunCommand, SEC_ADMINISTRATOR, Console::Yes }
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
        if (!companionAccount)
        {
            handler->SendErrorMessage(
                "The companion account could not be found.");
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
        bool failed = false;
        for (std::string const& message : messages)
        {
            handler->PSendSysMessage("WEBADMIN_COMPANION_RESULT\t{}", message);
            if (message.rfind("Failure:", 0) == 0)
            {
                handler->SendErrorMessage(message, false);
                failed = true;
            }
        }
        if (failed)
            return false;
        RegisterQuestingCompanion(leader->GetGUID(), companionGuid);
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
        bool failed = false;
        for (std::string const& message : messages)
        {
            handler->PSendSysMessage("WEBADMIN_COMPANION_RESULT\t{}", message);
            if (message.rfind("Failure:", 0) == 0)
            {
                handler->SendErrorMessage(message, false);
                failed = true;
            }
        }
        if (failed)
            return false;
        UnregisterQuestingCompanion(companionGuid);
        handler->PSendSysMessage(
            "Questing companion {} is logging out.", companionName);
        return true;
    }

    static bool HandleCompanionResetCommand(ChatHandler* handler, char const* args)
    {
        Player* leader = nullptr;
        std::string companionName;
        ObjectGuid companionGuid;
        if (!ParseCompanionArguments(
                handler, args, leader, companionName, companionGuid))
            return false;

        PlayerbotMgr* manager = PlayerbotsMgr::instance().GetPlayerbotMgr(leader);
        Player* companion = manager ? manager->GetPlayerBot(companionGuid) : nullptr;
        PlayerbotAI* companionAI = companion
            ? PlayerbotsMgr::instance().GetPlayerbotAI(companion)
            : nullptr;
        if (!companion || !companionAI
            || !IsActiveQuestingCompanion(leader, companion))
        {
            handler->SendErrorMessage(
                "That character is not this leader's active companion.");
            return false;
        }
        if (leader->IsInCombat() || companion->IsInCombat())
        {
            handler->SendErrorMessage(
                "Wait until the leader and companion are out of combat.");
            return false;
        }

        companionAI->Reset(false);
        if (QuestingCompanionRegistration* registration =
                FindCompanionRegistration(companionGuid))
        {
            ConfigureCompanionActivity(*registration, companion);
            registration->InitialSyncPending = true;
            registration->LeaderWasDead = false;
            registration->QuestObjectCheckTimer = 0;
            registration->BagEquipCheckTimer = 0;
            registration->ProfessionTrainingCheckTimer = 0;
            registration->LogisticsCheckTimer = 0;
        }
        handler->PSendSysMessage(
            "Reset questing companion {} and restored follow, combat and loot behaviour.",
            companion->GetName());
        return true;
    }

    static bool PlayerCanControlCompanion(
        ChatHandler* handler, Player* leader)
    {
        WorldSession* session = handler ? handler->GetSession() : nullptr;
        if (!session || session->GetPlayer() == leader)
            return true;

        handler->SendErrorMessage(
            "Players may control only their own questing companions.");
        return false;
    }

    static bool RequireActiveCompanion(
        ChatHandler* handler, Player* leader, ObjectGuid companionGuid,
        Player*& companion, PlayerbotAI*& companionAI)
    {
        if (!PlayerCanControlCompanion(handler, leader))
            return false;
        PlayerbotMgr* manager = PlayerbotsMgr::instance().GetPlayerbotMgr(leader);
        companion = manager ? manager->GetPlayerBot(companionGuid) : nullptr;
        companionAI = companion
            ? PlayerbotsMgr::instance().GetPlayerbotAI(companion)
            : nullptr;
        if (!companion || !companionAI
            || !IsActiveQuestingCompanion(leader, companion))
        {
            handler->SendErrorMessage(
                "That character is not this leader's active companion.");
            return false;
        }
        RegisterQuestingCompanion(leader->GetGUID(), companionGuid);
        return true;
    }

    static bool SupportsCompanionRole(
        Player* companion, std::string const& role)
    {
        return role == "auto"
            || (role == "damage" && PlayerbotAI::IsDps(companion, true))
            || (role == "tank" && PlayerbotAI::IsTank(companion, true))
            || (role == "healer" && PlayerbotAI::IsHeal(companion, true));
    }

    static bool ApplyCompanionBehavior(
        ChatHandler* handler, Player* leader, Player* companion,
        QuestingCompanionRegistration& registration)
    {
        if (leader->IsInCombat() || companion->IsInCombat())
        {
            handler->SendErrorMessage(
                "Wait until the leader and companion are out of combat.");
            return false;
        }
        if (!SupportsCompanionRole(companion, registration.Role))
        {
            handler->SendErrorMessage(
                "That companion's current specialization does not support the selected role.");
            return false;
        }

        ConfigureCompanionActivity(registration, companion);
        registration.QuestObjectCheckTimer = 0;
        registration.TrashSellCheckTimer = 0;
        registration.ProfessionTrainingCheckTimer = 0;
        registration.LogisticsCheckTimer = 0;
        handler->PSendSysMessage(
            "Updated {}: {} role, {}, {}, {}m follow distance; loot {}, gather {}, sell {}, repair {}.",
            companion->GetName(), ResolveCompanionRole(registration, companion),
            registration.Movement,
            registration.CombatFocus == "assist" ? "assist leader" : "defend party",
            registration.FollowDistance,
            registration.LootEnabled ? "on" : "off",
            registration.GatherEnabled ? "on" : "off",
            registration.AutoSellTrash ? "on" : "off",
            registration.AutoRepair ? "on" : "off");
        return true;
    }

    static bool HandleCompanionBehaviorCommand(
        ChatHandler* handler, char const* args)
    {
        std::istringstream input(args ? args : "");
        std::string leaderName, companionName, preset, role, movement, focus,
            unexpected;
        float followDistance = 0.0f;
        int loot = -1, gather = -1, autoSell = -1, autoRepair = -1;
        if (!(input >> leaderName >> companionName >> preset >> role >> movement
              >> focus >> followDistance >> loot >> gather >> autoSell >> autoRepair)
            || (input >> unexpected)
            || (role != "auto" && role != "tank" && role != "healer"
                && role != "damage")
            || (movement != "follow" && movement != "stay")
            || (focus != "assist" && focus != "defend")
            || followDistance < 1.0f || followDistance > 20.0f
            || loot < 0 || loot > 1 || gather < 0 || gather > 1
            || autoSell < 0 || autoSell > 1 || autoRepair < 0 || autoRepair > 1)
        {
            handler->SendErrorMessage(
                "Usage: webadmin companion behavior <leader> <companion> <preset> <auto|tank|healer|damage> <follow|stay> <assist|defend> <1-20 metres> <loot 0|1> <gather 0|1> <sell 0|1> <repair 0|1>");
            return false;
        }

        std::string pairArguments = leaderName + " " + companionName;
        Player* leader = nullptr;
        ObjectGuid companionGuid;
        if (!ParseCompanionArguments(
                handler, pairArguments.c_str(), leader, companionName,
                companionGuid))
            return false;
        Player* companion = nullptr;
        PlayerbotAI* companionAI = nullptr;
        if (!RequireActiveCompanion(
                handler, leader, companionGuid, companion, companionAI))
            return false;
        if (!SupportsCompanionRole(companion, role))
        {
            handler->SendErrorMessage(
                "That companion's current specialization does not support the selected role.");
            return false;
        }

        QuestingCompanionRegistration* registration =
            FindCompanionRegistration(companionGuid);
        registration->BehaviorPreset = preset;
        registration->Role = role;
        registration->Movement = movement;
        registration->CombatFocus = focus;
        registration->FollowDistance = followDistance;
        registration->LootEnabled = loot != 0;
        registration->GatherEnabled = gather != 0;
        registration->AutoSellTrash = autoSell != 0;
        registration->AutoRepair = autoRepair != 0;
        return ApplyCompanionBehavior(
            handler, leader, companion, *registration);
    }

    static bool HandleCompanionPresetCommand(
        ChatHandler* handler, char const* args)
    {
        std::istringstream input(args ? args : "");
        std::string leaderName, companionName, preset, unexpected;
        if (!(input >> leaderName >> companionName >> preset)
            || (input >> unexpected)
            || (preset != "questing" && preset != "dungeon-tank"
                && preset != "dungeon-healer"))
        {
            handler->SendErrorMessage(
                "Usage: webadmin companion preset <leader> <companion> <questing|dungeon-tank|dungeon-healer>");
            return false;
        }

        std::string pairArguments = leaderName + " " + companionName;
        Player* leader = nullptr;
        ObjectGuid companionGuid;
        if (!ParseCompanionArguments(
                handler, pairArguments.c_str(), leader, companionName,
                companionGuid))
            return false;
        Player* companion = nullptr;
        PlayerbotAI* companionAI = nullptr;
        if (!RequireActiveCompanion(
                handler, leader, companionGuid, companion, companionAI))
            return false;
        QuestingCompanionRegistration* registration =
            FindCompanionRegistration(companionGuid);

        std::string const presetRole = preset == "dungeon-tank" ? "tank"
            : preset == "dungeon-healer" ? "healer" : "auto";
        if (!SupportsCompanionRole(companion, presetRole))
        {
            handler->SendErrorMessage(
                "That companion's current specialization does not support the selected role.");
            return false;
        }

        registration->BehaviorPreset = preset;
        registration->Role = presetRole;
        registration->Movement = "follow";
        registration->CombatFocus = preset == "questing" ? "assist" : "defend";
        registration->FollowDistance = preset == "dungeon-healer" ? 8.0f
            : preset == "dungeon-tank" ? 4.0f : 3.0f;
        registration->LootEnabled = true;
        registration->GatherEnabled = preset == "questing";
        registration->AutoSellTrash = true;
        registration->AutoRepair = true;
        return ApplyCompanionBehavior(
            handler, leader, companion, *registration);
    }

    static bool HandleCompanionRegroupCommand(
        ChatHandler* handler, char const* args)
    {
        Player* leader = nullptr;
        std::string companionName;
        ObjectGuid companionGuid;
        if (!ParseCompanionArguments(
                handler, args, leader, companionName, companionGuid))
            return false;
        Player* companion = nullptr;
        PlayerbotAI* companionAI = nullptr;
        if (!RequireActiveCompanion(
                handler, leader, companionGuid, companion, companionAI))
            return false;
        if (leader->IsInCombat() || companion->IsInCombat())
        {
            handler->SendErrorMessage(
                "Wait until the leader and companion are out of combat.");
            return false;
        }

        QuestingCompanionRegistration* registration =
            FindCompanionRegistration(companionGuid);
        registration->Movement = "follow";
        registration->BehaviorPreset = "custom";
        companionAI->Reset(false);
        ConfigureCompanionActivity(*registration, companion);
        handler->PSendSysMessage(
            "Regrouped {} and restored follow behaviour.", companion->GetName());
        return true;
    }

    static bool HandleCompanionProtectCommand(ChatHandler* handler, char const* args)
    {
        std::istringstream input(args ? args : "");
        std::string leaderName, companionName, state, unexpected;
        int slot = -1;
        if (!(input >> leaderName >> companionName >> slot >> state)
            || (input >> unexpected)
            || (state != "on" && state != "off"))
        {
            handler->SendErrorMessage(
                "Usage: webadmin companion protect <leader> <companion> <slot> <on|off>");
            return false;
        }
        if (slot < EQUIPMENT_SLOT_START || slot >= EQUIPMENT_SLOT_END)
        {
            handler->SendErrorMessage("The equipment slot is invalid.");
            return false;
        }

        std::string pairArguments = leaderName + " " + companionName;
        Player* leader = nullptr;
        ObjectGuid companionGuid;
        if (!ParseCompanionArguments(
                handler, pairArguments.c_str(), leader, companionName,
                companionGuid))
            return false;

        PlayerbotMgr* manager = PlayerbotsMgr::instance().GetPlayerbotMgr(leader);
        Player* companion = manager ? manager->GetPlayerBot(companionGuid) : nullptr;
        if (!companion || !IsActiveQuestingCompanion(leader, companion))
        {
            handler->SendErrorMessage(
                "That character is not this leader's active companion.");
            return false;
        }

        RegisterQuestingCompanion(leader->GetGUID(), companionGuid);
        QuestingCompanionRegistration* registration =
            FindCompanionRegistration(companionGuid);
        if (!registration)
        {
            handler->SendErrorMessage("The companion registration was not found.");
            return false;
        }

        if (state == "off")
        {
            registration->ProtectedEquipment[slot].Clear();
            handler->PSendSysMessage(
                "Removed protection from {}'s {} slot.", companion->GetName(),
                CompanionEquipmentSlotName(static_cast<uint8>(slot)));
            return true;
        }

        Item* item = companion->GetItemByPos(
            INVENTORY_SLOT_BAG_0, static_cast<uint8>(slot));
        if (!item)
        {
            handler->SendErrorMessage("There is no item equipped in that slot.");
            return false;
        }
        registration->ProtectedEquipment[slot] = item->GetGUID();
        handler->PSendSysMessage(
            "Protected {} in {}'s {} slot for this companion session.",
            item->GetTemplate()->Name1, companion->GetName(),
            CompanionEquipmentSlotName(static_cast<uint8>(slot)));
        return true;
    }

    static bool HandleCompanionLogisticsCommand(
        ChatHandler* handler, char const* args)
    {
        std::istringstream input(args ? args : "");
        std::string leaderName, companionName;
        uint32 triggerFreeSlots = 0, targetFreeSlots = 0;
        int automatic = -1;
        if (!(input >> leaderName >> companionName >> triggerFreeSlots
              >> targetFreeSlots >> automatic)
            || triggerFreeSlots < 1 || triggerFreeSlots > 20
            || targetFreeSlots < triggerFreeSlots || targetFreeSlots > 40
            || automatic < 0 || automatic > 1)
        {
            handler->SendErrorMessage(
                "Usage: webadmin companion logistics <leader> <companion> <trigger 1-20> <target trigger-40> <auto 0|1> [category recipient keep]...");
            return false;
        }

        std::vector<CompanionLogisticsRoute> routes;
        std::string category, recipientName;
        uint32 keepQuantity = 0;
        while (input >> category)
        {
            if (!(input >> recipientName >> keepQuantity)
                || !IsCompanionLogisticsCategory(category)
                || keepQuantity > 1000
                || std::any_of(routes.begin(), routes.end(),
                    [&category](CompanionLogisticsRoute const& route)
                    {
                        return route.Category == category;
                    }))
            {
                handler->SendErrorMessage(
                    "Each route must be: <cloth|leather|metal|herbs|enchanting|jewelcrafting|disenchant|meat|elemental|parts|catchall> <recipient> <keep 0-1000>. Catch-all keep must be 0.");
                return false;
            }
            if (category == "catchall" && keepQuantity != 0)
            {
                handler->SendErrorMessage("The catch-all route must use a keep quantity of 0.");
                return false;
            }
            if (!normalizePlayerName(recipientName))
            {
                handler->SendErrorMessage("A logistics recipient was not found.");
                return false;
            }
            ObjectGuid recipientGuid =
                sCharacterCache->GetCharacterGuidByName(recipientName);
            if (recipientGuid.IsEmpty())
            {
                handler->SendErrorMessage("A logistics recipient was not found.");
                return false;
            }
            routes.push_back({
                category, recipientGuid, recipientName, keepQuantity });
        }
        std::stable_sort(
            routes.begin(), routes.end(),
            [](CompanionLogisticsRoute const& left,
                CompanionLogisticsRoute const& right)
            {
                return left.Category != "catchall"
                    && right.Category == "catchall";
            });

        std::string pairArguments = leaderName + " " + companionName;
        Player* leader = nullptr;
        ObjectGuid companionGuid;
        if (!ParseCompanionArguments(
                handler, pairArguments.c_str(), leader, companionName,
                companionGuid))
            return false;
        Player* companion = nullptr;
        PlayerbotAI* companionAI = nullptr;
        if (!RequireActiveCompanion(
                handler, leader, companionGuid, companion, companionAI))
            return false;
        for (CompanionLogisticsRoute const& route : routes)
        {
            if (route.RecipientGuid == companionGuid
                || sCharacterCache->GetCharacterTeamByGuid(route.RecipientGuid)
                    != companion->GetTeamId())
            {
                handler->SendErrorMessage(
                    "Recipients must be different, same-faction characters.");
                return false;
            }
        }

        QuestingCompanionRegistration* registration =
            FindCompanionRegistration(companionGuid);
        registration->LogisticsTriggerFreeSlots = triggerFreeSlots;
        registration->LogisticsTargetFreeSlots = targetFreeSlots;
        registration->AutomaticLogistics = automatic != 0;
        registration->LogisticsRoutes = std::move(routes);
        registration->LogisticsCheckTimer = 0;
        registration->LogisticsStatus = registration->LogisticsRoutes.empty()
            ? "No bag routes are configured."
            : "Bag routes configured; waiting for bag pressure or a manual run.";
        handler->PSendSysMessage(
            "Configured {} logistics route(s) for {}.",
            registration->LogisticsRoutes.size(), companion->GetName());
        return true;
    }

    static bool HandleCompanionLogisticsRunCommand(
        ChatHandler* handler, char const* args)
    {
        Player* leader = nullptr;
        std::string companionName;
        ObjectGuid companionGuid;
        if (!ParseCompanionArguments(
                handler, args, leader, companionName, companionGuid))
            return false;
        Player* companion = nullptr;
        PlayerbotAI* companionAI = nullptr;
        if (!RequireActiveCompanion(
                handler, leader, companionGuid, companion, companionAI))
            return false;
        QuestingCompanionRegistration* registration =
            FindCompanionRegistration(companionGuid);
        ProcessCompanionLogistics(*registration, companion, true);
        handler->PSendSysMessage(
            "{}: {}", companion->GetName(), registration->LogisticsStatus);
        return true;
    }

    static bool HandleCompanionLogisticsPreviewCommand(
        ChatHandler* handler, char const* args)
    {
        std::istringstream input(args ? args : "");
        std::string leaderName, companionName;
        if (!(input >> leaderName >> companionName))
        {
            handler->SendErrorMessage(
                "Usage: webadmin companion logistics-preview <leader> <companion> [category recipient keep]...");
            return false;
        }
        std::vector<CompanionLogisticsRoute> routes;
        std::string category, recipientName;
        uint32 keepQuantity = 0;
        while (input >> category)
        {
            if (!(input >> recipientName >> keepQuantity)
                || !IsCompanionLogisticsCategory(category)
                || keepQuantity > 1000
                || (category == "catchall" && keepQuantity != 0)
                || std::any_of(routes.begin(), routes.end(),
                    [&category](CompanionLogisticsRoute const& route)
                    {
                        return route.Category == category;
                    })
                || !normalizePlayerName(recipientName))
            {
                handler->SendErrorMessage(
                    "Each preview route must be: <category> <recipient> <keep 0-1000>. Catch-all keep must be 0.");
                return false;
            }
            ObjectGuid recipientGuid =
                sCharacterCache->GetCharacterGuidByName(recipientName);
            if (recipientGuid.IsEmpty())
            {
                handler->SendErrorMessage("A logistics recipient was not found.");
                return false;
            }
            routes.push_back({
                category, recipientGuid, recipientName, keepQuantity });
        }
        std::stable_sort(
            routes.begin(), routes.end(),
            [](CompanionLogisticsRoute const& left,
                CompanionLogisticsRoute const& right)
            {
                return left.Category != "catchall"
                    && right.Category == "catchall";
            });

        Player* leader = nullptr;
        ObjectGuid companionGuid;
        std::string pairArguments = leaderName + " " + companionName;
        if (!ParseCompanionArguments(
                handler, pairArguments.c_str(), leader, companionName,
                companionGuid))
            return false;
        Player* companion = nullptr;
        PlayerbotAI* companionAI = nullptr;
        if (!RequireActiveCompanion(
                handler, leader, companionGuid, companion, companionAI))
            return false;
        QuestingCompanionRegistration* registration =
            FindCompanionRegistration(companionGuid);
        for (CompanionLogisticsRoute const& route : routes)
        {
            if (route.RecipientGuid == companionGuid
                || sCharacterCache->GetCharacterTeamByGuid(route.RecipientGuid)
                    != companion->GetTeamId())
            {
                handler->SendErrorMessage(
                    "Recipients must be different, same-faction characters.");
                return false;
            }
        }
        if (routes.empty())
            routes = registration->LogisticsRoutes;
        ReportCompanionLogisticsPreview(
            handler, companion, routes, registration->AutoSellTrash,
            registration->LogisticsTargetFreeSlots);
        return true;
    }

    static bool HandleCompanionInspectCommand(ChatHandler* handler, char const* args)
    {
        Player* leader = ParseLeader(handler, args);
        if (!leader) return false;
        if (WorldSession* session = handler->GetSession();
            session && session->GetPlayer() != leader)
        {
            handler->SendErrorMessage(
                "Players may inspect only their own questing companions.");
            return false;
        }
        handler->PSendSysMessage(
            "WEBADMIN_COMPANION_PROTOCOL\t{}", CompanionProtocolVersion);
        PlayerbotMgr* manager =
            PlayerbotsMgr::instance().GetPlayerbotMgr(leader);
        if (!manager)
        {
            handler->SendErrorMessage("PlayerBots is not available for the leader.");
            return false;
        }
        ReportCompanionQuestProgress(handler, leader);
        unsigned count = 0;
        for (auto iterator = manager->GetPlayerBotsBegin();
             iterator != manager->GetPlayerBotsEnd(); ++iterator)
        {
            Player* bot = iterator->second;
            if (!bot || sRandomPlayerbotMgr.IsRandomBot(bot)) continue;
            RegisterQuestingCompanion(leader->GetGUID(), bot->GetGUID());
            PlayerbotAI* companionAI =
                PlayerbotsMgr::instance().GetPlayerbotAI(bot);
            QuestingCompanionRegistration* registration =
                FindCompanionRegistration(bot->GetGUID());
            if (registration)
            {
                EnforceCompanionEquipmentProtection(*registration, bot);
                TrackCompanionEquipment(*registration, bot);
                TrackCompanionInventory(*registration, bot);
            }
            handler->PSendSysMessage(
                "WEBADMIN_COMPANION\t{}\t{}\t{}\t{}\t{}\t{}\t{}",
                bot->GetName(), bot->GetLevel(), bot->getClass(),
                bot->GetGroup() == leader->GetGroup() ? 1 : 0,
                HasCompanionLootStrategy(companionAI) ? 1 : 0,
                bot->GetFreeInventorySpace(),
                GetCompanionInventoryCapacity(bot));
            if (registration)
            {
                handler->PSendSysMessage(
                    "WEBADMIN_COMPANION_GATHER\t{}\t{}",
                    bot->GetName(),
                    CompanionProtocolText(registration->QuestObjectStatus));
                handler->PSendSysMessage(
                    "WEBADMIN_COMPANION_LOGISTICS\t{}\t{}\t{}\t{}\t{}\t{}",
                    bot->GetName(), registration->LogisticsTriggerFreeSlots,
                    registration->LogisticsTargetFreeSlots,
                    registration->AutomaticLogistics ? 1 : 0,
                    registration->LogisticsRoutes.size(),
                    CompanionProtocolText(registration->LogisticsStatus));
            }
            ReportCompanionInventory(handler, bot, registration);
            ReportCompanionQuestProgress(handler, bot);
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
        uint32 entry = 0, level = 0, despawnMinutes = 0, count = 1;
        float squareSideLength = 0.0f;
        if (!(input >> anchorName >> entry >> level >> despawnMinutes))
        {
            handler->SendErrorMessage(
                "Usage: webadmin creature spawn <anchorPlayer> <creatureId> <level> "
                "<despawnMinutes> [count squareSideLength]");
            return false;
        }
        input >> std::ws;
        if (!input.eof()
            && (!(input >> count >> squareSideLength) || (input >> unexpected)))
        {
            handler->SendErrorMessage(
                "Usage: webadmin creature spawn <anchorPlayer> <creatureId> <level> "
                "<despawnMinutes> [count squareSideLength]");
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
        if (level < 1 || level > 83 || despawnMinutes < 1 || despawnMinutes > 30
            || count < 1 || count > 25 || squareSideLength < 0.0f
            || squareSideLength > 200.0f || (count > 1 && squareSideLength < 1.0f))
        {
            handler->SendErrorMessage(
                "Creature level must be 1-83, despawn time 1-30 minutes, count 1-25, "
                "and square side length 1-200 metres for multiple creatures.");
            return false;
        }
        Map* map = anchor->GetMap();
        if (!map || map->IsBattlegroundOrArena() || (!isUtilityNpc && map->IsDungeon()) || anchor->GetTransport()
            || anchor->IsInFlight() || anchor->IsInCombat() || anchor->IsBeingTeleported())
        {
            handler->SendErrorMessage("The anchor must be stationary, out of combat, outdoors, and outside instances and transports.");
            return false;
        }

        struct ActiveWebSpawn
        {
            ObjectGuid AnchorGuid;
            ObjectGuid CreatureGuid;
            bool UtilityNpc;
        };
        static std::vector<ActiveWebSpawn> activeSpawns;
        std::erase_if(activeSpawns, [anchor](auto const& spawn)
        {
            return spawn.AnchorGuid == anchor->GetGUID()
                && !ObjectAccessor::GetCreature(*anchor, spawn.CreatureGuid);
        });
        uint32 const activeForType = uint32(std::ranges::count_if(
            activeSpawns, [anchor, isUtilityNpc](auto const& spawn)
            {
                return spawn.AnchorGuid == anchor->GetGUID()
                    && spawn.UtilityNpc == isUtilityNpc;
            }));
        uint32 const maximumActive = isUtilityNpc ? 3 : 25;
        if (count > maximumActive - std::min(activeForType, maximumActive))
        {
            handler->SendErrorMessage(
                "That player can have at most {} active {} web-spawned nearby; {} are already active.",
                maximumActive, isUtilityNpc ? "utility NPCs" : "creatures", activeForType);
            return false;
        }

        uint32 spawnedCount = 0;
        for (uint32 index = 0; index < count; ++index)
        {
            Position position;
            if (squareSideLength > 0.0f)
            {
                float const halfSide = squareSideLength / 2.0f;
                float const destinationX = anchor->GetPositionX() + frand(-halfSide, halfSide);
                float const destinationY = anchor->GetPositionY() + frand(-halfSide, halfSide);
                position = anchor->GetFirstCollisionPosition(
                    anchor->GetPositionX(), anchor->GetPositionY(), anchor->GetPositionZ(),
                    destinationX, destinationY);
            }
            else
                position = anchor->GetFirstCollisionPosition(5.0f, 0.0f);

            TempSummon* creature = anchor->SummonCreature(
                entry, position, TEMPSUMMON_TIMED_OR_DEAD_DESPAWN,
                despawnMinutes * MINUTE * IN_MILLISECONDS);
            if (!creature)
                continue;

            activeSpawns.push_back({ anchor->GetGUID(), creature->GetGUID(), isUtilityNpc });
            creature->SetLevel(level);
            CreatureBaseStats const* stats =
                sObjectMgr->GetCreatureBaseStats(level, creatureTemplate->unit_class);
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
            creature->SetStatFlatModifier(
                UNIT_MOD_ATTACK_POWER_RANGED, BASE_VALUE, stats->RangedAttackPower);
            creature->UpdateAllStats();
            creature->SetMaxHealth(health);
            creature->SetHealth(health);
            ++spawnedCount;
        }

        if (spawnedCount == 0)
        {
            handler->SendErrorMessage(
                "AzerothCore could not create a temporary creature in that area.");
            return false;
        }
        if (squareSideLength > 0.0f)
            handler->PSendSysMessage(
                "Spawned {} of {} {} creatures (entry {}, level {}) in a {} by {} metre square "
                "centred on {} for up to {} minutes. Tameable: {}. Exotic: {}.",
                spawnedCount, count, creatureTemplate->Name, entry, level,
                squareSideLength, squareSideLength, anchor->GetName(), despawnMinutes,
                creatureTemplate->IsTameable(true) ? "Yes" : "No",
                creatureTemplate->IsExotic() ? "Yes" : "No");
        else
            handler->PSendSysMessage(
                "Spawned {} (entry {}, level {}) beside {} for up to {} minutes. "
                "Tameable: {}. Exotic: {}.",
                creatureTemplate->Name, entry, level, anchor->GetName(), despawnMinutes,
                creatureTemplate->IsTameable(true) ? "Yes" : "No",
                creatureTemplate->IsExotic() ? "Yes" : "No");
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

class WebAdminCompanionPlayerScript final : public PlayerScript
{
public:
    WebAdminCompanionPlayerScript()
        : PlayerScript("WebAdminCompanionPlayerScript", {
            PLAYERHOOK_ON_AFTER_UPDATE,
            PLAYERHOOK_ON_LOGOUT,
            PLAYERHOOK_ON_PLAYER_QUEST_ACCEPT
        }) { }

    void OnPlayerAfterUpdate(Player* player, uint32 diff) override
    {
        if (!player || !player->GetSession() || player->GetSession()->IsBot())
            return;

        UpdateWarlockDualDemon(player, diff);

        for (QuestingCompanionRegistration& registration : QuestingCompanions)
        {
            if (registration.LeaderGuid != player->GetGUID())
                continue;

            Player* companion =
                ObjectAccessor::FindConnectedPlayer(registration.CompanionGuid);
            if (!IsActiveQuestingCompanion(player, companion))
                continue;

            UpdateWarlockDualDemon(companion, diff);

            if (!player->IsAlive())
            {
                registration.LeaderWasDead = true;
                continue;
            }

            bool const recoveredFromLeaderDeath = registration.LeaderWasDead;
            registration.LeaderWasDead = false;
            if (registration.InitialSyncPending || recoveredFromLeaderDeath)
                ConfigureCompanionActivity(registration, companion);

            if (registration.InitialSyncPending)
            {
                registration.InitialSyncPending = false;
                SyncLeaderQuestLog(player, companion);
            }

            EnforceCompanionEquipmentProtection(registration, companion);
            TrackCompanionEquipment(registration, companion);
            TrackCompanionInventory(registration, companion);
            UpdateCompanionCombatFocus(registration, player, companion);

            if (registration.BagEquipCheckTimer > diff)
            {
                registration.BagEquipCheckTimer -= diff;
            }
            else
            {
                registration.BagEquipCheckTimer = 5000;
                EquipOneCompanionBag(companion);
            }

            if (registration.SpecialQuestCheckTimer > diff)
            {
                registration.SpecialQuestCheckTimer -= diff;
            }
            else
            {
                registration.SpecialQuestCheckTimer = 2000;
                AcceptNearbyCompanionSpecialQuests(player, companion);
            }

            if (registration.QuestObjectCheckTimer > diff)
            {
                registration.QuestObjectCheckTimer -= diff;
            }
            else
            {
                registration.QuestObjectCheckTimer = 1000;
                CollectCompanionQuestObject(registration, companion);
            }

            if (registration.TrashSellCheckTimer > diff)
            {
                registration.TrashSellCheckTimer -= diff;
            }
            else
            {
                registration.TrashSellCheckTimer = 2000;
                MaintainCompanionAtVendor(registration, player, companion);
            }

            if (registration.ProfessionTrainingCheckTimer > diff)
            {
                registration.ProfessionTrainingCheckTimer -= diff;
            }
            else
            {
                registration.ProfessionTrainingCheckTimer = 10000;
                TrainCompanionAtProfessionTrainer(player, companion);
            }

            if (registration.LogisticsCheckTimer > diff)
            {
                registration.LogisticsCheckTimer -= diff;
            }
            else
            {
                registration.LogisticsCheckTimer = 10000;
                ProcessCompanionLogistics(registration, companion, false);
            }
        }
    }

    void OnPlayerQuestAccept(Player* player, Quest const* quest) override
    {
        if (!player || !player->GetSession() || player->GetSession()->IsBot())
            return;

        for (QuestingCompanionRegistration const& registration : QuestingCompanions)
        {
            if (registration.LeaderGuid != player->GetGUID())
                continue;

            Player* companion =
                ObjectAccessor::FindConnectedPlayer(registration.CompanionGuid);
            if (IsActiveQuestingCompanion(player, companion))
                AcceptCompanionQuest(player, companion, quest, player, true);
        }
    }

    void OnPlayerLogout(Player* player) override
    {
        if (!player)
            return;

        ObjectGuid playerGuid = player->GetGUID();
        auto dualDemon = WarlockDualDemons.find(playerGuid);
        if (dualDemon != WarlockDualDemons.end())
        {
            DespawnWarlockDualDemon(player, dualDemon->second);
            WarlockDualDemons.erase(dualDemon);
        }
        QuestingCompanions.erase(
            std::remove_if(
                QuestingCompanions.begin(), QuestingCompanions.end(),
                [playerGuid](QuestingCompanionRegistration const& registration)
                {
                    return registration.LeaderGuid == playerGuid
                        || registration.CompanionGuid == playerGuid;
                }),
            QuestingCompanions.end());
    }
};

class WebAdminCompanionCreatureScript final : public AllCreatureScript
{
public:
    WebAdminCompanionCreatureScript()
        : AllCreatureScript("WebAdminCompanionCreatureScript") { }

    bool CanCreatureGossipHello(Player* player, Creature* creature) override
    {
        AcceptCompanionSpecialQuests(
            player, creature,
            sObjectMgr->GetCreatureQuestRelationBounds(creature->GetEntry()));
        return false;
    }
};

class WebAdminCompanionGameObjectScript final : public AllGameObjectScript
{
public:
    WebAdminCompanionGameObjectScript()
        : AllGameObjectScript("WebAdminCompanionGameObjectScript") { }

    bool CanGameObjectGossipHello(Player* player, GameObject* gameObject) override
    {
        AcceptCompanionSpecialQuests(
            player, gameObject,
            sObjectMgr->GetGOQuestRelationBounds(gameObject->GetEntry()));
        return false;
    }
};
}

void Addmod_web_adminScripts()
{
    new WebAdminCommandScript();
    new WebAdminCompanionPlayerScript();
    new WebAdminCompanionCreatureScript();
    new WebAdminCompanionGameObjectScript();
}
