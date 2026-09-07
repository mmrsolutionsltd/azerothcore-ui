/*
 * This file is part of the AzerothCore Project. See AUTHORS file for Copyright information
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful, but WITHOUT
 * ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
 * FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
 * more details.
 *
 * You should have received a copy of the GNU General Public License along
 * with this program. If not, see <http://www.gnu.org/licenses/>.
 */

#include "gtest/gtest.h"
#include <cstdint>
#include <vector>

/**
 * Tests for the pure decision rules behind server-modules/mod-web-admin's generic
 * "quest source item at a gameobject" companion support (mod_web_admin.cpp:
 * ItemProducesQuestItem, FindCompanionSpellFocusObject, CollectCompanionQuestSourceItem).
 *
 * That code needs a live Player/Item/GameObject/AiObjectContext and cannot be
 * exercised directly here, so - mirroring the existing convention in
 * MountTeachSpellResolutionTest.cpp and CanUseItemMountRaceExemptionTest.cpp - each
 * decision rule is restated over plain data instead of world objects:
 *
 *   1. Does a carried item's use-spell produce the exact item a quest still needs?
 *      (SPELL_EFFECT_CREATE_ITEM / SPELL_EFFECT_CREATE_ITEM_2 effect, ItemType match)
 *   2. Does a nearby gameobject satisfy a spell's RequiresSpellFocus requirement?
 *      (GAMEOBJECT_TYPE_SPELL_FOCUS + matching spellFocus.focusId)
 *   3. Is the companion close enough to use the item now, or does it need to move
 *      first? (same INTERACTION_DISTANCE - 2.0f threshold CollectCompanionQuestObject
 *      already uses for lootable quest objects)
 *
 * This is deliberately generic - none of these rules name a specific quest, item, or
 * gameobject, matching the "reusable, data-driven" requirement (the Crown of the
 * Earth/Tenaron's Summons scenario is only one instance the rule already covers).
 */

namespace
{
    constexpr int32_t SPELL_EFFECT_CREATE_ITEM = 24;
    constexpr int32_t SPELL_EFFECT_CREATE_ITEM_2 = 157;
    constexpr int32_t SPELL_EFFECT_SCHOOL_DAMAGE = 2;
    constexpr uint32_t GAMEOBJECT_TYPE_SPELL_FOCUS = 8;
    constexpr uint32_t GAMEOBJECT_TYPE_CHEST = 3;
    constexpr float INTERACTION_DISTANCE = 5.0f;

    struct FakeSpellEffect
    {
        int32_t Effect;
        uint32_t ItemType;
    };

    // Mirrors ItemProducesQuestItem's effect scan.
    bool ProducesItem(std::vector<FakeSpellEffect> const& effects, uint32_t producedItemId)
    {
        for (auto const& effect : effects)
            if ((effect.Effect == SPELL_EFFECT_CREATE_ITEM || effect.Effect == SPELL_EFFECT_CREATE_ITEM_2)
                && effect.ItemType == producedItemId)
                return true;
        return false;
    }

    // Mirrors FindCompanionSpellFocusObject's per-candidate filter.
    bool MatchesSpellFocus(uint32_t candidateGoType, uint32_t candidateFocusId, uint32_t requiredFocusId)
    {
        return candidateGoType == GAMEOBJECT_TYPE_SPELL_FOCUS && candidateFocusId == requiredFocusId;
    }

    // Mirrors CollectCompanionQuestSourceItem's move-vs-use branch.
    bool ShouldMoveCloserFirst(float nearestDistance)
    {
        return nearestDistance >= INTERACTION_DISTANCE - 2.0f;
    }
}

TEST(CompanionQuestSourceItem, RecognisesACreateItemEffectProducingTheNeededItem)
{
    // "Filling" (spell 4976): SPELL_EFFECT_CREATE_ITEM, ItemType=5184 (Crown of the Earth).
    std::vector<FakeSpellEffect> fillingEffects = {{SPELL_EFFECT_CREATE_ITEM, 5184}};
    EXPECT_TRUE(ProducesItem(fillingEffects, 5184));
}

TEST(CompanionQuestSourceItem, AlsoRecognisesTheCreateItem2Variant)
{
    std::vector<FakeSpellEffect> effects = {{SPELL_EFFECT_CREATE_ITEM_2, 9001}};
    EXPECT_TRUE(ProducesItem(effects, 9001));
}

TEST(CompanionQuestSourceItem, DoesNotMatchADifferentProducedItem)
{
    std::vector<FakeSpellEffect> fillingEffects = {{SPELL_EFFECT_CREATE_ITEM, 5184}};
    EXPECT_FALSE(ProducesItem(fillingEffects, 1234));
}

TEST(CompanionQuestSourceItem, IgnoresUnrelatedEffectsOnTheSameSpell)
{
    // Guards against consuming/using an unrelated item whose spell happens to also
    // deal damage or have some other effect - only a genuine CREATE_ITEM(_2) effect
    // targeting the exact needed item counts as a match.
    std::vector<FakeSpellEffect> effects = {{SPELL_EFFECT_SCHOOL_DAMAGE, 0}, {SPELL_EFFECT_CREATE_ITEM, 5184}};
    EXPECT_TRUE(ProducesItem(effects, 5184));

    std::vector<FakeSpellEffect> damageOnly = {{SPELL_EFFECT_SCHOOL_DAMAGE, 0}};
    EXPECT_FALSE(ProducesItem(damageOnly, 5184));
}

TEST(CompanionQuestSourceItem, MatchesASpellFocusObjectWithTheSameFocusId)
{
    EXPECT_TRUE(MatchesSpellFocus(GAMEOBJECT_TYPE_SPELL_FOCUS, 42, 42));
}

TEST(CompanionQuestSourceItem, RejectsASpellFocusObjectWithADifferentFocusId)
{
    EXPECT_FALSE(MatchesSpellFocus(GAMEOBJECT_TYPE_SPELL_FOCUS, 7, 42));
}

TEST(CompanionQuestSourceItem, RejectsANonSpellFocusGameobjectEvenIfIdsHappenToMatch)
{
    // A chest or other object must never be treated as a stand-in for the required
    // spell-focus object, even if its data0 field happens to equal the focus id.
    EXPECT_FALSE(MatchesSpellFocus(GAMEOBJECT_TYPE_CHEST, 42, 42));
}

TEST(CompanionQuestSourceItem, MovesCloserWhenBeyondInteractionRange)
{
    EXPECT_TRUE(ShouldMoveCloserFirst(10.0f));
}

TEST(CompanionQuestSourceItem, UsesTheItemImmediatelyWhenAlreadyCloseEnough)
{
    EXPECT_FALSE(ShouldMoveCloserFirst(1.0f));
}
