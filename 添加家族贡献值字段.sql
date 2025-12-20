USE jiuzhou;

-- 添加家族贡献值字段到players表
ALTER TABLE players
ADD COLUMN clan_contribution INT NOT NULL DEFAULT 0 COMMENT '家族贡献值';

-- 添加索引（如果需要按贡献值排序）
ALTER TABLE players
ADD INDEX idx_clan_contribution (clan_contribution);

