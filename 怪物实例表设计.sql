-- 怪物实例表（monsters）
-- 存储当前世界中存在的怪物实例
-- 
-- 使用说明：
-- 1. 仅用于王城战Boss等需要跨玩家同步的特殊怪物
-- 2. 普通怪物（土匪、黄巾军等）不需要存储实例，由客户端按模板生成即可
-- 3. Boss初始无归属（clan_id = NULL），最后击败者家族获得归属

CREATE TABLE IF NOT EXISTS monsters (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '怪物实例ID（主键）',
    template_id INT NOT NULL COMMENT '关联怪物模板ID',
    monster_type INT NOT NULL COMMENT '怪物类型（冗余字段，便于查询，5=王城战Boss）',
    name VARCHAR(50) NOT NULL COMMENT '怪物名称',
    level INT NOT NULL COMMENT '当前等级',
    max_hp INT NOT NULL COMMENT '最大生命值',
    current_hp INT NOT NULL COMMENT '当前生命值（实时更新，用于战斗同步）',
    attack INT NOT NULL COMMENT '攻击力',
    defense INT NOT NULL COMMENT '防御力',
    copper_money INT NOT NULL DEFAULT 0 COMMENT '掉落铜钱',
    experience INT NOT NULL DEFAULT 0 COMMENT '掉落经验值',
    
    -- 王城战相关字段
    clan_id INT NULL COMMENT '归属家族ID（初始为NULL中立，击败后归属击败者家族）',
    battlefield_clan_a_id INT NULL COMMENT '战场A方家族ID',
    battlefield_clan_b_id INT NULL COMMENT '战场B方家族ID',
    last_attacker_player_id INT NULL COMMENT '最后攻击者玩家ID（用于判断归属）',
    last_attacker_clan_id INT NULL COMMENT '最后攻击者家族ID（用于判断归属）',
    
    spawn_time DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '生成时间',
    is_alive BOOLEAN NOT NULL DEFAULT TRUE COMMENT '是否存活',
    killed_by_player_id INT NULL COMMENT '被哪个玩家击败（用于记录）',
    killed_by_clan_id INT NULL COMMENT '被哪个家族击败（用于记录）',
    killed_time DATETIME NULL COMMENT '被击败时间',
    
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    
    INDEX idx_template_id (template_id),
    INDEX idx_monster_type (monster_type),
    INDEX idx_clan_id (clan_id),
    INDEX idx_battlefield_clans (battlefield_clan_a_id, battlefield_clan_b_id),
    INDEX idx_is_alive (is_alive),
    INDEX idx_last_attacker_clan (last_attacker_clan_id),
    FOREIGN KEY (template_id) REFERENCES monster_templates(id) ON DELETE CASCADE,
    FOREIGN KEY (clan_id) REFERENCES clans(id) ON DELETE SET NULL,
    FOREIGN KEY (battlefield_clan_a_id) REFERENCES clans(id) ON DELETE SET NULL,
    FOREIGN KEY (battlefield_clan_b_id) REFERENCES clans(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='怪物实例表（仅用于Boss）';

-- 注意：
-- 1. 普通怪物（土匪、黄巾军等）不需要存储实例，客户端按模板生成即可
-- 2. 王城战Boss创建时：clan_id = NULL（中立），设置battlefield_clan_a_id和battlefield_clan_b_id
-- 3. 玩家攻击Boss时：更新last_attacker_player_id和last_attacker_clan_id
-- 4. Boss被击败时：clan_id = last_attacker_clan_id（归属最后击败者家族），增加家族积分
-- 5. 通过WebSocket实时推送Boss状态变化（生命值、归属等）

