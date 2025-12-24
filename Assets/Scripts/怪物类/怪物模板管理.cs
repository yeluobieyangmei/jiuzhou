using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using 怪物数据结构;

/// <summary>
/// 怪物模板管理器
/// 负责从服务器获取怪物模板，并提供给客户端生成怪物使用
/// </summary>
public class 怪物模板管理 : MonoBehaviour
{
    public static 怪物模板管理 实例 { get; private set; }

    [Header("接口地址")]
    private string 获取怪物模板地址 = "http://43.139.181.191:5000/api/getMonsterTemplates";

    // 怪物模板列表（从服务器获取）
    private List<服务端怪物模板数据> 怪物模板列表 = new List<服务端怪物模板数据>();

    // 是否已加载模板
    public bool 是否已加载 { get; private set; } = false;

    private void Awake()
    {
        // 单例模式
        if (实例 != null && 实例 != this)
        {
            Destroy(gameObject);
            Debug.Log("已成功获取怪物模板实例");
            return;
        }

        实例 = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("已成功获取怪物模板实例");
    }

    /// <summary>
    /// 获取怪物模板（从服务器加载）
    /// </summary>
    public void 获取怪物模板()
    {
        if (是否已加载)
        {
            Debug.Log("怪物模板已加载，无需重复加载");
            return;
        }

        StartCoroutine(发送获取怪物模板请求());
    }

    /// <summary>
    /// 根据怪物类型获取模板
    /// </summary>
    public 服务端怪物模板数据 获取模板(怪物类型 类型)
    {
        if (!是否已加载)
        {
            Debug.LogWarning("怪物模板尚未加载，请先调用 获取怪物模板()");
            return null;
        }

        int 类型值 = (int)类型;
        foreach (var 模板 in 怪物模板列表)
        {
            if (模板.monsterType == 类型值)
            {
                return 模板;
            }
        }

        Debug.LogWarning($"未找到怪物类型 {类型} 的模板");
        return null;
    }

    /// <summary>
    /// 获取所有模板
    /// </summary>
    public List<服务端怪物模板数据> 获取所有模板()
    {
        if (!是否已加载)
        {
            Debug.LogWarning("怪物模板尚未加载，请先调用 获取怪物模板()");
            return new List<服务端怪物模板数据>();
        }

        return new List<服务端怪物模板数据>(怪物模板列表);
    }

    IEnumerator 发送获取怪物模板请求()
    {
        using (UnityWebRequest 请求 = UnityWebRequest.Get(获取怪物模板地址))
        {
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取怪物模板出错: " + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("获取怪物模板响应: " + 返回文本);

                获取怪物模板响应 响应 = JsonUtility.FromJson<获取怪物模板响应>(返回文本);
                if (响应 != null && 响应.success)
                {
                    怪物模板列表.Clear();
                    foreach (var 模板 in 响应.templates)
                    {
                        怪物模板列表.Add(模板);
                    }
                    是否已加载 = true;
                    Debug.Log($"成功加载 {怪物模板列表.Count} 个怪物模板");
                }
                else
                {
                    Debug.LogError("获取怪物模板失败: " + (响应 != null ? 响应.message : "解析失败"));
                }
            }
        }
    }
}

// =================== 服务端返回的数据结构 ===================

[System.Serializable]
public class 获取怪物模板响应
{
    public bool success;
    public string message;
    public List<服务端怪物模板数据> templates;
}

[System.Serializable]
public class 服务端怪物模板数据
{
    public int id;
    public int monsterType;
    public string name;
    public int baseLevel;
    public int baseHp;
    public int baseAttack;
    public int baseDefense;
    public int baseCopperMoney;
    public int baseExperience;
    public float levelGrowthRate;  // Unity JsonUtility 不支持 decimal，使用 float
    public bool isBoss;
    public string description;
}

