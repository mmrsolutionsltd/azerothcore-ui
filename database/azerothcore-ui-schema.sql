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
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_admin_user_normalized_username` (`normalized_username`),
  CONSTRAINT `ck_admin_user_role` CHECK (`role` IN ('Owner', 'Administrator'))
) ENGINE=InnoDB;

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

INSERT INTO `ui_schema_version` (`version`, `applied_at_utc`)
VALUES (1, UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE `version` = VALUES(`version`);
