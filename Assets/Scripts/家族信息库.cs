using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using 玩家数据结构;

namespace 国家系统
{
    public class 家族信息库
    {
        public string 家族名字 { get; set; }
        public int 家族ID { get; set; }
        public int 族长ID = -1;
        public int 副族长ID = -1;
        public int 家族等级 = 1;
        public int 家族繁荣值 { get; set; }
        public int 家族资金 { get; set; }
        
        // 排名变量：保留用于客户端显示，但值应该从服务器实时获取
        // 注意：排名是动态的，不应该存储在数据库中，应该由服务器实时计算
        public int 国家排名 { get; set; }  // 从服务器获取，用于显示
        public int 世界排名 { get; set; }  // 从服务器获取，用于显示
        
        public int 家族王城战积分 { get; set; }
        public bool 王城战是否宣战 { get; set; } = false;
        public bool 王城战是否战斗中 { get; set; } = false;
        public List<玩家数据> 家族成员 = new List<玩家数据>();
        public 国家信息库 家族国家;

        // 家族升级相关常量
        private const int 初始人数上限 = 10;  // 1级家族人数上限
        private const int 每级增加人数 = 10;   // 每升1级增加的人数上限
        private const int 最高等级 = 5;        // 最高家族等级
        private const int 升级消耗资金 = 50000; // 每次升级消耗的家族资金

        /// <summary>
        /// 根据家族等级计算当前人数上限
        /// 公式：初始人数上限 + (家族等级 - 1) * 每级增加人数
        /// 1级：10人，2级：20人，3级：30人，4级：40人，5级：50人
        /// </summary>
        public int 获取人数上限()
        {
            if (家族等级 < 1) 家族等级 = 1;
            if (家族等级 > 最高等级) 家族等级 = 最高等级;
            
            return 初始人数上限 + (家族等级 - 1) * 每级增加人数;
        }

        /// <summary>
        /// 获取当前家族成员数量
        /// </summary>
        public int 获取当前人数()
        {
            return 家族成员 != null ? 家族成员.Count : 0;
        }

        /// <summary>
        /// 检查是否可以添加新成员
        /// </summary>
        public bool 是否可以添加成员()
        {
            return 获取当前人数() < 获取人数上限();
        }

        /// <summary>
        /// 检查是否可以升级家族
        /// </summary>
        public bool 是否可以升级()
        {
            // 检查是否已达到最高等级
            if (家族等级 >= 最高等级)
            {
                return false;
            }

            // 检查家族资金是否足够
            if (家族资金 < 升级消耗资金)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 升级家族等级
        /// </summary>
        /// <returns>升级是否成功</returns>
        public bool 升级家族()
        {
            if (!是否可以升级())
            {
                Debug.LogWarning($"家族升级失败：已达到最高等级({最高等级})或资金不足(需要{升级消耗资金})");
                return false;
            }

            // 扣除升级费用
            家族资金 -= 升级消耗资金;
            
            // 提升等级
            家族等级++;
            
            Debug.Log($"家族升级成功！当前等级：{家族等级}，人数上限：{获取人数上限()}，剩余资金：{家族资金}");
            return true;
        }

        /// <summary>
        /// 获取升级所需资金
        /// </summary>
        public int 获取升级所需资金()
        {
            return 升级消耗资金;
        }

        /// <summary>
        /// 获取最高等级
        /// </summary>
        public int 获取最高等级()
        {
            return 最高等级;
        }

        public void 创建一个家族(string 家族名字, 玩家数据 族长)
        {
            for (int i = 0; i < 全局变量.所有家族列表.Count; i++)
            {
                if (家族名字 == 全局变量.所有家族列表[i].家族名字)
                {
                    Debug.Log("家族名重复了");
                    return;
                }
            }
            全局变量.家族ID记录++;
            this.家族ID = 全局变量.家族ID记录;
            this.族长ID = 族长.ID;
            this.副族长ID = -1;  // 初始时没有副族长，使用-1而不是0
            this.家族名字 = 家族名字;
            this.家族等级 = 1;  // 初始等级为1
            this.家族资金 = 0;  // 初始资金为0
            this.家族繁荣值 = 0; // 初始繁荣值为0
            this.家族国家 = 族长.国家;
            if (家族成员 == null)
            {
                家族成员 = new List<玩家数据>();
            }
            家族成员.Add(族长);
            Debug.Log($"家族创建成功！家族名：{家族名字}，等级：{家族等级}，人数上限：{获取人数上限()}");
        }
    }

}
