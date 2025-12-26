using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Text;
using 玩家数据结构;
using 国家系统;

public class 国家信息显示 : MonoBehaviour
{
    [Header("UI 引用")]
    public Text 国名;
    public Text 国王;
    public Text 大都督;
    public Text 丞相;
    public Text 太尉;
    public Text 御史大夫;
    public Text 金吾卫;
    public Text 成员;
    public Text 排名;
    public Text 科技;
    public Text 国库资金;
    public Text 执政家族;
    public Button 换国按钮;
    public Button 宣战按钮;

    public 国家列表界面 国家列表界面;
    public 玩家列表显示 玩家列表显示;

    [Header("接口地址")]
    private string 获取国家信息地址 = "http://43.139.181.191:5000/api/getCountryInfo";
    private string 宣战接口地址 = "http://43.139.181.191:5000/api/declareWar";

    private void OnEnable()
    {
        刷新显示();
    }

    public void 刷新显示()
    {
        Debug.Log($"刷新国家信息 - GameObject激活状态: {gameObject.activeInHierarchy}, 激活状态: {gameObject.activeSelf}");
        
        // 检查UI组件是否准备好
        if (国名 == null || 成员 == null || 排名 == null || 国库资金 == null)
        {
            Debug.LogWarning("国家信息显示的UI组件未准备好，无法刷新");
            return;
        }
        
        // 如果GameObject未激活，尝试激活它
        if (!gameObject.activeInHierarchy)
        {
            Debug.LogWarning("国家信息显示GameObject未激活，尝试激活");
            gameObject.SetActive(true);
        }

        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null || 当前玩家.国家 == null)
        {
            Debug.LogWarning("当前玩家没有国家，无法显示国家信息");
            // 清空显示
            if (国名 != null) 国名.text = "国 名：无";
            if (成员 != null) 成员.text = "成     员：0";
            if (排名 != null) 排名.text = "排     名：无";
            if (国库资金 != null) 国库资金.text = "国库资金：0";
            return;
        }

        国家信息库 当前国家 = 当前玩家.国家;
        
        // 更新基本国家信息
        if (国名 != null) 国名.text = $"国 名：{当前国家.国名}({当前国家.国号})";
        
        // 更新官职信息（需要检查索引是否有效）
        if (国王 != null)
        {
            国王.text = 当前国家.国王ID == -1 ? "国     王：无" : 
                (当前国家.国王ID < 全局变量.所有玩家数据表.Count ? 
                    $"国     王：{全局变量.所有玩家数据表[当前国家.国王ID].姓名}" : "国     王：无");
        }
        if (大都督 != null)
        {
            大都督.text = 当前国家.大都督ID == -1 ? "大 都 督：无" : 
                (当前国家.大都督ID < 全局变量.所有玩家数据表.Count ? 
                    $"大 都 督：{全局变量.所有玩家数据表[当前国家.大都督ID].姓名}" : "大 都 督：无");
        }
        if (丞相 != null)
        {
            丞相.text = 当前国家.丞相ID == -1 ? "丞     相：无" : 
                (当前国家.丞相ID < 全局变量.所有玩家数据表.Count ? 
                    $"丞     相：{全局变量.所有玩家数据表[当前国家.丞相ID].姓名}" : "丞     相：无");
        }
        if (太尉 != null)
        {
            太尉.text = 当前国家.太尉ID == -1 ? "太     尉：无" : 
                (当前国家.太尉ID < 全局变量.所有玩家数据表.Count ? 
                    $"太     尉：{全局变量.所有玩家数据表[当前国家.太尉ID].姓名}" : "太     尉：无");
        }
        if (御史大夫 != null)
        {
            御史大夫.text = 当前国家.御史大夫ID == -1 ? "御史大夫：无" : 
                (当前国家.御史大夫ID < 全局变量.所有玩家数据表.Count ? 
                    $"御史大夫：{全局变量.所有玩家数据表[当前国家.御史大夫ID].姓名}" : "御史大夫：无");
        }
        if (金吾卫 != null)
        {
            金吾卫.text = 当前国家.金吾卫ID == -1 ? "金 吾 卫：无" : 
                (当前国家.金吾卫ID < 全局变量.所有玩家数据表.Count ? 
                    $"金 吾 卫：{全局变量.所有玩家数据表[当前国家.金吾卫ID].姓名}" : "金 吾 卫：无");
        }

        // 先显示一个占位，后面通过服务器真实人数覆盖
        if (成员 != null) 成员.text = "成     员：查询中...";

        // 更新国库资金（如果国家信息完整）
        if (国库资金 != null) 国库资金.text = $"国库资金：{当前国家.黄金}";
        
        if (执政家族 != null)
        {
            执政家族.text = 当前国家.执政家族 == null ? "执政家族：无" : $"执政家族：{当前国家.执政家族.家族名字}";
        }
        
        if (换国按钮 != null)
        {
            换国按钮.gameObject.SetActive(!(当前玩家.官职 == 官职枚举.国王));
        }

        // 更新宣战按钮显示状态（只有族长或副族长才能看到）
        if (宣战按钮 != null)
        {
            bool 可以宣战 = (当前玩家.家族 != null) &&
                           (当前玩家.家族.族长ID == 当前玩家.ID || 
                            当前玩家.家族.副族长ID == 当前玩家.ID ||
                            (当前玩家.家族职位 == "族长" || 当前玩家.家族职位 == "副族长"));
            宣战按钮.gameObject.SetActive(可以宣战);
        }

        // 向服务器请求真实成员数和排名
        StartCoroutine(获取国家成员数和排名(当前国家.国家ID));
    }

    IEnumerator 获取国家成员数和排名(int 国家ID)
    {
        if (成员 == null || 排名 == null)
            yield break;

        string json数据 = $"{{\"countryId\":{国家ID}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(获取国家信息地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取国家成员数出错: " + 请求.error);
                成员.text = "成     员：错误";
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("获取国家信息响应: " + 返回文本);

                获取国家信息响应 响应 = JsonUtility.FromJson<获取国家信息响应>(返回文本);
                if (响应 != null && 响应.success)
                {
                    成员.text = $"成     员：{响应.memberCount}";
                    排名.text = $"排     名：第{响应.rank}名";
                    
                    // 同步宣战家族信息
                }
                else
                {
                    成员.text = "成     员：未知";
                    排名.text = "排     名：未知";
                }
            }
        }
    }

    public void 点击换国按钮()
    {
        玩家数据 当前玩家 = 玩家数据管理.实例.当前玩家数据;

        if (当前玩家.家族 != null)
        {
            // 检查家族是否正在王城战中
            if (当前玩家.家族.王城战是否战斗中)
            {
                通用提示框.显示("家族正在战斗中,不可操作!");
                return;
            }

            通用提示框.显示("请先退出或解散家族!");
            return;
        }
        国家列表界面.列表显示类型 = 显示类型.更换国家;
        国家列表界面.gameObject.SetActive(true);
    }

    public void 点击排名按钮()
    {
        国家列表界面.列表显示类型 = 显示类型.国家排名;
        国家列表界面.gameObject.SetActive(true);
    }

    public void 点击显示国家成员列表()
    {
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null || 当前玩家.国家 == null)
        {
            Debug.LogWarning("当前玩家没有国家，无法显示国家成员列表");
            return;
        }

        玩家列表显示.UI标题.text = "国家成员";
        玩家列表显示.当前显示类型 = 玩家列表显示.显示类型.国家不任命官员;
        玩家列表显示.当前国家 = 当前玩家.国家; // 设置当前国家
        玩家列表显示.gameObject.SetActive(true);
    }

    public void 点击宣战按钮()
    {
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null)
        {
            通用提示框.显示("无法获取玩家信息");
            return;
        }

        国家信息库 当前国家 = 当前玩家.国家;
        if (当前国家 == null)
        {
            通用提示框.显示("无法获取国家信息");
            return;
        }

        if (当前玩家.家族 == null || 当前玩家.家族.家族ID <= 0)
        {
            通用提示框.显示("请先加入或创建家族!");
            return;
        }

        // 检查权限
        if (当前玩家.家族.族长ID != 当前玩家.ID && 当前玩家.家族.副族长ID != 当前玩家.ID)
        {
            通用提示框.显示("族长或副族长才可宣战!");
            return;
        }

        // 发送宣战请求
        StartCoroutine(发送宣战请求());
    }

    IEnumerator 发送宣战请求()
    {
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null || 当前玩家.国家 == null)
        {
            通用提示框.显示("无法获取玩家信息");
            yield break;
        }

        if (当前玩家.家族 == null || 当前玩家.家族.家族ID <= 0)
        {
            通用提示框.显示("您不属于任何家族，无法宣战");
            yield break;
        }

        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId <= 0)
        {
            通用提示框.显示("未登录，无法宣战");
            yield break;
        }

        // 构建请求数据：包含账号ID、国家ID、家族ID
        string json数据 = $"{{\"accountId\":{accountId},\"countryId\":{当前玩家.国家.国家ID},\"clanId\":{当前玩家.家族.家族ID}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(宣战接口地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("宣战请求失败: " + 请求.error);
                通用提示框.显示("宣战请求失败: " + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("宣战响应: " + 返回文本);

                宣战响应 响应 = JsonUtility.FromJson<宣战响应>(返回文本);
                if (响应 != null && 响应.success)
                {
                    if (响应.bothClansReady)
                    {
                        通用提示框.显示("宣战成功! 战场将在30秒后开启!");
                    }
                    else
                    {
                        通用提示框.显示("宣战成功! 等待另一个家族宣战...");
                    }
                    // 刷新国家信息
                    刷新显示();
                }
                else
                {
                    通用提示框.显示("宣战失败: " + (响应 != null ? 响应.message : "未知错误"));
                }
            }
        }
    }


}

// =================== 服务端获取国家信息返回的数据结构 ===================

[System.Serializable]
public class 获取国家信息响应
{
    public bool success;
    public string message;
    public int memberCount;
    public int rank;
}

[System.Serializable]
public class 宣战响应
{
    public bool success;
    public string message;
    public bool bothClansReady; // 两个家族是否都就绪
}
