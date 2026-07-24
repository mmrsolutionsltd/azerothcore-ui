USE `azerothcore_ui`;

ALTER TABLE `admin_user`
  DROP CHECK `ck_admin_user_role`,
  ADD COLUMN `account_scope` VARCHAR(16) NOT NULL DEFAULT 'All',
  ADD CONSTRAINT `ck_admin_user_account_scope`
    CHECK (`account_scope` IN ('All', 'Assigned', 'None'));

CREATE TABLE `admin_role` (
  `name` VARCHAR(32) NOT NULL,
  `description` VARCHAR(255) NOT NULL,
  `is_system` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`name`)
) ENGINE=InnoDB;

CREATE TABLE `admin_permission` (
  `permission_key` VARCHAR(64) NOT NULL,
  `display_name` VARCHAR(100) NOT NULL,
  `category` VARCHAR(32) NOT NULL,
  `description` VARCHAR(255) NOT NULL,
  PRIMARY KEY (`permission_key`)
) ENGINE=InnoDB;

CREATE TABLE `admin_role_permission` (
  `role_name` VARCHAR(32) NOT NULL,
  `permission_key` VARCHAR(64) NOT NULL,
  PRIMARY KEY (`role_name`, `permission_key`),
  CONSTRAINT `fk_role_permission_role` FOREIGN KEY (`role_name`)
    REFERENCES `admin_role` (`name`) ON DELETE CASCADE,
  CONSTRAINT `fk_role_permission_permission` FOREIGN KEY (`permission_key`)
    REFERENCES `admin_permission` (`permission_key`) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE `admin_user_game_account` (
  `admin_user_id` BIGINT UNSIGNED NOT NULL,
  `game_account_id` INT UNSIGNED NOT NULL,
  PRIMARY KEY (`admin_user_id`, `game_account_id`),
  CONSTRAINT `fk_user_game_account_user` FOREIGN KEY (`admin_user_id`)
    REFERENCES `admin_user` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB;

INSERT INTO `admin_role` (`name`, `description`, `is_system`) VALUES
  ('Owner', 'Unrestricted website and server access.', 1),
  ('Administrator', 'All player and world tools, excluding server infrastructure.', 1);

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
  ('security.audit', 'View security audit', 'Security', 'View website security activity.');

INSERT INTO `admin_role_permission` (`role_name`, `permission_key`)
SELECT 'Owner', `permission_key` FROM `admin_permission`;

INSERT INTO `admin_role_permission` (`role_name`, `permission_key`)
SELECT 'Administrator', `permission_key`
FROM `admin_permission`
WHERE `permission_key` NOT LIKE 'server.%';

UPDATE `admin_user` SET `account_scope` = 'All'
WHERE `role` IN ('Owner', 'Administrator');

INSERT INTO `ui_schema_version` (`version`, `applied_at_utc`)
VALUES (2, UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE `applied_at_utc` = VALUES(`applied_at_utc`);
