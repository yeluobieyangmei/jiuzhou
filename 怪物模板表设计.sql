-- 怪物模板表（monster_templates）
-- 存储各种怪物的基础配置信息
-- 这个表是必须的，用于服务器统一管理怪物配置
-- 
-- 使用说明：
-- 1. 普通怪物（土匪、黄巾军等）：客户端按模板生成，不存储实例
-- 2. Boss（王城战Boss）：服务器按模板生成实例，存储到monsters表

CREATE TABLE IF NOT EXISTS monster_templates (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '模板ID（主键）',
    monster_type INT NOT NULL COMMENT '怪物类型（1=土匪, 2=黄巾军, 3=绿林匪寇, 4=逃兵, 5=王城战Boss）',
    name VARCHAR(50) NOT NULL COMMENT '怪物名称',
    base_level INT NOT NULL DEFAULT 1 COMMENT '基础等级',
    base_hp INT NOT NULL COMMENT '基础生命值',
    base_attack INT NOT NULL COMMENT '基础攻击力',
    base_defense INT NOT NULL COMMENT '基础防御力',
    base_copper_money INT NOT NULL DEFAULT 0 COMMENT '基础掉落铜钱',
    base_experience INT NOT NULL DEFAULT 0 COMMENT '基础掉落经验值',
    level_growth_rate DECIMAL(5,2) NOT NULL DEFAULT 1.0 COMMENT '等级成长系数（每级属性增长倍数）',
    is_boss BOOLEAN NOT NULL DEFAULT FALSE COMMENT '是否为Boss（Boss需要存储实例到monsters表）',
    description TEXT NULL COMMENT '怪物描述',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    
    UNIQUE KEY uk_monster_type (monster_type),
    INDEX idx_is_boss (is_boss)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='怪物模板表';

-- 插入初始怪物模板数据
INSERT INTO monster_templates (monster_type, name, base_level, base_hp, base_attack, base_defense, base_copper_money, base_experience, level_growth_rate, is_boss, description) VALUES
(1, '土匪', 1, 100, 20, 5, 100, 50, 1.2, FALSE, '普通怪物，用于玩家练级获取经验值'),
(2, '黄巾军', 5, 300, 50, 15, 500, 200, 1.3, FALSE, '普通怪物，用于玩家练级获取经验值'),
(3, '绿林匪寇', 10, 600, 100, 30, 1000, 500, 1.4, FALSE, '普通怪物，用于玩家练级获取经验值'),
(4, '逃兵', 15, 1000, 150, 50, 2000, 1000, 1.5, FALSE, '普通怪物，用于玩家练级获取经验值'),
(5, '王城战Boss', 50, 10000, 1000, 300, 50000, 10000, 2.0, TRUE, '王城战战场Boss，初始中立，击败后归属击败者家族，增加家族积分');

