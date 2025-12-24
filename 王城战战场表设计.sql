-- 王城战战场表（battlefields）
-- 管理王城战战场信息
-- 
-- 说明：
-- 1. 如果王城战逻辑简单，可以不需要此表，直接在monsters表中关联两个家族ID即可
-- 2. 如果需要更复杂的战场管理（如战场状态、开始/结束时间等），可以使用此表

CREATE TABLE IF NOT EXISTS battlefields (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '战场ID（主键）',
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
    
    INDEX idx_clan_a (clan_a_id),
    INDEX idx_clan_b (clan_b_id),
    INDEX idx_boss_id (boss_id),
    INDEX idx_status (battlefield_status),
    FOREIGN KEY (clan_a_id) REFERENCES clans(id) ON DELETE CASCADE,
    FOREIGN KEY (clan_b_id) REFERENCES clans(id) ON DELETE CASCADE,
    FOREIGN KEY (boss_id) REFERENCES monsters(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='王城战战场表';

-- 注意：
-- 1. 如果不需要复杂的战场管理，可以省略此表
-- 2. 直接在monsters表中通过battlefield_clan_a_id和battlefield_clan_b_id关联即可
-- 3. 家族积分可以直接存储在clans表的war_score字段中

