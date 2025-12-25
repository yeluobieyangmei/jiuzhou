-- 更新战场表结构，添加唯一索引以支持 ON DUPLICATE KEY UPDATE
-- 注意：如果表已存在，需要先删除旧数据或调整索引

USE jiuzhou;

-- 如果表不存在，创建表（不包含外键约束，稍后单独添加）
-- 如果表已存在，先添加 country_id 字段（如果不存在）
CREATE TABLE IF NOT EXISTS battlefields (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '战场ID（主键）',
    country_id INT NOT NULL COMMENT '国家ID（外键，关联countries表）',
    clan_a_id INT NOT NULL COMMENT 'A方家族ID',
    clan_b_id INT NOT NULL COMMENT 'B方家族ID',
    boss_id INT NULL COMMENT '关联Boss ID（monsters表）',
    battlefield_status ENUM('preparing', 'fighting', 'finished') NOT NULL DEFAULT 'preparing' COMMENT '战场状态（准备中/战斗中/已结束）',
    clan_a_score INT NOT NULL DEFAULT 0 COMMENT 'A方家族积分',
    clan_b_score INT NOT NULL DEFAULT 0 COMMENT 'B方家族积分',
    winner_clan_id INT NULL COMMENT '获胜家族ID',
    start_time DATETIME NULL COMMENT '战斗开始时间',
    end_time DATETIME NULL COMMENT '战斗结束时间',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    
    UNIQUE KEY uk_country_status (country_id, battlefield_status), -- 唯一索引：确保每个国家同时只有一个进行中的战场
    INDEX idx_country_id (country_id),
    INDEX idx_clan_a (clan_a_id),
    INDEX idx_clan_b (clan_b_id),
    INDEX idx_boss_id (boss_id),
    INDEX idx_status (battlefield_status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='王城战战场表';

-- 如果表已存在但没有 country_id 字段，添加该字段
SET @column_exists = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_SCHEMA = 'jiuzhou' 
    AND TABLE_NAME = 'battlefields' 
    AND COLUMN_NAME = 'country_id'
);

SET @sql = IF(@column_exists = 0,
    'ALTER TABLE battlefields ADD COLUMN country_id INT NOT NULL COMMENT ''国家ID（外键）'' AFTER id',
    'SELECT ''country_id column already exists'' AS message'
);

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 如果 country_id 字段已存在但还没有外键约束，添加外键约束
SET @fk_exists = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
    WHERE TABLE_SCHEMA = 'jiuzhou' 
    AND TABLE_NAME = 'battlefields' 
    AND CONSTRAINT_NAME = 'fk_battlefields_country_id'
);

SET @sql2 = IF(@fk_exists = 0,
    'ALTER TABLE battlefields ADD CONSTRAINT fk_battlefields_country_id FOREIGN KEY (country_id) REFERENCES countries(id) ON DELETE CASCADE',
    'SELECT ''Foreign key already exists'' AS message'
);

PREPARE stmt2 FROM @sql2;
EXECUTE stmt2;
DEALLOCATE PREPARE stmt2;

-- 添加其他外键约束（如果不存在）
-- 1. clan_a_id 外键
SET @fk_clan_a_exists = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
    WHERE TABLE_SCHEMA = 'jiuzhou' 
    AND TABLE_NAME = 'battlefields' 
    AND CONSTRAINT_NAME = 'fk_battlefields_clan_a_id'
);

SET @sql3 = IF(@fk_clan_a_exists = 0,
    'ALTER TABLE battlefields ADD CONSTRAINT fk_battlefields_clan_a_id FOREIGN KEY (clan_a_id) REFERENCES clans(id) ON DELETE CASCADE',
    'SELECT ''Foreign key fk_battlefields_clan_a_id already exists'' AS message'
);

PREPARE stmt3 FROM @sql3;
EXECUTE stmt3;
DEALLOCATE PREPARE stmt3;

-- 2. clan_b_id 外键
SET @fk_clan_b_exists = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
    WHERE TABLE_SCHEMA = 'jiuzhou' 
    AND TABLE_NAME = 'battlefields' 
    AND CONSTRAINT_NAME = 'fk_battlefields_clan_b_id'
);

SET @sql4 = IF(@fk_clan_b_exists = 0,
    'ALTER TABLE battlefields ADD CONSTRAINT fk_battlefields_clan_b_id FOREIGN KEY (clan_b_id) REFERENCES clans(id) ON DELETE CASCADE',
    'SELECT ''Foreign key fk_battlefields_clan_b_id already exists'' AS message'
);

PREPARE stmt4 FROM @sql4;
EXECUTE stmt4;
DEALLOCATE PREPARE stmt4;

-- 3. boss_id 外键（仅当 monsters 表存在时添加）
SET @monsters_table_exists = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.TABLES 
    WHERE TABLE_SCHEMA = 'jiuzhou' 
    AND TABLE_NAME = 'monsters'
);

SET @fk_boss_exists = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
    WHERE TABLE_SCHEMA = 'jiuzhou' 
    AND TABLE_NAME = 'battlefields' 
    AND CONSTRAINT_NAME = 'fk_battlefields_boss_id'
);

SET @sql5 = IF(@monsters_table_exists > 0 AND @fk_boss_exists = 0,
    'ALTER TABLE battlefields ADD CONSTRAINT fk_battlefields_boss_id FOREIGN KEY (boss_id) REFERENCES monsters(id) ON DELETE SET NULL',
    IF(@monsters_table_exists = 0,
        'SELECT ''monsters table does not exist, skipping boss_id foreign key'' AS message',
        'SELECT ''Foreign key fk_battlefields_boss_id already exists'' AS message'
    )
);

PREPARE stmt5 FROM @sql5;
EXECUTE stmt5;
DEALLOCATE PREPARE stmt5;

