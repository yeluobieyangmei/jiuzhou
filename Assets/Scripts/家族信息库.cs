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
        public int 国家排名 { get; set; }
        public int 世界排名 { get; set; }
        public int 家族王城战积分 { get; set; }
        public bool 王城战是否宣战 { get; set; } = false;
        public bool 王城战是否战斗中 { get; set; } = false;
        public List<玩家数据> 家族成员 = new List<玩家数据>();

        public void 创建一个家族(string 家族名字, 玩家数据 族长)
        {
            for (int i = 0; i < 全局变量.所有家族列表.Count; i++)
            {
                if (家族名字 == 全局变量.所有家族列表[i].家族名字)
                {
                    Debug.Log("家族名重复了");
                }
            }
            全局变量.家族ID记录++;
            this.家族ID = 全局变量.家族ID记录;
            this.族长ID = 族长.ID;
            this.副族长ID = 0;
            this.家族名字 = 家族名字;
            家族成员.Add(族长);
            Debug.Log("创建成功");
        }
    }

}
