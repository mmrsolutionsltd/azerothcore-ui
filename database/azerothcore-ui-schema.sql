CREATE DATABASE IF NOT EXISTS `azerothcore_ui`
  CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;

USE `azerothcore_ui`;

CREATE TABLE IF NOT EXISTS `admin_user` (
  `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `username` VARCHAR(64) NOT NULL,
  `normalized_username` VARCHAR(64) NOT NULL,
  `password_hash` VARCHAR(255) NOT NULL,
  `role` VARCHAR(32) NOT NULL,
  `enabled` TINYINT(1) NOT NULL DEFAULT 1,
  `must_change_password` TINYINT(1) NOT NULL DEFAULT 0,
  `failed_login_count` INT UNSIGNED NOT NULL DEFAULT 0,
  `lockout_until_utc` DATETIME(6) NULL,
  `security_stamp` CHAR(36) NOT NULL,
  `created_at_utc` DATETIME(6) NOT NULL,
  `last_login_at_utc` DATETIME(6) NULL,
  `account_scope` VARCHAR(16) NOT NULL DEFAULT 'All',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_admin_user_normalized_username` (`normalized_username`),
  CONSTRAINT `ck_admin_user_account_scope`
    CHECK (`account_scope` IN ('All', 'Assigned', 'None'))
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `admin_role` (
  `name` VARCHAR(32) NOT NULL,
  `description` VARCHAR(255) NOT NULL,
  `is_system` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`name`)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `admin_permission` (
  `permission_key` VARCHAR(64) NOT NULL,
  `display_name` VARCHAR(100) NOT NULL,
  `category` VARCHAR(32) NOT NULL,
  `description` VARCHAR(255) NOT NULL,
  PRIMARY KEY (`permission_key`)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `admin_role_permission` (
  `role_name` VARCHAR(32) NOT NULL,
  `permission_key` VARCHAR(64) NOT NULL,
  PRIMARY KEY (`role_name`, `permission_key`),
  CONSTRAINT `fk_role_permission_role` FOREIGN KEY (`role_name`)
    REFERENCES `admin_role` (`name`) ON DELETE CASCADE,
  CONSTRAINT `fk_role_permission_permission` FOREIGN KEY (`permission_key`)
    REFERENCES `admin_permission` (`permission_key`) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `admin_user_game_account` (
  `admin_user_id` BIGINT UNSIGNED NOT NULL,
  `game_account_id` INT UNSIGNED NOT NULL,
  PRIMARY KEY (`admin_user_id`, `game_account_id`),
  CONSTRAINT `fk_user_game_account_user` FOREIGN KEY (`admin_user_id`)
    REFERENCES `admin_user` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB;

INSERT INTO `admin_role` (`name`, `description`, `is_system`) VALUES
  ('Owner', 'Unrestricted website and server access.', 1),
  ('Administrator', 'All player and world tools, excluding server infrastructure.', 1)
ON DUPLICATE KEY UPDATE `description` = VALUES(`description`);

INSERT INTO `admin_permission`
  (`permission_key`, `display_name`, `category`, `description`) VALUES
  ('players.accounts', 'Accounts', 'Players', 'View game accounts and their characters.'),
  ('players.characters', 'Characters', 'Players', 'View character details and inventory.'),
  ('players.actions', 'Player actions', 'Players', 'Give items or money, teleport, and alter players.'),
  ('players.services', 'Character services', 'Players', 'Apply character services and starter presets.'),
  ('players.collectibles', 'Collectibles', 'Players', 'Inspect and grant collectibles.'),
  ('adventures.quests', 'Quest helper', 'Adventures', 'Inspect, add, remove, and navigate quests.'),
  ('adventures.dungeons', 'Dungeon assistant', 'Adventures', 'Manage parties and launch dungeons.'),
  ('adventures.training', 'Training', 'Adventures', 'Manage class, weapon, and profession training.'),
  ('world.auction-house', 'Auction House', 'World', 'Inspect and manage the Auction House.'),
  ('world.creatures', 'Creature spawning', 'World', 'Search for and spawn creatures.'),
  ('server.control', 'Server control', 'Server', 'Start, stop, and restart AzerothCore.'),
  ('server.settings', 'Server settings', 'Server', 'Change server and module configuration.'),
  ('server.diagnostics', 'Diagnostics', 'Server', 'View server health and diagnostic reports.'),
  ('server.backups', 'Database backups', 'Server', 'Create, schedule, and restore database backups.'),
  ('security.users', 'Manage users', 'Security', 'Create and administer website users.'),
  ('security.roles', 'Manage roles', 'Security', 'Create roles and assign permissions.'),
  ('security.audit', 'View security audit', 'Security', 'View website security activity.')
ON DUPLICATE KEY UPDATE `display_name` = VALUES(`display_name`),
  `category` = VALUES(`category`), `description` = VALUES(`description`);

INSERT IGNORE INTO `admin_role_permission` (`role_name`, `permission_key`)
SELECT 'Owner', `permission_key` FROM `admin_permission`;

INSERT IGNORE INTO `admin_role_permission` (`role_name`, `permission_key`)
SELECT 'Administrator', `permission_key` FROM `admin_permission`
WHERE `permission_key` NOT LIKE 'server.%';

CREATE TABLE IF NOT EXISTS `admin_audit_log` (
  `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id` BIGINT UNSIGNED NULL,
  `username` VARCHAR(64) NOT NULL,
  `action` VARCHAR(100) NOT NULL,
  `outcome` VARCHAR(32) NOT NULL,
  `remote_address` VARCHAR(64) NULL,
  `detail` VARCHAR(500) NULL,
  `occurred_at_utc` DATETIME(6) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_admin_audit_occurred` (`occurred_at_utc` DESC),
  KEY `ix_admin_audit_user` (`user_id`, `occurred_at_utc` DESC),
  CONSTRAINT `fk_admin_audit_user`
    FOREIGN KEY (`user_id`) REFERENCES `admin_user` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `ui_schema_version` (
  `version` INT UNSIGNED NOT NULL,
  `applied_at_utc` DATETIME(6) NOT NULL,
  PRIMARY KEY (`version`)
) ENGINE=InnoDB;

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
VALUES (5, UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE `version` = VALUES(`version`);
