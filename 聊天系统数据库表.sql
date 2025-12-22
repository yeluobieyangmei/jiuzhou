-- ============================================
-- 九州游戏聊天系统数据库表结构
-- 数据库名：jiuzhou
-- ============================================

USE jiuzhou;

-- 1. 世界消息表（全局最新20条）
CREATE TABLE IF NOT EXISTS world_messages (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '消息ID（主键）',
    player_id INT NOT NULL COMMENT '玩家ID',
    player_name VARCHAR(50) NOT NULL COMMENT '玩家姓名',
    message TEXT NOT NULL COMMENT '消息内容',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    INDEX idx_created_at (created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='世界消息表（全局最新20条）';

-- 2. 国家消息表（全局最新20条）
CREATE TABLE IF NOT EXISTS country_messages (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '消息ID（主键）',
    country_id INT NOT NULL COMMENT '国家ID',
    player_id INT NOT NULL COMMENT '玩家ID',
    player_name VARCHAR(50) NOT NULL COMMENT '玩家姓名',
    message TEXT NOT NULL COMMENT '消息内容',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    INDEX idx_created_at (created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='国家消息表（全局最新20条）';

-- 3. 家族消息表（全局最新20条）
CREATE TABLE IF NOT EXISTS clan_messages (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '消息ID（主键）',
    clan_id INT NOT NULL COMMENT '家族ID',
    player_id INT NOT NULL COMMENT '玩家ID',
    player_name VARCHAR(50) NOT NULL COMMENT '玩家姓名',
    message TEXT NOT NULL COMMENT '消息内容',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    INDEX idx_created_at (created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='家族消息表（全局最新20条）';

-- 4. 玩家发言记录表（用于防刷和频率限制）
CREATE TABLE IF NOT EXISTS player_message_logs (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '记录ID（主键）',
    player_id INT NOT NULL COMMENT '玩家ID',
    channel_type ENUM('world', 'country', 'clan') NOT NULL COMMENT '频道类型（世界/国家/家族）',
    message_hash VARCHAR(64) NOT NULL COMMENT '消息内容的MD5哈希，用于检测重复',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    INDEX idx_player_channel_time (player_id, channel_type, created_at),
    INDEX idx_player_hash_time (player_id, message_hash, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='玩家发言记录表（用于防刷和频率限制）';

-- 5. 玩家禁言记录表
CREATE TABLE IF NOT EXISTS player_mute_records (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '记录ID（主键）',
    player_id INT NOT NULL COMMENT '玩家ID',
    mute_until DATETIME NOT NULL COMMENT '禁言到期时间',
    reason VARCHAR(255) COMMENT '禁言原因',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    INDEX idx_player_mute (player_id, mute_until)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='玩家禁言记录表';

