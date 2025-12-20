using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using 玩家数据结构;
using 国家系统;

namespace 玩家数据结构
{
    public enum 官职枚举
    {
        国王,
        大都督,
        丞相,
        太尉,
        御史大夫,
        金吾卫,
        镖师,
        国民
    }
    public class 玩家数据
    {
        public int ID;
        public int 等级;
        public int 铜钱;
        public int 黄金;
        public string 姓名;
        public string 称号名;
        public string 性别;
        public int 家族贡献值;  // 家族贡献值
        public 玩家属性 玩家属性 = new 玩家属性();
        public 国家信息库 国家;
        public 家族信息库 家族;
        public 官职枚举 官职 { get; set; }

        public void 初始化一个玩家(string 姓名, string 性别)
        {
            this.ID = 全局变量.所有玩家数据表.Count;
            this.等级 = 1;
            this.铜钱 = 50000000;
            this.黄金 = 2000000;
            this.姓名 = 姓名;
            this.性别 = 性别;
            this.称号名 = "无";
            this.官职 = 官职枚举.国民;
            this.国家 = null;
            this.玩家属性.生命值 = this.等级 * 200;
            this.玩家属性.当前生命值 = this.玩家属性.生命值;
            //this.玩家属性.攻击力 = this.等级 * 2;
            this.玩家属性.攻击力 = 100000;
            this.玩家属性.防御力 = this.等级 * 2; ;
            计算最终属性();  //确保在初始化完成后计算属性
        }

        public void 计算最终属性()
        {
            // 先获取基础属性值
            int 基础生命值 = this.玩家属性.生命值;
            int 基础攻击力 = this.玩家属性.攻击力;
            int 基础防御力 = this.玩家属性.防御力;

            // 获取称号加成百分比（直接是百分比数值，如 2 表示 +2%）
            double 生命值加成百分比, 攻击力加成百分比, 防御力加成百分比;
            获取称号加成(out 生命值加成百分比, out 攻击力加成百分比, out 防御力加成百分比);

            // 应用加成到各个属性（将百分比转换为倍数：2% = 1 + 2/100 = 1.02）
            this.玩家属性.生命值 = (int)(基础生命值 * (1 + 生命值加成百分比 / 100.0));
            this.玩家属性.攻击力 = (int)(基础攻击力 * (1 + 攻击力加成百分比 / 100.0));
            this.玩家属性.防御力 = (int)(基础防御力 * (1 + 防御力加成百分比 / 100.0));

            //Debug.Log($"称号名：{this.称号名}，生命值加成：{生命值加成百分比}%，攻击力加成：{攻击力加成百分比}%，防御力加成：{防御力加成百分比}%");
        }

        /// <summary>
        /// 获取称号加成数据，支持为每个称号设置不同的生命值、攻击力、防御力加成
        /// 返回值是百分比数值，如 2 表示增加 2%，5 表示增加 5%
        /// </summary>
        public void 获取称号加成(out double 生命值加成百分比, out double 攻击力加成百分比, out double 防御力加成百分比)
        {
            switch (this.称号名)
            {
                case "无":
                    生命值加成百分比 = 0;  // 无加成
                    攻击力加成百分比 = 0;
                    防御力加成百分比 = 0;
                    break;
                case "散人":
                    生命值加成百分比 = 2;  // 生命值+2%
                    攻击力加成百分比 = 3;  // 攻击力+3%
                    防御力加成百分比 = 2;  // 防御力+2%
                    break;
                case "精英":
                    生命值加成百分比 = 3;  // 生命值+3%
                    攻击力加成百分比 = 4;  // 攻击力+4%
                    防御力加成百分比 = 3;  // 防御力+3%
                    break;
                case "大神":
                    生命值加成百分比 = 4;  // 生命值+4%
                    攻击力加成百分比 = 5;  // 攻击力+5%
                    防御力加成百分比 = 4;  // 防御力+4%
                    break;
                case "九五至尊":
                    生命值加成百分比 = 5;  // 生命值+5%
                    攻击力加成百分比 = 6;  // 攻击力+6%
                    防御力加成百分比 = 7;  // 防御力+7%
                    break;
                default:
                    生命值加成百分比 = 0;
                    攻击力加成百分比 = 0;
                    防御力加成百分比 = 0;
                    break;
            }
        }

        public void 加入一个国家(string 国名)
        {
            国家信息库 原国家 = this.国家;
            if (this.国家 != null)
            {
                this.国家.国家成员表.Remove(this);
            }
            国家信息库 国家信息库 = 全局方法类.获取指定名字的国家(国名);
            if (国家信息库 != null)
            {
                国家信息库.国家成员表.Add(this);
                this.国家 = 国家信息库;
            }
            else
            {
                Debug.Log("国家是空的");
            }
        }

        public void 加入一个家族(家族信息库 要加入的家族)
        {
            家族信息库 原家族 = this.家族;
            if (this.家族 != null)
            {
                this.家族.家族成员.Remove(this);
            }
            if (要加入的家族 != null)
            {
                要加入的家族.家族成员.Add(this);
                this.家族 = 要加入的家族;
                Debug.Log("加入成功!");
            }
            else
            {
                Debug.Log("国家是空的");
            }
        }

        public bool 解散家族()
        {
            if (this.家族 == null)
            {
                Debug.Log("你当前没有家族，无法解散!");
                return false;
            }

            家族信息库 当前家族 = this.家族;   // 关键：先缓存引用

            if (当前家族.王城战是否宣战)
            {
                Debug.Log("家族已报名王城战,无法解散!");
                return false;
            }

            if (当前家族.王城战是否战斗中)
            {
                Debug.Log("家族正在王城战战斗中,无法解散!");
                return false;
            }
            

            Debug.Log($"ID是：{this.ID}");
            Debug.Log($"族长ID是：{当前家族.族长ID}");
            Debug.Log($"家族成员是：{当前家族.家族成员.Count}");

            if (this.ID != 当前家族.族长ID)
            {
                Debug.Log("只有族长才可操作!");
                return false;
            }

            foreach (var member in 当前家族.家族成员)
            {
                if (member != null)
                {
                    member.家族 = null;
                }
            }
            当前家族.家族成员.Clear();

            if (this.国家 != null)
            {
                this.国家.家族成员表.Remove(当前家族);
            }
            全局变量.所有家族列表.Remove(当前家族);

            this.家族 = null;   // 明确当前玩家也不再有家族引用

            Debug.Log("已解散家族!");
            return true;
        }
    }
}

