-- 清空王城战宣战数据
-- 用于测试或重置宣战状态

USE jiuzhou;

-- 1. 清空所有国家的宣战家族信息
UPDATE countries 
SET war_clan1_id = NULL, 
    war_clan2_id = NULL
WHERE id > 0;  -- 添加 WHERE 条件以满足安全更新模式

-- 2. 清空所有家族的宣战状态（如果存在 is_war_declared 字段）
-- 注意：如果 clans 表中没有 is_war_declared 字段，这行会报错，可以忽略
UPDATE clans 
SET is_war_declared = FALSE 
WHERE id > 0 AND is_war_declared = TRUE;  -- 添加 id > 0 以满足安全更新模式

-- 3. 删除所有准备中的战场记录（可选，如果需要完全清空）
DELETE FROM battlefields 
WHERE battlefield_status = 'preparing';

-- 4. 或者删除所有战场记录（包括进行中和已结束的，谨慎使用）
-- DELETE FROM battlefields;

-- 显示清空结果
SELECT '宣战数据已清空' AS 结果;

