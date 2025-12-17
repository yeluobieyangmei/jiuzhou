-- ============================================
-- 九州游戏国家初始化脚本
-- 数据库名：jiuzhou
-- ============================================

USE jiuzhou;

-- 清空现有国家数据（可选，如果之前有测试数据想重置）
-- DELETE FROM countries;

-- 初始化9个国家
INSERT INTO countries (name, code, declaration, announcement, copper_money, food, gold)
VALUES
('燕州', '燕', '九州沉沦、谁与争锋！', '', 5000000, 10000000, 50000),
('徐州', '徐', '九州沉沦、谁与争锋！', '', 5000000, 10000000, 50000),
('荆州', '荆', '九州沉沦、谁与争锋！', '', 5000000, 10000000, 50000),
('扬州', '扬', '九州沉沦、谁与争锋！', '', 5000000, 10000000, 50000),
('幽州', '幽', '九州沉沦、谁与争锋！', '', 5000000, 10000000, 50000),
('江州', '江', '九州沉沦、谁与争锋！', '', 5000000, 10000000, 50000),
('青州', '青', '九州沉沦、谁与争锋！', '', 5000000, 10000000, 50000),
('豫州', '豫', '九州沉沦、谁与争锋！', '', 5000000, 10000000, 50000),
('益州', '益', '九州沉沦、谁与争锋！', '', 5000000, 10000000, 50000)
ON DUPLICATE KEY UPDATE
    declaration = VALUES(declaration),
    announcement = VALUES(announcement),
    copper_money = VALUES(copper_money),
    food = VALUES(food),
    gold = VALUES(gold);

-- 查询验证（执行后应该看到9条记录）
SELECT id, name, code, declaration, announcement, copper_money, food, gold 
FROM countries 
ORDER BY id;

