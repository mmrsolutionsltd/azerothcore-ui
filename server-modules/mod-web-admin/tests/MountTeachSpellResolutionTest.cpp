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
 * Tests for the taught-spell resolution logic in server-modules/mod-web-admin's
 * "webadmin mount teach" command (mod_web_admin.cpp, HandleMountTeachCommand).
 *
 * That command cannot be exercised directly without a fully constructed Player and
 * ItemTemplate (world/DB context), so this mirrors just the pure resolution rule:
 *
 *   uint32 teachSpellId = (proto->Spells[0].SpellId == 483 || proto->Spells[0].SpellId == 55884)
 *       ? proto->Spells[1].SpellId
 *       : proto->Spells[0].SpellId;
 *
 * This is the same redirect Player::CastItemUseSpell uses for its own "special
 * learning case" (PlayerStorage.cpp) - many mount items route through a generic
 * "teach a spell chosen by the caller" trigger spell (483 or 55884) whose actual
 * target spell is stored in the item's second spell slot; a mount item not using
 * that generic redirect casts its teach spell directly from the first slot instead.
 */

namespace
{
    uint32_t ResolveTeachSpellId(uint32_t spell0, uint32_t spell1)
    {
        return (spell0 == 483 || spell0 == 55884) ? spell1 : spell0;
    }
}

TEST(MountTeachSpellResolution, RedirectsThroughTheGenericTeachSpell55884)
{
    // Brown Horse Bridle (item 5656): Spells[0]=55884, Spells[1]=458 (the real spell).
    EXPECT_EQ(458u, ResolveTeachSpellId(55884, 458));
}

TEST(MountTeachSpellResolution, RedirectsThroughTheGenericTeachSpell483)
{
    EXPECT_EQ(999u, ResolveTeachSpellId(483, 999));
}

TEST(MountTeachSpellResolution, UsesTheFirstSlotDirectlyWhenNotUsingTheGenericRedirect)
{
    // A mount item whose first spell slot directly casts the mount-learn spell.
    EXPECT_EQ(12345u, ResolveTeachSpellId(12345, 0));
}

TEST(MountTeachSpellResolution, ReturnsZeroWhenNeitherSlotResolvesToAUsableSpell)
{
    EXPECT_EQ(0u, ResolveTeachSpellId(55884, 0));
}
