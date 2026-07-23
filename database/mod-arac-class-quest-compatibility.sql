-- mod-arac class quest compatibility for AzerothCore 3.3.5a
--
-- Purpose
-- -------
-- mod-arac permits every playable race/class combination and installs shared
-- class trainers (creature entries 26324-26332) in starting and capital areas.
-- Those shared trainers teach normal trainer spells but have no class-quest
-- starter relationships. Characters using non-standard race/class combinations
-- can therefore miss quest-only mechanics such as pets, forms, totems, demons,
-- Redemption, poisons, and Warrior stances.
--
-- This migration gives the shared trainers one coherent Alliance or Horde route
-- for each quest-only mechanic. Starter quests are restricted to the normal
-- faction race masks so a shared trainer does not show both faction variants:
--
--   Alliance: 1101 (Human, Dwarf, Night Elf, Gnome, Draenei)
--   Horde:     690  (Orc, Undead, Tauren, Troll, Blood Elf)
--
-- The migration is idempotent. UPDATE statements set deterministic values and
-- INSERT IGNORE prevents duplicate creature_queststarter rows.
--
-- Tested against the PlayerBots revision whose generic trainers are:
--   26324 Druid, 26325 Hunter, 26327 Paladin, 26329 Rogue,
--   26330 Shaman, 26331 Warlock, 26332 Warrior.

USE `acore_world`;

START TRANSACTION;

-- Restrict only the selected entry quests to the appropriate faction. Follow-up
-- quests remain available to all races as installed by mod-arac.
UPDATE `quest_template`
SET `AllowableRaces` = 1101
WHERE `ID` IN (
    26,    -- Druid: A Lesson to Learn (Aquatic Form, Alliance)
    1641,  -- Paladin: The Tome of Divinity (Redemption, Alliance)
    1661,  -- Paladin: The Tome of Nobility (Warhorse, Alliance)
    1638,  -- Warrior: A Warrior's Training (Defensive Stance, Alliance)
    1685,  -- Warlock: Gakin's Summons (Voidwalker, Alliance)
    1717,  -- Warlock: Gakin's Summons (Succubus, Alliance)
    2360,  -- Rogue: Mathias and the Defias (Poisons, Alliance)
    5921,  -- Druid: Moonglade (Bear Form, Alliance)
    6074,  -- Hunter: The Hunter's Path (Dun Morogh chain, Alliance)
    6121,  -- Druid: Lessons Anew (Cure Poison, Alliance)
    9449,  -- Shaman: Call of Earth (Draenei/Alliance)
    9462,  -- Shaman: Call of Fire (Draenei/Alliance)
    9500,  -- Shaman: Call of Water (Draenei/Alliance)
    9547   -- Shaman: Call of Air (Draenei/Alliance)
);

UPDATE `quest_template`
SET `AllowableRaces` = 690
WHERE `ID` IN (
    27,    -- Druid: A Lesson to Learn (Aquatic Form, Horde)
    1505,  -- Warrior: Veteran Uzzek (Defensive Stance, Horde)
    1506,  -- Warlock: Gan'rul's Summons (Voidwalker, Horde)
    1507,  -- Warlock: Devourer of Souls (Succubus, Horde)
    1516,  -- Shaman: Call of Earth (Horde)
    1522,  -- Shaman: Call of Fire (Horde)
    1528,  -- Shaman: Call of Water (Horde)
    1531,  -- Shaman: Call of Air (Horde)
    2460,  -- Rogue: The Shattered Salute (Poisons, Horde)
    3001,  -- Warlock: Seeking Strahad (Felhunter, Horde)
    5922,  -- Druid: Moonglade (Bear Form, Horde)
    6070,  -- Hunter: The Hunter's Path (Durotar chain, Horde)
    6126,  -- Druid: Lessons Anew (Cure Poison, Horde)
    9677,  -- Paladin: Summons from Knight-Lord Bloodvalor (Redemption, Horde)
    9712   -- Paladin: The Thalassian Warhorse (Horde)
);

-- Warrior: faction-specific Defensive Stance and shared Berserker Stance route.
INSERT IGNORE INTO `creature_queststarter` (`id`, `quest`) VALUES
    (26332, 1638),
    (26332, 1505),
    (26332, 1718);

-- Paladin: Redemption and level-40 Warhorse routes.
INSERT IGNORE INTO `creature_queststarter` (`id`, `quest`) VALUES
    (26327, 1641),
    (26327, 9677),
    (26327, 1661),
    (26327, 9712);

-- Hunter: route Alliance players to the Dun Morogh chain and Horde players to
-- the Durotar chain. Both chains ultimately grant Tame Beast and pet controls.
INSERT IGNORE INTO `creature_queststarter` (`id`, `quest`) VALUES
    (26325, 6074),
    (26325, 6070);

-- Druid: Bear Form, Cure Poison, Aquatic Form, and the race-neutral level-70
-- Swift Flight/Raven God starter. Moonglade remains part of the intended chains.
INSERT IGNORE INTO `creature_queststarter` (`id`, `quest`) VALUES
    (26324, 5921),
    (26324, 5922),
    (26324, 6121),
    (26324, 6126),
    (26324, 26),
    (26324, 27),
    (26324, 10955);

-- Rogue: faction-specific routes to the level-20 Poisons unlock.
INSERT IGNORE INTO `creature_queststarter` (`id`, `quest`) VALUES
    (26329, 2360),
    (26329, 2460);

-- Shaman: faction-specific Earth, Fire, Water, and Air totem chains.
INSERT IGNORE INTO `creature_queststarter` (`id`, `quest`) VALUES
    (26330, 9449),
    (26330, 1516),
    (26330, 9462),
    (26330, 1522),
    (26330, 9500),
    (26330, 1528),
    (26330, 9547),
    (26330, 1531);

-- Warlock: faction-specific Voidwalker and Succubus routes, faction-specific
-- Felhunter breadcrumbs, and the shared level-40 Felsteed route.
UPDATE `quest_template`
SET `AllowableRaces` = 1101
WHERE `ID` = 1798; -- Seeking Strahad (Alliance)

INSERT IGNORE INTO `creature_queststarter` (`id`, `quest`) VALUES
    (26331, 1685),
    (26331, 1506),
    (26331, 1717),
    (26331, 1507),
    (26331, 1798),
    (26331, 3001),
    (26331, 3631);

COMMIT;

-- Verification
-- ------------
-- Expected relationship counts after applying this migration:
-- Druid 7, Hunter 2, Paladin 4, Rogue 2, Shaman 8, Warlock 7, Warrior 3.
SELECT
    `starter`.`id` AS `TrainerEntry`,
    `trainer`.`name` AS `TrainerName`,
    COUNT(*) AS `ClassQuestStarters`
FROM `creature_queststarter` AS `starter`
INNER JOIN `creature_template` AS `trainer` ON `trainer`.`entry` = `starter`.`id`
WHERE `starter`.`id` IN (26324, 26325, 26327, 26329, 26330, 26331, 26332)
GROUP BY `starter`.`id`, `trainer`.`name`
ORDER BY `starter`.`id`;

SELECT
    `starter`.`id` AS `TrainerEntry`,
    `starter`.`quest` AS `QuestID`,
    `quest`.`AllowableRaces`,
    `quest`.`LogTitle`
FROM `creature_queststarter` AS `starter`
INNER JOIN `quest_template` AS `quest` ON `quest`.`ID` = `starter`.`quest`
WHERE `starter`.`id` IN (26324, 26325, 26327, 26329, 26330, 26331, 26332)
ORDER BY `starter`.`id`, `quest`.`MinLevel`, `starter`.`quest`;

-- Rollback (run manually only if this migration must be removed)
-- ---------------------------------------------------------------------------
-- START TRANSACTION;
-- DELETE FROM `creature_queststarter`
-- WHERE (`id`, `quest`) IN (
--     ROW(26332,1638),ROW(26332,1505),ROW(26332,1718),
--     ROW(26327,1641),ROW(26327,9677),ROW(26327,1661),ROW(26327,9712),
--     ROW(26325,6074),ROW(26325,6070),
--     ROW(26324,5921),ROW(26324,5922),ROW(26324,6121),ROW(26324,6126),
--     ROW(26324,26),ROW(26324,27),ROW(26324,10955),
--     ROW(26329,2360),ROW(26329,2460),
--     ROW(26330,9449),ROW(26330,1516),ROW(26330,9462),ROW(26330,1522),
--     ROW(26330,9500),ROW(26330,1528),ROW(26330,9547),ROW(26330,1531),
--     ROW(26331,1685),ROW(26331,1506),ROW(26331,1717),ROW(26331,1507),
--     ROW(26331,1798),ROW(26331,3001),ROW(26331,3631)
-- );
-- UPDATE `quest_template` SET `AllowableRaces` = 1791
-- WHERE `ID` IN (
--     26,27,1505,1506,1507,1516,1522,1528,1531,1638,1641,1661,1685,1717,
--     1798,2360,2460,3001,5921,5922,6070,6074,6121,6126,9449,9462,9500,
--     9547,9677,9712
-- );
-- COMMIT;
