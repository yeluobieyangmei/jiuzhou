-- ============================================
-- 家族日志表设计
-- 用途：记录家族的所有重要操作历史
-- ============================================

USE jiuzhou;

-- 家族日志表（clan_logs）
CREATE TABLE IF NOT EXISTS clan_logs (
    id INT AUTO_INCREMENT PRIMARY KEY COMMENT '日志ID（主键）',
    clan_id INT NOT NULL COMMENT '家族ID',
    
    -- 操作类型（枚举值）
    -- 'create': 创建家族
    -- 'join': 加入家族
    -- 'leave': 离开家族
    -- 'kick': 踢出成员
    -- 'appoint': 职位任命
    -- 'donate': 家族捐献
    -- 'disband': 解散家族
    -- 'upgrade': 家族升级
    -- 'change_name': 改名
    operation_type VARCHAR(20) NOT NULL COMMENT '操作类型',
    
    -- 操作者信息
    operator_id INT NULL COMMENT '操作者玩家ID（NULL表示系统操作）',
    operator_name VARCHAR(50) NULL COMMENT '操作者姓名',
    
    -- 目标玩家信息（如果是针对玩家的操作）
    target_player_id INT NULL COMMENT '目标玩家ID（如被任命、被踢出的玩家）',
    target_player_name VARCHAR(50) NULL COMMENT '目标玩家姓名',
    
    -- 操作详情（JSON格式存储详细信息，便于扩展）
    -- 例如：{"role":"副族长","old_role":"成员"} 或 {"amount":1000,"funds_added":100}
    details TEXT NULL COMMENT '操作详情（JSON格式）',
    
    -- 操作描述（格式化后的文本，便于直接显示）
    description TEXT NOT NULL COMMENT '操作描述（如：XXX创建了家族、XXX加入了家族）',
    
    -- 时间戳
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '操作时间',
    
    -- 索引
    INDEX idx_clan_id (clan_id),
    INDEX idx_operation_type (operation_type),
    INDEX idx_created_at (created_at),
    INDEX idx_clan_created (clan_id, created_at),
    
    -- 外键约束
    CONSTRAINT fk_clan_logs_clan_id FOREIGN KEY (clan_id) REFERENCES clans(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='家族操作日志表';

-- ============================================
-- 使用说明：
-- 1. 所有家族相关操作都应该记录到此表
-- 2. operation_type 用于分类查询（如只查询职位任命）
-- 3. description 字段存储格式化后的文本，便于直接显示
-- 4. details 字段存储JSON格式的详细信息，便于后续扩展
-- 5. 查询时按 clan_id 和 created_at 排序即可
-- ============================================

-- 示例数据：
-- INSERT INTO clan_logs (clan_id, operation_type, operator_id, operator_name, description) 
-- VALUES (1, 'create', 100, '张三', '张三创建了家族');

-- INSERT INTO clan_logs (clan_id, operation_type, operator_id, operator_name, target_player_id, target_player_name, details, description) 
-- VALUES (1, 'join', 101, '李四', NULL, NULL, NULL, '李四加入了家族');

-- INSERT INTO clan_logs (clan_id, operation_type, operator_id, operator_name, target_player_id, target_player_name, details, description) 
-- VALUES (1, 'appoint', 100, '张三', 102, '王五', '{"role":"副族长","old_role":"成员"}', '张三任命王五为副族长');


