using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
namespace 怪物数据结构
{
    public static class 怪物管理器
    {
        private static Dictionary<int, 怪物数据> 活动怪物表 = new Dictionary<int, 怪物数据>();
        private static Dictionary<怪物类型, Queue<怪物数据>> 怪物对象池 = new Dictionary<怪物类型, Queue<怪物数据>>();

        static 怪物管理器()
        {
            // 初始化对象池
            foreach (怪物类型 类型 in Enum.GetValues(typeof(怪物类型)))
            {
                怪物对象池[类型] = new Queue<怪物数据>();
            }
        }

        public static 怪物数据 生成怪物(怪物类型 类型, int 等级, int 生命值, int 攻击力, int 防御力)
        {
            var 池 = 怪物对象池[类型];
            怪物数据 怪物;

            if (池.Count > 0)
            {
                怪物 = 池.Dequeue();
                怪物.重置(类型, 等级, 生命值, 攻击力, 防御力);
            }
            else
            {
                怪物 = 怪物数据.创建(类型, 等级, 生命值, 攻击力, 防御力);
            }

            活动怪物表[怪物.ID] = 怪物;
            return 怪物;
        }

        public static void 回收怪物(int 怪物ID)
        {
            if (活动怪物表.TryGetValue(怪物ID, out var 怪物))
            {
                Debug.Log($"已回收怪物:{怪物.名称}");
                活动怪物表.Remove(怪物ID);
                怪物对象池[怪物.类型].Enqueue(怪物);
            }
        }

        public static bool 获取怪物(int 怪物ID, out 怪物数据 怪物)
        {
            return 活动怪物表.TryGetValue(怪物ID, out 怪物);
        }

        public static void 清理所有怪物()
        {
            foreach (var 怪物 in 活动怪物表.Values)
            {
                怪物对象池[怪物.类型].Enqueue(怪物);
            }
            活动怪物表.Clear();
        }
    }

}
