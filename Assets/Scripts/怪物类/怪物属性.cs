using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace 怪物数据结构
{
    /// <summary>
    /// 怪物战斗属性（只包含战斗相关属性）
    /// </summary>
    public class 怪物属性
    {
        public int 生命值 { get; set; }
        public int 当前生命值 { get; set; }
        public int 攻击力 { get; set; }
        public int 防御力 { get; set; }
    }
}
