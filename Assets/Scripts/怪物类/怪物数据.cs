using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using 国家系统;

namespace 怪物数据结构
{
    public enum 怪物类型
    {
        土匪 = 1,
        黄巾军 = 2,
        绿林匪寇 = 3,
        逃兵 = 4,
        王城战Boss = 5,
    }
    public class 怪物数据
    {
        private static int 全局ID计数器 = 1;

        // 怪物唯一标识
        public int ID { get; private set; }
        public 怪物类型 类型 { get; private set; }
        public string 名称 { get; private set; }
        public int 等级 { get; private set; }
        public int 铜钱 { get; set; }
        public int 经验值 { get; set; }
        public 怪物属性 属性 { get; private set; } = new 怪物属性();
        
        // 怪物归属信息（不属于战斗属性，属于元数据）
        public 家族信息库 归属家族 { get; set; }  // 如果怪物属于某个家族（如王城战Boss）

        // 私有构造函数
        private 怪物数据() { }

        // 工厂方法
        public static 怪物数据 创建(怪物类型 类型, int 等级, int 生命值, int 攻击力, int 防御力)
        {
            var 怪物 = new 怪物数据
            {
                ID = 获取下一个ID(),
                类型 = 类型,
                名称 = 获取怪物名称(类型),
                等级 = 等级
            };

            return 怪物.初始化(生命值, 攻击力, 防御力);
        }

        private static int 获取下一个ID()
        {
            return 全局ID计数器++;
        }

        private static string 获取怪物名称(怪物类型 类型)
        {
            return 类型 switch
            {
                怪物类型.土匪 => "土匪",
                怪物类型.黄巾军 => "黄巾军",
                怪物类型.绿林匪寇 => "绿林匪寇",
                怪物类型.逃兵 => "逃兵",
                怪物类型.王城战Boss => "王城战Boss",
                _ => "未知怪物"
            };
        }

        private 怪物数据 初始化(int 生命值, int 攻击力, int 防御力)
        {
            属性.生命值 = 生命值;
            属性.当前生命值 = 属性.生命值;
            属性.攻击力 = 攻击力;
            属性.防御力 = 防御力;
            return this;
        }

        // 重置怪物状态（用于对象池）
        // 在 怪物数据.cs 中修改 重置 方法
        public void 重置(怪物类型 类型, int 等级, int 生命值, int 攻击力, int 防御力)
        {
            this.类型 = 类型;
            this.等级 = 等级;
            名称 = 获取怪物名称(类型);
            初始化(生命值, 攻击力, 防御力);
        }

        /// <summary>
        /// 根据模板创建怪物（使用模板的基础属性，根据等级计算实际属性）
        /// </summary>
        /// <param name="模板">怪物模板数据（来自怪物模板管理）</param>
        /// <param name="等级">怪物等级（可选，默认使用模板的基础等级）</param>
        public static 怪物数据 根据模板创建(服务端怪物模板数据 模板, int? 等级 = null)
        {
            if (模板 == null)
            {
                Debug.LogError("怪物模板为空，无法创建怪物");
                return null;
            }

            int 实际等级 = 等级 ?? 模板.baseLevel;
            
            // 根据等级和成长系数计算实际属性
            // 公式：实际属性 = 基础属性 * (成长系数 ^ (等级 - 基础等级))
            float 成长倍数 = Mathf.Pow((float)模板.levelGrowthRate, 实际等级 - 模板.baseLevel);
            
            int 实际生命值 = Mathf.RoundToInt(模板.baseHp * 成长倍数);
            int 实际攻击力 = Mathf.RoundToInt(模板.baseAttack * 成长倍数);
            int 实际防御力 = Mathf.RoundToInt(模板.baseDefense * 成长倍数);
            
            // 掉落奖励也根据等级增长
            int 实际铜钱 = Mathf.RoundToInt(模板.baseCopperMoney * 成长倍数);
            int 实际经验值 = Mathf.RoundToInt(模板.baseExperience * 成长倍数);

            var 怪物 = new 怪物数据
            {
                ID = 获取下一个ID(),
                类型 = (怪物类型)模板.monsterType,
                名称 = 模板.name,
                等级 = 实际等级,
                铜钱 = 实际铜钱,
                经验值 = 实际经验值
            };

            怪物.初始化(实际生命值, 实际攻击力, 实际防御力);
            
            return 怪物;
        }
    }
}
