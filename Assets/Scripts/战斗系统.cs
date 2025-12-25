using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using 怪物数据结构;
using 玩家数据结构;

public enum 战斗模式
{
    战场模式,  // 单次攻击，造成伤害后结束（用于王城战等战场）
    PK模式     // 回合制互砍，直至一方死亡（用于怪物PK、玩家间决斗等）
}

/// <summary>
/// 战斗系统（静态类）
/// 实现玩家和怪物的回合制互砍，直至一方死亡
/// </summary>
public static class 战斗系统
{
    /// <summary>
    /// 开始战斗（回合制互砍）- 联网版本
    /// 从服务器获取玩家数据，战斗结果同步到服务器
    /// </summary>
    /// <param name="玩家">玩家数据（从服务器获取）</param>
    /// <param name="怪物">怪物数据</param>
    /// <returns>(是否胜利, 被击败的怪物)</returns>
    public static (bool 是否胜利, 怪物数据 被击败的怪物) 开始战斗(玩家数据 玩家, 怪物数据 怪物)
    {
        if (玩家 == null || 怪物 == null)
        {
            Debug.LogError("战斗系统：玩家或怪物数据为空");
            return (false, null);
        }

        // 清空之前的战斗日志
        if (战斗日志管理器.实例 != null)
        {
            战斗日志管理器.实例.清空日志();
        }

        // 记录初始状态
        int 玩家初始生命值 = 玩家.玩家属性.当前生命值;
        int 怪物初始生命值 = 怪物.属性.当前生命值;

        // 添加战斗开始日志
        添加战斗日志($"战斗开始！玩家 {玩家.姓名} VS {怪物.名称}", 战斗日志管理器.日志类型.系统);
        添加战斗日志($"玩家生命值：{玩家.玩家属性.当前生命值}/{玩家.玩家属性.生命值}", 战斗日志管理器.日志类型.普通);
        添加战斗日志($"怪物生命值：{怪物.属性.当前生命值}/{怪物.属性.生命值}", 战斗日志管理器.日志类型.普通);

        // 回合制战斗循环
        int 回合数 = 0;
        const int 最大回合数 = 100; // 防止无限循环

        while (玩家.玩家属性.当前生命值 > 0 && 怪物.属性.当前生命值 > 0 && 回合数 < 最大回合数)
        {
            回合数++;
            添加战斗日志($"\n=== 第 {回合数} 回合 ===", 战斗日志管理器.日志类型.重要);

            // 玩家攻击怪物
            int 玩家伤害 = 计算伤害(玩家.玩家属性.攻击力, 怪物.属性.防御力);
            怪物.属性.当前生命值 = Mathf.Max(0, 怪物.属性.当前生命值 - 玩家伤害);
            添加战斗日志($"{玩家.姓名} 攻击 {怪物.名称}，造成 {玩家伤害} 点伤害", 战斗日志管理器.日志类型.伤害);
            添加战斗日志($"{怪物.名称} 剩余生命值：{怪物.属性.当前生命值}/{怪物.属性.生命值}", 战斗日志管理器.日志类型.普通);

            // 检查怪物是否死亡
            if (怪物.属性.当前生命值 <= 0)
            {
                添加战斗日志($"\n{怪物.名称} 被击败！", 战斗日志管理器.日志类型.重要);
                添加战斗日志($"战斗胜利！获得 {怪物.经验值} 经验值，{怪物.铜钱} 铜钱", 战斗日志管理器.日志类型.系统);
                
                // 联网版本：同步战斗结果到服务器
                同步战斗结果到服务器(玩家, 怪物);
                
                return (true, 怪物);
            }

            // 怪物攻击玩家
            int 怪物伤害 = 计算伤害(怪物.属性.攻击力, 玩家.玩家属性.防御力);
            玩家.玩家属性.当前生命值 = Mathf.Max(0, 玩家.玩家属性.当前生命值 - 怪物伤害);
            添加战斗日志($"{怪物.名称} 攻击 {玩家.姓名}，造成 {怪物伤害} 点伤害", 战斗日志管理器.日志类型.伤害);
            添加战斗日志($"{玩家.姓名} 剩余生命值：{玩家.玩家属性.当前生命值}/{玩家.玩家属性.生命值}", 战斗日志管理器.日志类型.普通);

            // 检查玩家是否死亡
            if (玩家.玩家属性.当前生命值 <= 0)
            {
                添加战斗日志($"\n{玩家.姓名} 被击败！", 战斗日志管理器.日志类型.重要);
                添加战斗日志($"战斗失败！", 战斗日志管理器.日志类型.系统);
                // 恢复玩家生命值（战斗失败不扣除生命值，或者可以设置为1）
                玩家.玩家属性.当前生命值 = 1;
                return (false, null);
            }
        }

        // 超过最大回合数，判定为平局（理论上不应该发生）
        if (回合数 >= 最大回合数)
        {
            添加战斗日志($"战斗超时，判定为平局", 战斗日志管理器.日志类型.系统);
            // 恢复初始状态
            玩家.玩家属性.当前生命值 = 玩家初始生命值;
            怪物.属性.当前生命值 = 怪物初始生命值;
            return (false, null);
        }

        return (false, null);
    }

    /// <summary>
    /// 计算伤害（攻击力 - 防御力，最少造成1点伤害）
    /// </summary>
    private static int 计算伤害(int 攻击力, int 防御力)
    {
        int 伤害 = 攻击力 - 防御力;
        return Mathf.Max(1, 伤害); // 至少造成1点伤害
    }

    /// <summary>
    /// 添加战斗日志（线程安全）
    /// </summary>
    private static void 添加战斗日志(string 内容, 战斗日志管理器.日志类型 类型 = 战斗日志管理器.日志类型.普通)
    {
        if (战斗日志管理器.实例 != null)
        {
            战斗日志管理器.实例.添加日志(内容, 类型);
        }
        else
        {
            Debug.Log($"[战斗日志] {内容}");
        }
    }

    /// <summary>
    /// 同步战斗结果到服务器（获得经验值和铜钱）
    /// </summary>
    private static void 同步战斗结果到服务器(玩家数据 玩家, 怪物数据 怪物)
    {
        // 使用玩家数据管理来执行协程
        if (玩家数据管理.实例 != null)
        {
            玩家数据管理.实例.StartCoroutine(同步战斗结果到服务器协程(玩家, 怪物));
        }
        else
        {
            Debug.LogWarning("玩家数据管理实例不存在，无法同步战斗结果到服务器");
        }
    }

    /// <summary>
    /// 同步战斗结果到服务器协程
    /// </summary>
    private static IEnumerator 同步战斗结果到服务器协程(玩家数据 玩家, 怪物数据 怪物)
    {
        string 接口地址 = "http://43.139.181.191:5000/api/battleResult";
        string json数据 = $"{{\"playerId\":{玩家.ID},\"monsterType\":{(int)怪物.类型},\"experience\":{怪物.经验值},\"copperMoney\":{怪物.铜钱},\"currentHp\":{玩家.玩家属性.当前生命值},\"victory\":true}}";
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json数据);

        using (UnityEngine.Networking.UnityWebRequest 请求 = new UnityEngine.Networking.UnityWebRequest(接口地址, "POST"))
        {
            请求.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityEngine.Networking.UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityEngine.Networking.UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("同步战斗结果失败: " + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("战斗结果同步响应: " + 返回文本);

                战斗结果响应 响应 = JsonUtility.FromJson<战斗结果响应>(返回文本);
                if (响应 != null && 响应.success)
                {
                    // 更新本地玩家数据
                    玩家.经验值 = 响应.newExperience;
                    玩家.铜钱 = 响应.newCopperMoney;
                    玩家.玩家属性.当前生命值 = 响应.newCurrentHp;
                    
                    // 如果升级了，重新获取玩家数据
                    if (响应.levelUp)
                    {
                        添加战斗日志($"恭喜！等级提升到 {响应.newLevel} 级！", 战斗日志管理器.日志类型.系统);
                        // 重新获取玩家数据以更新属性
                        if (玩家数据管理.实例 != null)
                        {
                            玩家数据管理.实例.获取玩家数据(PlayerPrefs.GetInt("AccountId", -1), false);
                        }
                    }
                    else
                    {
                        // 只更新UI显示
                        if (玩家数据管理.实例 != null && 玩家数据管理.实例.当前玩家数据 != null)
                        {
                            玩家数据管理.实例.当前玩家数据.经验值 = 响应.newExperience;
                            玩家数据管理.实例.当前玩家数据.铜钱 = 响应.newCopperMoney;
                            玩家数据管理.实例.当前玩家数据.玩家属性.当前生命值 = 响应.newCurrentHp;
                        }
                    }
                }
                else
                {
                    Debug.LogError("战斗结果同步失败: " + (响应 != null ? 响应.message : "解析失败"));
                }
            }
        }
    }
}

// =================== 战斗结果响应数据类 ===================

[System.Serializable]
public class 战斗结果响应
{
    public bool success;
    public string message;
    public int newExperience;
    public int newCopperMoney;
    public int newCurrentHp;
    public bool levelUp;
    public int newLevel;
}
