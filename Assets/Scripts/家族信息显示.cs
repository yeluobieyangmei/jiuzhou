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
    private string 解散家族地址 = "http://43.139.181.191:5000/api/disbandClan";
    
    [Header("其他引用")]
    public 家族显示判断 家族显示判断;
    public GameObject 无家族界面;
    
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
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null)
        {
            通用提示框.显示("无法获取当前玩家数据，无法解散家族");
            return;
        }

        if (当前玩家.家族 == null || 当前玩家.家族.家族ID <= 0)
        {
            通用提示框.显示("当前玩家没有家族，无法解散家族");
            return;
        }

        // 检查玩家是否是族长（客户端预检查）
        // 注意：服务端会再次验证，这里只是提前提示
        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId <= 0)
        {
            通用提示框.显示("解散家族失败：未找到账号ID，请先登录");
            return;
        }

        // 发送解散家族请求
        StartCoroutine(发送解散家族请求(accountId));
    }

    /// <summary>
    /// 发送解散家族请求到服务器
    /// </summary>
    IEnumerator 发送解散家族请求(int accountId)
    {
        string json数据 = $"{{\"accountId\":{accountId}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(解散家族地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("解散家族出错: " + 请求.error);
                // 可以在这里显示错误提示
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("解散家族响应: " + 返回文本);

                解散家族响应 响应 = JsonUtility.FromJson<解散家族响应>(返回文本);
                if (响应 != null)
                {
                    if (响应.success)
                    {
                        通用提示框.显示("家族解散成功！");

                        // 刷新玩家数据（会更新家族信息，家族应该变为null）
                        if (玩家数据管理.实例 != null)
                        {
                            玩家数据管理.实例.获取玩家数据(accountId);
                        }

                        // 直接关闭家族信息显示界面，显示无家族界面
                        无家族界面.SetActive(true);
                        this.gameObject.SetActive(false);
                    }
                    else
                    {
                        通用提示框.显示("解散家族失败: " + 响应.message);
                        // 可以在这里显示错误提示
                    }
                }
                else
                {
                    通用提示框.显示("解散家族失败：解析响应失败");
                }
            }
        }
    }

    /// <summary>
    /// 延迟刷新家族显示判断（等待玩家数据更新完成）
    /// 使用加载动画显示等待过程，确保组件在延迟期间保持激活状态
    /// </summary>
    static IEnumerator 延迟刷新家族显示判断(家族显示判断 家族显示判断组件, float 延迟秒数)
    {
        Debug.Log($"准备等待{延迟秒数}秒后刷新家族显示判断");
        
        // 显示加载动画，让用户知道正在处理
        if (玩家数据管理.实例 != null && 玩家数据管理.实例.加载动画组件 != null)
        {
            玩家数据管理.实例.加载动画组件.开始加载动画(延迟秒数, "正在更新家族信息...");
        }
        
        // 等待指定时间
        yield return new WaitForSeconds(延迟秒数);
        
        Debug.Log($"{延迟秒数}秒等待完成，准备刷新家族显示判断");
        
        // 确保组件和GameObject都存在且激活
        if (家族显示判断组件 != null && 家族显示判断组件.gameObject != null)
        {
            // 如果GameObject未激活，尝试激活它
            if (!家族显示判断组件.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("家族显示判断GameObject未激活，尝试激活");
                家族显示判断组件.gameObject.SetActive(true);
            }
            
            Debug.Log("开始刷新家族显示判断");
            家族显示判断组件.刷新显示();
        }
        else
        {
            Debug.LogError("家族显示判断组件为null或GameObject为null，无法刷新！");
        }
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

[System.Serializable]
public class 解散家族响应
{
    public bool success;
    public string message;
}
