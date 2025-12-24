using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Text;
using System;
using 玩家数据结构;

public class 家族日志显示 : MonoBehaviour
{
    private string 获取家族日志地址 = "http://43.139.181.191:5000/api/getClanLogs";
    public Transform 父对象;
    public GameObject 要克隆的对象;
    List<GameObject> 克隆池 = new List<GameObject>();

    // 分页相关变量
    private List<家族日志数据> 所有日志列表 = new List<家族日志数据>();
    private int 当前页数 = 1;
    private const int 每页显示数量 = 20;

    // UI组件引用（不在脚本中初始化，由用户在Unity中手动拖拽赋值）
    public Button 上翻页按钮;
    public Button 下翻页按钮;
    public Text 页码文本;

    private void Start()
    {
        // 绑定按钮事件（如果按钮已赋值）
        if (上翻页按钮 != null)
        {
            上翻页按钮.onClick.AddListener(上翻页);
        }

        if (下翻页按钮 != null)
        {
            下翻页按钮.onClick.AddListener(下翻页);
        }
    }

    public void OnEnable()
    {
        // 获取当前玩家的家族ID并加载日志
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 != null && 当前玩家.家族 != null && 当前玩家.家族.家族ID > 0)
        {
            当前页数 = 1; // 重置到第一页
            StartCoroutine(加载家族日志(当前玩家.家族.家族ID));
        }
        else
        {
            Debug.LogWarning("[家族日志显示] 当前玩家没有家族，无法加载日志");
        }
    }

    /// <summary>
    /// 加载家族日志
    /// </summary>
    private IEnumerator 加载家族日志(int clanId)
    {
        // 构建请求
        string json数据 = $"{{\"clanId\":{clanId}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(获取家族日志地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取家族日志出错: " + 请求.error);
                通用提示框.显示("获取家族日志失败：" + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("获取家族日志响应: " + 返回文本);

                try
                {
                    获取家族日志响应 响应 = JsonUtility.FromJson<获取家族日志响应>(返回文本);
                    if (响应 != null && 响应.success)
                    {
                        // 保存所有日志
                        所有日志列表 = 响应.logs ?? new List<家族日志数据>();
                        
                        // 显示第一页
                        刷新显示();
                    }
                    else
                    {
                        通用提示框.显示(响应 != null ? 响应.message : "获取家族日志失败");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("解析家族日志响应失败: " + ex.Message);
                    通用提示框.显示("解析家族日志失败");
                }
            }
        }
    }

    public void 刷新显示()
    {
        if (要克隆的对象 == null || 父对象 == null)
        {
            Debug.LogError("[家族日志显示] 要克隆的对象或父对象未设置");
            return;
        }

        // 隐藏模板对象
        要克隆的对象.gameObject.SetActive(false);

        // 清空现有克隆对象
        foreach (var obj in 克隆池)
        {
            if (obj != null) Destroy(obj);
        }
        克隆池.Clear();

        if (所有日志列表 == null || 所有日志列表.Count == 0)
        {
            // 如果没有日志，更新分页UI后返回
            更新分页UI();
            return;
        }

        // 计算总页数
        int 总页数 = Mathf.CeilToInt((float)所有日志列表.Count / 每页显示数量);
        if (总页数 == 0) 总页数 = 1;

        // 确保当前页数在有效范围内
        if (当前页数 < 1) 当前页数 = 1;
        if (当前页数 > 总页数) 当前页数 = 总页数;

        // 计算当前页的起始索引和结束索引
        int 起始索引 = (当前页数 - 1) * 每页显示数量;
        int 结束索引 = Mathf.Min(起始索引 + 每页显示数量, 所有日志列表.Count);

        // 显示当前页的日志
        for (int i = 起始索引; i < 结束索引; i++)
        {
            var 日志 = 所有日志列表[i];
            
            // 克隆日志项
            GameObject 克隆对象 = Instantiate(要克隆的对象, 父对象);
            克隆对象.SetActive(true);
            
            // 获取Text组件并设置内容
            Text 文本组件 = 克隆对象.transform.GetChild(0).GetComponent<Text>();
            if (文本组件 != null)
            {
                // 格式化时间：HH:mm
                string 时间 = 日志.CreatedAt.ToString("HH:mm");
                // 显示描述（已经格式化好的文本）
                文本组件.text = $"{时间} {日志.description}";
            }
            
            克隆池.Add(克隆对象);
        }

        // 更新分页UI
        更新分页UI();
    }

    /// <summary>
    /// 更新分页UI（页码显示和按钮状态）
    /// </summary>
    private void 更新分页UI()
    {
        if (所有日志列表 == null || 所有日志列表.Count == 0)
        {
            if (页码文本 != null)
            {
                页码文本.text = "0/0";
            }
            if (上翻页按钮 != null)
            {
                上翻页按钮.interactable = false;
            }
            if (下翻页按钮 != null)
            {
                下翻页按钮.interactable = false;
            }
            return;
        }

        // 计算总页数
        int 总页数 = Mathf.CeilToInt((float)所有日志列表.Count / 每页显示数量);
        if (总页数 == 0) 总页数 = 1;

        // 更新页码显示
        if (页码文本 != null)
        {
            页码文本.text = $"{当前页数}/{总页数}";
        }

        // 更新按钮状态
        if (上翻页按钮 != null)
        {
            上翻页按钮.interactable = (当前页数 > 1);
        }
        if (下翻页按钮 != null)
        {
            下翻页按钮.interactable = (当前页数 < 总页数);
        }
    }

    /// <summary>
    /// 上翻页
    /// </summary>
    private void 上翻页()
    {
        if (当前页数 > 1)
        {
            当前页数--;
            刷新显示();
        }
    }

    /// <summary>
    /// 下翻页
    /// </summary>
    private void 下翻页()
    {
        if (所有日志列表 == null || 所有日志列表.Count == 0) return;

        int 总页数 = Mathf.CeilToInt((float)所有日志列表.Count / 每页显示数量);
        if (当前页数 < 总页数)
        {
            当前页数++;
            刷新显示();
        }
    }
}

// =================== 响应数据类 ===================

[System.Serializable]
public class 获取家族日志响应
{
    public bool success;
    public string message;
    public List<家族日志数据> logs;
}

[System.Serializable]
public class 家族日志数据
{
    public int id;
    public int clanId;
    public string operationType;
    public int? operatorId;
    public string operatorName;
    public int? targetPlayerId;
    public string targetPlayerName;
    public string details;
    public string description;
    public string createdAt; // JSON中可能是字符串格式

    // 用于Unity的DateTime转换
    public DateTime CreatedAt
    {
        get
        {
            if (DateTime.TryParse(createdAt, out DateTime result))
            {
                return result;
            }
            return DateTime.Now;
        }
    }
}

