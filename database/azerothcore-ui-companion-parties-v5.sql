USE `azerothcore_ui`;

CREATE TABLE IF NOT EXISTS `companion_party_session` (
  `leader_guid` INT UNSIGNED NOT NULL,
  `leader_name` VARCHAR(12) NOT NULL,
  `leader_account_id` INT UNSIGNED NOT NULL,
  `started_by_user_id` BIGINT UNSIGNED NULL,
  `started_by_username` VARCHAR(64) NOT NULL,
  `started_at_utc` DATETIME(6) NOT NULL,
  `last_leader_online_at_utc` DATETIME(6) NOT NULL,
  `offline_timeout_minutes` SMALLINT UNSIGNED NOT NULL DEFAULT 5,
  `updated_at_utc` DATETIME(6) NOT NULL,
  PRIMARY KEY (`leader_guid`),
  KEY `ix_companion_party_account` (`leader_account_id`),
  KEY `ix_companion_party_started_by` (`started_by_user_id`),
  CONSTRAINT `ck_companion_party_timeout`
    CHECK (`offline_timeout_minutes` BETWEEN 1 AND 120),
  CONSTRAINT `fk_companion_party_admin_user`
    FOREIGN KEY (`started_by_user_id`) REFERENCES `admin_user` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `companion_party_session_member` (
  `leader_guid` INT UNSIGNED NOT NULL,
  `companion_guid` INT UNSIGNED NOT NULL,
  `companion_name` VARCHAR(12) NOT NULL,
  `added_at_utc` DATETIME(6) NOT NULL,
  PRIMARY KEY (`leader_guid`, `companion_guid`),
  KEY `ix_companion_party_member` (`companion_guid`),
  CONSTRAINT `fk_companion_party_member_session`
    FOREIGN KEY (`leader_guid`) REFERENCES `companion_party_session` (`leader_guid`) ON DELETE CASCADE
) ENGINE=InnoDB;

INSERT INTO `ui_schema_version` (`version`, `applied_at_utc`)
VALUES (5, UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE `applied_at_utc` = VALUES(`applied_at_utc`);
