-- Add every character belonging to the four real-player accounts to Workshy.
-- Warblelf remains Guild Master (rank 0); all others become Officers (rank 1).
START TRANSACTION;

INSERT INTO acore_characters.guild_member
    (guildid, guid, `rank`, pnote, offnote)
SELECT
    21,
    c.guid,
    IF(c.guid = 2513, 0, 1),
    '',
    ''
FROM acore_characters.characters c
WHERE c.account IN (102, 103, 104, 105)
ON DUPLICATE KEY UPDATE
    guildid = VALUES(guildid),
    `rank` = VALUES(`rank`);

-- Full general guild permissions and unlimited guild-money withdrawal.
UPDATE acore_characters.guild_rank
SET rights = 1962495,
    BankMoneyPerDay = 4294967295
WHERE guildid = 21
  AND rid = 1;

-- Full view/deposit/withdraw access on every currently purchased bank tab.
INSERT INTO acore_characters.guild_bank_right
    (guildid, TabId, rid, gbright, SlotPerDay)
SELECT
    21,
    tab.TabId,
    1,
    255,
    4294967295
FROM acore_characters.guild_bank_tab tab
WHERE tab.guildid = 21
ON DUPLICATE KEY UPDATE
    gbright = VALUES(gbright),
    SlotPerDay = VALUES(SlotPerDay);

COMMIT;
