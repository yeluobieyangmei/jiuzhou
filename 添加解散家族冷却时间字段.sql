-- ============================================
-- 添加解散家族冷却时间字段
-- 用途：记录玩家最后一次解散家族的时间，用于防止频繁创建/解散家族攻击
-- ============================================

USE jiuzhou;

-- 在 players 表中添加 last_clan_disband_time 字段
-- 用于记录玩家最后一次解散家族的时间
ALTER TABLE players 
ADD COLUMN last_clan_disband_time DATETIME NULL COMMENT '最后一次解散家族的时间（用于冷却时间检查）' 
AFTER clan_id;

-- 添加索引以便快速查询
ALTER TABLE players 
ADD INDEX idx_last_clan_disband_time (last_clan_disband_time);

