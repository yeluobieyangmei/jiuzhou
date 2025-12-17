using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using 玩家数据结构;

namespace 国家系统
{
    public class 国家信息库
    {
        public int 国王ID = -1;
        public int 大都督ID = -1;
        public int 丞相ID = -1;
        public int 太尉ID = -1;
        public int 御史大夫ID = -1;
        public int 金吾卫ID = -1;
        public int 国家ID;
        public string 国名;
        public string 国号;
        public string 国家宣言;
        public string 国家公告;
        public int 铜钱;
        public int 粮食;
        public int 黄金;
        public 家族信息库 执政家族 { get; set; }
        public 家族信息库 宣战家族1 { get; set; }
        public 家族信息库 宣战家族2 { get; set; }
        public List<玩家数据> 国家成员表 = new List<玩家数据>();
        public List<家族信息库> 家族成员表 = new List<家族信息库>();

        public void 创建一个新的国家(string 国名, string 国号, string 宣言, string 公告)
        {
            this.国名 = 国名;
            this.国号 = 国号;
            全局变量.国家ID记录++;
            this.国家ID = 全局变量.国家ID记录;
            全局变量.所有国家列表.Add(this);
        }
    }
}

