-- ============================================
-- 九州游戏数据库表结构设计
-- 数据库名：jiuzhou
-- ============================================

USE jiuzhou;

-- ============================================
-- 第一步：先创建表结构（不添加外键约束，避免循环依赖）
-- ============================================

-- 1. 国家表（countries）- 先创建，因为它不依赖其他表
CREATE TABLE IF NOT EXISTS countries (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '国家ID（主键）',
    name VARCHAR(50) NOT NULL UNIQUE COMMENT '国名',
    code VARCHAR(50) NOT NULL COMMENT '国号',
    declaration TEXT COMMENT '国家宣言',
    announcement TEXT COMMENT '国家公告',
    copper_money INT NOT NULL DEFAULT 0 COMMENT '铜钱',
    food INT NOT NULL DEFAULT 0 COMMENT '粮食',
    gold INT NOT NULL DEFAULT 0 COMMENT '黄金',
    
    -- 各官职的玩家ID（稍后添加外键）
    king_id INT NULL COMMENT '国王ID',
    governor_id INT NULL COMMENT '大都督ID',
    prime_minister_id INT NULL COMMENT '丞相ID',
    defense_minister_id INT NULL COMMENT '太尉ID',
    censor_id INT NULL COMMENT '御史大夫ID',
    guard_id INT NULL COMMENT '金吾卫ID',
    
    -- 家族相关（稍后添加外键）
    ruling_clan_id INT NULL COMMENT '执政家族ID',
    war_clan1_id INT NULL COMMENT '宣战家族1ID',
    war_clan2_id INT NULL COMMENT '宣战家族2ID',
    
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    
    INDEX idx_name (name),
    INDEX idx_king_id (king_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='国家信息表';

-- 2. 家族表（clans）
-- 注意：排名（country_rank, world_rank）不存储在数据库中，应该由服务器实时计算
CREATE TABLE IF NOT EXISTS clans (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '家族ID（主键）',
    name VARCHAR(50) NOT NULL UNIQUE COMMENT '家族名字',
    level INT NOT NULL DEFAULT 1 COMMENT '家族等级（1-5级，每级增加10人上限）',
    leader_id INT NULL COMMENT '族长ID（稍后添加外键）',
    deputy_leader_id INT NULL COMMENT '副族长ID（稍后添加外键）',
    prosperity INT NOT NULL DEFAULT 0 COMMENT '家族繁荣值',
    funds INT NOT NULL DEFAULT 0 COMMENT '家族资金（升级消耗50000）',
    war_score INT NOT NULL DEFAULT 0 COMMENT '家族王城战积分',
    is_war_declared BOOLEAN NOT NULL DEFAULT FALSE COMMENT '王城战是否宣战',
    is_war_fighting BOOLEAN NOT NULL DEFAULT FALSE COMMENT '王城战是否战斗中',
    country_id INT NULL COMMENT '所属国家ID（稍后添加外键，家族创建时自动设置为族长的国家）',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    
    INDEX idx_name (name),
    INDEX idx_leader_id (leader_id),
    INDEX idx_country_id (country_id),
    INDEX idx_level (level),
    INDEX idx_funds (funds)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='家族信息表';

-- 3. 玩家表（players）
CREATE TABLE IF NOT EXISTS players (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '玩家ID（主键）',
    account_id INT NOT NULL COMMENT '关联账号ID（稍后添加外键 -> accounts.id）',
    name VARCHAR(50) NOT NULL COMMENT '玩家姓名',
    gender VARCHAR(10) NOT NULL COMMENT '性别（男/女）',
    level INT NOT NULL DEFAULT 1 COMMENT '等级',
    title_name VARCHAR(50) NOT NULL DEFAULT '无' COMMENT '称号名（无/散人/精英/大神/九五至尊）',
    office VARCHAR(50) NOT NULL DEFAULT '国民' COMMENT '官职（国民/镖师/金吾卫/御史大夫/太尉/丞相/大都督/国王）',
    copper_money INT NOT NULL DEFAULT 50000000 COMMENT '铜钱',
    gold INT NOT NULL DEFAULT 2000000 COMMENT '黄金',
    country_id INT NULL COMMENT '所属国家ID（稍后添加外键 -> countries.id）',
    clan_id INT NULL COMMENT '所属家族ID（稍后添加外键 -> clans.id）',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    
    INDEX idx_account_id (account_id),
    INDEX idx_country_id (country_id),
    INDEX idx_clan_id (clan_id),
    INDEX idx_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='玩家数据表';

-- 4. 玩家属性表（player_attributes）
CREATE TABLE IF NOT EXISTS player_attributes (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '属性ID（主键）',
    player_id INT NOT NULL UNIQUE COMMENT '玩家ID（稍后添加外键 -> players.id）',
    max_hp INT NOT NULL DEFAULT 200 COMMENT '生命值（最大生命值）',
    current_hp INT NOT NULL DEFAULT 200 COMMENT '当前生命值',
    attack INT NOT NULL DEFAULT 100000 COMMENT '攻击力',
    defense INT NOT NULL DEFAULT 2 COMMENT '防御力',
    crit_rate FLOAT NOT NULL DEFAULT 0.0 COMMENT '暴击率（0.0-1.0）',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    
    INDEX idx_player_id (player_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='玩家属性表';

-- 5. 家族成员职位表（clan_member_roles）
-- 用途：存储家族成员的职位信息（支持多个副族长和多个精英）
CREATE TABLE IF NOT EXISTS clan_member_roles (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '职位记录ID（主键）',
    clan_id INT NOT NULL COMMENT '家族ID（稍后添加外键 -> clans.id）',
    player_id INT NOT NULL COMMENT '玩家ID（稍后添加外键 -> players.id）',
    role VARCHAR(20) NOT NULL COMMENT '职位（leader/副族长/精英/成员）',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '任命时间',
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    
    -- 唯一约束：一个玩家在一个家族中只能有一个职位
    UNIQUE KEY uk_clan_player (clan_id, player_id),
    
    INDEX idx_clan_id (clan_id),
    INDEX idx_player_id (player_id),
    INDEX idx_role (role)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='家族成员职位表';

-- ============================================
-- 第二步：添加外键约束（按依赖顺序）
-- ============================================

-- 玩家表外键
ALTER TABLE players
ADD CONSTRAINT fk_players_account_id FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE CASCADE,
ADD CONSTRAINT fk_players_country_id FOREIGN KEY (country_id) REFERENCES countries(id) ON DELETE SET NULL,
ADD CONSTRAINT fk_players_clan_id FOREIGN KEY (clan_id) REFERENCES clans(id) ON DELETE SET NULL;

-- 玩家属性表外键
ALTER TABLE player_attributes
ADD CONSTRAINT fk_player_attributes_player_id FOREIGN KEY (player_id) REFERENCES players(id) ON DELETE CASCADE;

-- 家族成员职位表外键
ALTER TABLE clan_member_roles
ADD CONSTRAINT fk_clan_member_roles_clan_id FOREIGN KEY (clan_id) REFERENCES clans(id) ON DELETE CASCADE,
ADD CONSTRAINT fk_clan_member_roles_player_id FOREIGN KEY (player_id) REFERENCES players(id) ON DELETE CASCADE;

-- 家族表外键
ALTER TABLE clans
ADD CONSTRAINT fk_clans_leader_id FOREIGN KEY (leader_id) REFERENCES players(id) ON DELETE SET NULL,
ADD CONSTRAINT fk_clans_deputy_leader_id FOREIGN KEY (deputy_leader_id) REFERENCES players(id) ON DELETE SET NULL,
ADD CONSTRAINT fk_clans_country_id FOREIGN KEY (country_id) REFERENCES countries(id) ON DELETE SET NULL;

-- 国家表外键（官职ID）
ALTER TABLE countries
ADD CONSTRAINT fk_countries_king_id FOREIGN KEY (king_id) REFERENCES players(id) ON DELETE SET NULL,
ADD CONSTRAINT fk_countries_governor_id FOREIGN KEY (governor_id) REFERENCES players(id) ON DELETE SET NULL,
ADD CONSTRAINT fk_countries_prime_minister_id FOREIGN KEY (prime_minister_id) REFERENCES players(id) ON DELETE SET NULL,
ADD CONSTRAINT fk_countries_defense_minister_id FOREIGN KEY (defense_minister_id) REFERENCES players(id) ON DELETE SET NULL,
ADD CONSTRAINT fk_countries_censor_id FOREIGN KEY (censor_id) REFERENCES players(id) ON DELETE SET NULL,
ADD CONSTRAINT fk_countries_guard_id FOREIGN KEY (guard_id) REFERENCES players(id) ON DELETE SET NULL;

-- 国家表外键（家族ID）
ALTER TABLE countries
ADD CONSTRAINT fk_countries_ruling_clan_id FOREIGN KEY (ruling_clan_id) REFERENCES clans(id) ON DELETE SET NULL,
ADD CONSTRAINT fk_countries_war_clan1_id FOREIGN KEY (war_clan1_id) REFERENCES clans(id) ON DELETE SET NULL,
ADD CONSTRAINT fk_countries_war_clan2_id FOREIGN KEY (war_clan2_id) REFERENCES clans(id) ON DELETE SET NULL;

-- ============================================
-- 说明：
-- 1. 玩家表（players）通过 account_id 关联账号表（accounts）
-- 2. 玩家表通过 country_id 和 clan_id 关联国家和家族
-- 3. 国家表存储各官职的玩家ID（外键）
-- 4. 家族表存储族长ID（外键），副族长和精英职位存储在 clan_member_roles 表中
-- 5. 玩家属性表与玩家表是一对一关系（通过 player_id）
-- 6. 家族成员职位表（clan_member_roles）存储家族成员的职位信息
--    职位类型：leader（族长）、副族长、精英、成员
--    职位数量限制（根据家族等级）：
--      1级：1个副族长，2个精英
--      2级：1个副族长，3个精英
--      3级：2个副族长，4个精英
--      4级：2个副族长，5个精英
--      5级：3个副族长，6个精英
-- 7. 所有外键都设置了 ON DELETE CASCADE 或 ON DELETE SET NULL，保证数据一致性
-- ============================================

