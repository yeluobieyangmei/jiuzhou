-- 为玩家表添加经验值字段
-- 执行此脚本后，所有玩家的经验值默认为0

ALTER TABLE players 
ADD COLUMN experience INT NOT NULL DEFAULT 0 COMMENT '当前经验值' AFTER level;

-- 为经验值字段添加索引（如果需要按经验值排序查询）
-- ALTER TABLE players ADD INDEX idx_experience (experience);

