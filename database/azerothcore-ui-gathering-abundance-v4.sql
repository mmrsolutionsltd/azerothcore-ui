USE `azerothcore_ui`;

CREATE TABLE IF NOT EXISTS `gathering_abundance_settings` (
  `id` TINYINT UNSIGNED NOT NULL,
  `herb_abundance_percent` SMALLINT UNSIGNED NOT NULL DEFAULT 100,
  `mining_abundance_percent` SMALLINT UNSIGNED NOT NULL DEFAULT 100,
  `updated_at_utc` DATETIME(6) NOT NULL,
  PRIMARY KEY (`id`),
  CONSTRAINT `ck_gathering_abundance_singleton` CHECK (`id` = 1),
  CONSTRAINT `ck_gathering_herb_percentage`
    CHECK (`herb_abundance_percent` BETWEEN 25 AND 500
      AND MOD(`herb_abundance_percent`, 5) = 0),
  CONSTRAINT `ck_gathering_mining_percentage`
    CHECK (`mining_abundance_percent` BETWEEN 25 AND 500
      AND MOD(`mining_abundance_percent`, 5) = 0)
) ENGINE=InnoDB;

INSERT IGNORE INTO `gathering_abundance_settings`
  (`id`, `herb_abundance_percent`, `mining_abundance_percent`, `updated_at_utc`)
VALUES (1, 100, 100, UTC_TIMESTAMP(6));

CREATE TABLE IF NOT EXISTS `gathering_spawn_baseline` (
  `guid` BIGINT UNSIGNED NOT NULL,
  `gameobject_entry` INT UNSIGNED NOT NULL,
  `category` ENUM('herb', 'mining') NOT NULL,
  `original_spawntimesecs` INT UNSIGNED NOT NULL,
  PRIMARY KEY (`guid`),
  KEY `ix_gathering_spawn_category` (`category`),
  KEY `ix_gathering_spawn_entry` (`gameobject_entry`)
) ENGINE=InnoDB;

INSERT INTO `ui_schema_version` (`version`, `applied_at_utc`)
VALUES (4, UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE `applied_at_utc` = VALUES(`applied_at_utc`);
