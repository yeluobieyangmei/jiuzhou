-- ============================================
-- 家族成员职位表设计
-- 用途：存储家族成员的职位信息（支持多个副族长和多个精英）
-- ============================================

USE jiuzhou;

-- 创建家族成员职位表
CREATE TABLE IF NOT EXISTS clan_member_roles (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '职位记录ID（主键）',
    clan_id INT NOT NULL COMMENT '家族ID（外键 -> clans.id）',
    player_id INT NOT NULL COMMENT '玩家ID（外键 -> players.id）',
    role VARCHAR(20) NOT NULL COMMENT '职位（leader/副族长/精英/成员）',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '任命时间',
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    
    -- 唯一约束：一个玩家在一个家族中只能有一个职位
    UNIQUE KEY uk_clan_player (clan_id, player_id),
    
    INDEX idx_clan_id (clan_id),
    INDEX idx_player_id (player_id),
    INDEX idx_role (role)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='家族成员职位表';

-- 添加外键约束
ALTER TABLE clan_member_roles
ADD CONSTRAINT fk_clan_member_roles_clan_id FOREIGN KEY (clan_id) REFERENCES clans(id) ON DELETE CASCADE,
ADD CONSTRAINT fk_clan_member_roles_player_id FOREIGN KEY (player_id) REFERENCES players(id) ON DELETE CASCADE;

-- ============================================
-- 职位说明：
-- leader: 族长（每个家族只有1个）
-- 副族长: 副族长（数量根据家族等级：1级1个，2级1个，3级2个，4级2个，5级3个）
-- 精英: 精英成员（数量根据家族等级：1级2个，2级3个，3级4个，4级5个，5级6个）
-- 成员: 普通成员（无限制，但受家族人数上限限制）
-- ============================================

