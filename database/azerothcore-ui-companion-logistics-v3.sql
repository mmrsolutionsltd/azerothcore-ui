USE `azerothcore_ui`;

CREATE TABLE IF NOT EXISTS `companion_logistics_profile` (
  `companion_guid` INT UNSIGNED NOT NULL,
  `trigger_free_slots` TINYINT UNSIGNED NOT NULL DEFAULT 4,
  `target_free_slots` TINYINT UNSIGNED NOT NULL DEFAULT 8,
  `automatic_enabled` TINYINT(1) NOT NULL DEFAULT 0,
  `updated_at_utc` DATETIME(6) NOT NULL,
  PRIMARY KEY (`companion_guid`),
  CONSTRAINT `ck_companion_logistics_thresholds`
    CHECK (`trigger_free_slots` BETWEEN 1 AND 20
      AND `target_free_slots` BETWEEN `trigger_free_slots` AND 40)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `companion_logistics_route` (
  `companion_guid` INT UNSIGNED NOT NULL,
  `category_key` VARCHAR(24) NOT NULL,
  `recipient_guid` INT UNSIGNED NOT NULL,
  `keep_quantity` SMALLINT UNSIGNED NOT NULL DEFAULT 0,
  `enabled` TINYINT(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`companion_guid`, `category_key`),
  KEY `ix_companion_logistics_recipient` (`recipient_guid`),
  CONSTRAINT `fk_companion_logistics_route_profile`
    FOREIGN KEY (`companion_guid`)
    REFERENCES `companion_logistics_profile` (`companion_guid`) ON DELETE CASCADE
) ENGINE=InnoDB;

INSERT INTO `ui_schema_version` (`version`, `applied_at_utc`)
VALUES (3, UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE `applied_at_utc` = VALUES(`applied_at_utc`);
