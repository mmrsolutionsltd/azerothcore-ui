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

/**
 * Tests for the AllowableRace exemption added to Player::CanUseItem(ItemTemplate const*)
 * for mount items/spells (PlayerStorage.cpp).
 *
 * Player::CanUseItem cannot be exercised directly without a fully constructed Player
 * (world/DB context, race/class masks resolved from character data), so this mirrors
 * just the boolean predicate and the combined gate condition added to that function:
 *
 *   bool const isMountItem = proto->Class == ITEM_CLASS_MISC
 *       && proto->SubClass == ITEM_SUBCLASS_JUNK_MOUNT;
 *   if ((proto->AllowableClass & getClassMask()) == 0
 *       || (!isMountItem && (proto->AllowableRace & getRaceMask()) == 0))
 *       return EQUIP_ERR_YOU_CAN_NEVER_USE_THAT_ITEM;
 *
 * ITEM_CLASS_MISC = 15, ITEM_SUBCLASS_JUNK_MOUNT = 5 (ItemTemplate.h) - the same
 * class/subclass pair AzerothCore-UI's own mount catalogue query filters on.
 */

namespace
{
    constexpr uint32_t ITEM_CLASS_MISC = 15;
    constexpr uint32_t ITEM_SUBCLASS_JUNK_MOUNT = 5;

    bool IsMountItem(uint32_t itemClass, uint32_t itemSubClass)
    {
        return itemClass == ITEM_CLASS_MISC && itemSubClass == ITEM_SUBCLASS_JUNK_MOUNT;
    }

    // Mirrors the patched condition in Player::CanUseItem(ItemTemplate const*):
    // returns true when the item would be REJECTED (EQUIP_ERR_YOU_CAN_NEVER_USE_THAT_ITEM).
    bool IsRejected(
        uint32_t allowableClass, uint32_t classMask,
        uint32_t allowableRace, uint32_t raceMask,
        uint32_t itemClass, uint32_t itemSubClass)
    {
        bool const isMountItem = IsMountItem(itemClass, itemSubClass);
        return (allowableClass & classMask) == 0
            || (!isMountItem && (allowableRace & raceMask) == 0);
    }
}

TEST(CanUseItemMountRaceExemption, MountItemBypassesAMismatchedRaceMask)
{
    // Alliance-only mount (AllowableRace bit for Human/1 only), Horde character (Orc/2).
    uint32_t const allianceOnlyRaceMask = 1u << (1 - 1);
    uint32_t const orcRaceMask = 1u << (2 - 1);
    EXPECT_FALSE(IsRejected(
        /*allowableClass*/ 0xFFFFFFFFu, /*classMask*/ 1u << (1 - 1),
        allianceOnlyRaceMask, orcRaceMask,
        ITEM_CLASS_MISC, ITEM_SUBCLASS_JUNK_MOUNT));
}

TEST(CanUseItemMountRaceExemption, MountItemStillEnforcesAllowableClass)
{
    // Warrior-only mount (class bit 1), Mage (class bit 8).
    uint32_t const warriorOnlyClassMask = 1u << (1 - 1);
    uint32_t const mageClassMask = 1u << (8 - 1);
    EXPECT_TRUE(IsRejected(
        warriorOnlyClassMask, mageClassMask,
        /*allowableRace*/ 0xFFFFFFFFu, /*raceMask*/ 0xFFFFFFFFu,
        ITEM_CLASS_MISC, ITEM_SUBCLASS_JUNK_MOUNT));
}

TEST(CanUseItemMountRaceExemption, NonMountItemIsUnaffectedByTheExemption)
{
    // A non-mount item (e.g. plate armor) with a mismatched race mask must still
    // be rejected exactly as before this change.
    uint32_t const allianceOnlyRaceMask = 1u << (1 - 1);
    uint32_t const orcRaceMask = 1u << (2 - 1);
    EXPECT_TRUE(IsRejected(
        /*allowableClass*/ 0xFFFFFFFFu, /*classMask*/ 1u << (1 - 1),
        allianceOnlyRaceMask, orcRaceMask,
        /*itemClass*/ 4u /* ITEM_CLASS_ARMOR */, /*itemSubClass*/ 0u));
}

TEST(CanUseItemMountRaceExemption, MountItemWithMatchingRaceAndClassIsAccepted)
{
    uint32_t const allianceOnlyRaceMask = 1u << (1 - 1);
    EXPECT_FALSE(IsRejected(
        /*allowableClass*/ 0xFFFFFFFFu, /*classMask*/ 1u << (1 - 1),
        allianceOnlyRaceMask, allianceOnlyRaceMask,
        ITEM_CLASS_MISC, ITEM_SUBCLASS_JUNK_MOUNT));
}

TEST(CanUseItemMountRaceExemption, WrongSubClassInItemClassMiscIsNotTreatedAsAMount)
{
    // ITEM_CLASS_MISC covers more than mounts (junk, reagents, holiday, other, mount).
    // Only subclass 5 (JUNK_MOUNT) is exempted - a mismatched-race non-mount misc item
    // (e.g. subclass 0 "Junk") must still be rejected.
    uint32_t const allianceOnlyRaceMask = 1u << (1 - 1);
    uint32_t const orcRaceMask = 1u << (2 - 1);
    EXPECT_TRUE(IsRejected(
        /*allowableClass*/ 0xFFFFFFFFu, /*classMask*/ 1u << (1 - 1),
        allianceOnlyRaceMask, orcRaceMask,
        ITEM_CLASS_MISC, /*itemSubClass*/ 0u));
}
