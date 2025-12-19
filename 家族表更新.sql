-- ============================================
-- 家族表更新脚本
-- 用途：如果数据库已经创建了clans表，运行此脚本更新表结构
-- 注意：如果clans表不存在，请先运行数据库设计.sql创建表
-- ============================================

USE jiuzhou;

-- 检查并移除排名字段（排名应该由服务器实时计算，不存储在数据库中）
-- 注意：MySQL不支持 IF EXISTS，如果字段不存在会报错，可以忽略
SET @exist := (SELECT COUNT(*) FROM information_schema.COLUMNS 
               WHERE TABLE_SCHEMA = 'jiuzhou' 
               AND TABLE_NAME = 'clans' 
               AND COLUMN_NAME = 'country_rank');
SET @sqlstmt := IF(@exist > 0, 'ALTER TABLE clans DROP COLUMN country_rank', 'SELECT "country_rank字段不存在，跳过"');
PREPARE stmt FROM @sqlstmt;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @exist := (SELECT COUNT(*) FROM information_schema.COLUMNS 
               WHERE TABLE_SCHEMA = 'jiuzhou' 
               AND TABLE_NAME = 'clans' 
               AND COLUMN_NAME = 'world_rank');
SET @sqlstmt := IF(@exist > 0, 'ALTER TABLE clans DROP COLUMN world_rank', 'SELECT "world_rank字段不存在，跳过"');
PREPARE stmt FROM @sqlstmt;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 添加索引优化查询性能（如果索引不存在则添加）
SET @exist := (SELECT COUNT(*) FROM information_schema.STATISTICS 
               WHERE TABLE_SCHEMA = 'jiuzhou' 
               AND TABLE_NAME = 'clans' 
               AND INDEX_NAME = 'idx_level');
SET @sqlstmt := IF(@exist = 0, 'ALTER TABLE clans ADD INDEX idx_level (level)', 'SELECT "idx_level索引已存在，跳过"');
PREPARE stmt FROM @sqlstmt;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @exist := (SELECT COUNT(*) FROM information_schema.STATISTICS 
               WHERE TABLE_SCHEMA = 'jiuzhou' 
               AND TABLE_NAME = 'clans' 
               AND INDEX_NAME = 'idx_funds');
SET @sqlstmt := IF(@exist = 0, 'ALTER TABLE clans ADD INDEX idx_funds (funds)', 'SELECT "idx_funds索引已存在，跳过"');
PREPARE stmt FROM @sqlstmt;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 更新表注释
ALTER TABLE clans COMMENT = '家族信息表（排名由服务器实时计算）';
