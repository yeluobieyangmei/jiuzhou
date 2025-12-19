-- ============================================
-- 家族表更新脚本（简单版）
-- 用途：如果数据库已经创建了clans表（旧版本），运行此脚本更新表结构
-- ============================================

USE jiuzhou;

-- 方案1：如果clans表已经存在（旧版本，包含country_rank和world_rank字段）
-- 运行以下SQL移除排名字段并添加新索引

-- 移除排名字段（如果字段不存在会报错，可以忽略）
ALTER TABLE clans DROP COLUMN country_rank;
ALTER TABLE clans DROP COLUMN world_rank;

-- 添加新索引（如果索引已存在会报错，可以忽略）
ALTER TABLE clans ADD INDEX idx_level (level);
ALTER TABLE clans ADD INDEX idx_funds (funds);

-- 更新表注释
ALTER TABLE clans COMMENT = '家族信息表（排名由服务器实时计算）';

-- ============================================
-- 方案2：如果clans表不存在，请运行数据库设计.sql中的家族表创建语句
-- ============================================

