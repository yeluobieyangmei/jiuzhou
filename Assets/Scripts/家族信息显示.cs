using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Text;
using 玩家数据结构;
using 国家系统;

public class 家族信息显示 : MonoBehaviour
{
    public Button 退出家族按钮;
    public Button 解散家族按钮;
    
    [Header("UI组件引用")]
    public Text 家族名称文本;
    public Text 家族族长文本;
    public Text 我的职位文本;
    public Text 家族等级文本;
    public Text 家族人数文本;
    public Text 家族繁荣文本;
    public Text 国内排名文本;
    public Text 世界排名文本;
    public Text 家族资金文本;
    
    [Header("接口地址")]
    private string 获取家族信息地址 = "http://43.139.181.191:5000/api/getClanInfo";
    
    //public 显示家族列表 显示家族列表;  显示家族列表尚未实现，此处先写补上格式 后续完善
    
    private void Start()
    {
        // 如果UI组件未在Inspector中赋值，尝试从子对象自动获取（兼容原有方式）
        自动获取UI组件();
    }

    /// <summary>
    /// 自动获取UI组件（如果未在Inspector中赋值）
    /// </summary>
    void 自动获取UI组件()
    {
        if (家族名称文本 == null && transform.childCount > 0) 家族名称文本 = transform.GetChild(0).GetComponent<Text>();
        if (家族族长文本 == null && transform.childCount > 1) 家族族长文本 = transform.GetChild(1).GetComponent<Text>();
        if (我的职位文本 == null && transform.childCount > 2) 我的职位文本 = transform.GetChild(2).GetComponent<Text>();
        if (家族等级文本 == null && transform.childCount > 3) 家族等级文本 = transform.GetChild(3).GetComponent<Text>();
        if (家族人数文本 == null && transform.childCount > 4) 家族人数文本 = transform.GetChild(4).GetComponent<Text>();
        if (家族繁荣文本 == null && transform.childCount > 5) 家族繁荣文本 = transform.GetChild(5).GetComponent<Text>();
        if (国内排名文本 == null && transform.childCount > 6) 国内排名文本 = transform.GetChild(6).GetComponent<Text>();
        if (世界排名文本 == null && transform.childCount > 7) 世界排名文本 = transform.GetChild(7).GetComponent<Text>();
        if (家族资金文本 == null && transform.childCount > 8) 家族资金文本 = transform.GetChild(8).GetComponent<Text>();
    }

    public void OnEnable()
    {
        刷新显示();
    }

    public void 刷新显示()
    {
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null)
        {
            Debug.LogWarning("无法获取当前玩家数据，无法显示家族信息");
            return;
        }

        if (当前玩家.家族 == null || 当前玩家.家族.家族ID <= 0)
        {
            Debug.LogWarning("当前玩家没有家族，无法显示家族信息");
            // 清空显示
            if (家族名称文本 != null) 家族名称文本.text = "家族名称：无";
            if (家族族长文本 != null) 家族族长文本.text = "家族族长：无";
            if (我的职位文本 != null) 我的职位文本.text = "我的职位：无";
            if (家族等级文本 != null) 家族等级文本.text = "家族等级：0";
            if (家族人数文本 != null) 家族人数文本.text = "家族人数：0";
            if (家族繁荣文本 != null) 家族繁荣文本.text = "家族繁荣：0";
            if (国内排名文本 != null) 国内排名文本.text = "国内排名：无";
            if (世界排名文本 != null) 世界排名文本.text = "世界排名：无";
            if (家族资金文本 != null) 家族资金文本.text = "家族资金：0";
            return;
        }

        // 从服务器获取家族详细信息
        StartCoroutine(获取家族详细信息(当前玩家.家族.家族ID, 当前玩家.ID));
    }

    /// <summary>
    /// 从服务器获取家族详细信息
    /// </summary>
    IEnumerator 获取家族详细信息(int 家族ID, int 玩家ID)
    {
        string json数据 = $"{{\"clanId\":{家族ID},\"playerId\":{玩家ID}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(获取家族信息地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取家族信息出错: " + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("获取家族信息响应: " + 返回文本);

                获取家族信息响应 响应 = JsonUtility.FromJson<获取家族信息响应>(返回文本);
                if (响应 != null && 响应.success && 响应.data != null)
                {
                    更新家族信息显示(响应.data);
                }
                else
                {
                    Debug.LogError("获取家族信息失败: " + (响应 != null ? 响应.message : "解析错误"));
                }
            }
        }
    }

    /// <summary>
    /// 更新家族信息显示
    /// </summary>
    void 更新家族信息显示(家族信息数据 家族信息)
    {
        // 更新UI显示
        if (家族名称文本 != null) 家族名称文本.text = $"家族名称：{家族信息.name}";
        if (家族族长文本 != null) 家族族长文本.text = $"家族族长：{家族信息.leaderName}";
        if (我的职位文本 != null) 我的职位文本.text = $"我的职位：{家族信息.playerRole}";
        if (家族等级文本 != null) 家族等级文本.text = $"家族等级：{家族信息.level}";
        if (家族人数文本 != null) 家族人数文本.text = $"家族人数：{家族信息.memberCount}";
        if (家族繁荣文本 != null) 家族繁荣文本.text = $"家族繁荣：{家族信息.prosperity}";
        if (国内排名文本 != null) 国内排名文本.text = $"国内排名：第{家族信息.countryRank}名";
        if (世界排名文本 != null) 世界排名文本.text = $"世界排名：第{家族信息.worldRank}名";
        if (家族资金文本 != null) 家族资金文本.text = $"家族资金：{家族信息.funds}";

        // 更新按钮显示（根据玩家职位）
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 != null)
        {
            bool 是族长 = 家族信息.playerRole == "族长";
            if (退出家族按钮 != null) 退出家族按钮.gameObject.SetActive(!是族长);
            if (解散家族按钮 != null) 解散家族按钮.gameObject.SetActive(是族长);
        }
    }

    public void 点击加入家族()
    {
        //显示家族列表.gameObject.SetActive(true);   显示家族列表尚未实现，此处先写补上格式 后续完善
    }

    public void 解散家族()
    {
        // 先不用管，后续完善
        // 玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        // if (当前玩家 == null || 当前玩家.家族 == null)
        // {
        //     return;
        // }
        // TODO: 实现解散家族的网络请求
    }

    public void 退出家族()
    {
        // 先不用管，后续完善
        // 玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        // if (当前玩家 == null || 当前玩家.家族 == null)
        // {
        //     通用提示框.显示("当前没有家族!");
        //     return;
        // }
        // TODO: 实现退出家族的网络请求
    }

    public void 捐献()
    {
        // 先不用管，后续完善
        // 玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        // if (当前玩家 == null || 当前玩家.家族 == null)
        // {
        //     return;
        // }
        // TODO: 实现捐献的网络请求
    }
}

// =================== 响应数据类 ===================

[System.Serializable]
public class 获取家族信息响应
{
    public bool success;
    public string message;
    public 家族信息数据 data;
}

[System.Serializable]
public class 家族信息数据
{
    public int id;
    public string name;
    public int level;
    public int leaderId;
    public string leaderName;
    public int memberCount;
    public int prosperity;
    public int funds;
    public int countryRank;
    public int worldRank;
    public string playerRole;
}
