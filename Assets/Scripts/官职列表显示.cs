using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Text;
using 国家系统;
using 玩家数据结构;


public class 官职列表显示 : MonoBehaviour
{
    public enum 显示类型
    {
        家族任职,
        国家任职,
    }

    public 显示类型 当前显示类型;
    public Transform 父对象;
    public GameObject 要克隆的对象;
    List<GameObject> 克隆池 = new List<GameObject>();
    public Button 国家任命按钮;
    public Button 家族任命按钮;
    
    // 当前要任命的玩家（从玩家列表显示传递过来）
    public 玩家数据 当前要任命的玩家 { get; set; }
    
    // 玩家列表显示的引用（用于刷新）
    public 玩家列表显示 玩家列表显示;
    
    // 当前选中的职位槽位
    private 职位槽位数据 当前选中职位 = null;
    
    [Header("接口地址")]
    private string 获取家族职位列表地址 = "http://43.139.181.191:5000/api/getClanRoles";
    private string 任命家族职位地址 = "http://43.139.181.191:5000/api/appointClanRole";

    // 存储从服务器获取的职位列表
    private List<职位槽位数据> 服务器职位列表 = new List<职位槽位数据>();

    public void OnEnable()
    {
        if (当前显示类型 == 显示类型.家族任职)
        {
            // 从服务器获取家族职位列表
            StartCoroutine(获取家族职位列表());
        }
        else
        {
            刷新显示();
        }
    }

    public void 刷新显示()
    {
        国家任命按钮.gameObject.SetActive(当前显示类型 == 显示类型.国家任职);
        家族任命按钮.gameObject.SetActive(当前显示类型 == 显示类型.家族任职);
        
        if (当前显示类型 == 显示类型.家族任职)
        {
            显示家族职位列表();
        }
    }

    /// <summary>
    /// 从服务器获取家族职位列表
    /// </summary>
    IEnumerator 获取家族职位列表()
    {
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null || 当前玩家.家族 == null || 当前玩家.家族.家族ID <= 0)
        {
            Debug.LogError("无法获取当前玩家的家族信息");
            yield break;
        }

        int 家族ID = 当前玩家.家族.家族ID;
        string json数据 = $"{{\"clanId\":{家族ID}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(获取家族职位列表地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取家族职位列表出错: " + 请求.error);
                yield break;
            }

            string 返回文本 = 请求.downloadHandler.text;
            Debug.Log("获取家族职位列表响应: " + 返回文本);

            获取家族职位列表响应 响应 = JsonUtility.FromJson<获取家族职位列表响应>(返回文本);
            if (响应 == null || !响应.success || 响应.data == null)
            {
                Debug.LogError("获取家族职位列表失败：" + (响应 != null ? 响应.message : "解析失败"));
                yield break;
            }

            // 转换服务器数据
            服务器职位列表.Clear();
            foreach (var item in 响应.data.roles)
            {
                职位槽位数据 职位 = new 职位槽位数据
                {
                    role = item.role,
                    slotIndex = item.slotIndex,
                    playerId = item.playerId,
                    playerName = item.playerName,
                    isOccupied = item.isOccupied
                };
                服务器职位列表.Add(职位);
            }

            // 刷新显示
            刷新显示();
        }
    }

    /// <summary>
    /// 显示家族职位列表
    /// </summary>
    void 显示家族职位列表()
    {
        if (要克隆的对象 == null || 父对象 == null)
        {
            Debug.LogError("官职列表显示：要克隆的对象或父对象未设置");
            return;
        }

        要克隆的对象.gameObject.SetActive(false);

        // 清理旧的克隆对象
        foreach (var obj in 克隆池)
        {
            if (obj != null) Destroy(obj);
        }
        克隆池.Clear();
        当前选中职位 = null;

        // 克隆显示职位
        int count = 服务器职位列表.Count;
        for (int i = 0; i < count; i++)
        {
            职位槽位数据 职位 = 服务器职位列表[i];
            GameObject 克隆对象 = Instantiate(要克隆的对象, 父对象);

            // 显示职位信息：职位名：玩家名（或无）
            string 显示文本 = $"{职位.role}：{职位.playerName}";
            克隆对象.transform.GetChild(0).GetComponent<Text>().text = 显示文本;

            克隆对象.gameObject.SetActive(true);
            克隆池.Add(克隆对象);

            // 处理点击选择逻辑
            Toggle t = 克隆对象.GetComponent<Toggle>();
            职位槽位数据 捕获职位 = 职位; // 闭包捕获
            t.onValueChanged.AddListener(isOn =>
            {
                if (isOn)
                {
                    当前选中职位 = 捕获职位;
                    Debug.Log($"当前选中职位：{捕获职位.role}，玩家：{捕获职位.playerName}");
                }
            });
        }
    }

    /// <summary>
    /// 点击家族任命按钮
    /// </summary>
    public void 家族任命()
    {
        if (当前要任命的玩家 == null)
        {
            通用提示框.显示("请先选择一个玩家");
            return;
        }

        if (当前选中职位 == null)
        {
            通用提示框.显示("请先选择一个职位");
            return;
        }

        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null || 当前玩家.家族 == null || 当前玩家.家族.家族ID <= 0)
        {
            通用提示框.显示("无法获取当前玩家的家族信息");
            return;
        }

        // 检查：族长不能任命自己为其他职位
        if (当前要任命的玩家.ID == 当前玩家.家族.族长ID)
        {
            通用提示框.显示("族长不能任命自己为其他职位");
            return;
        }

        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId <= 0)
        {
            通用提示框.显示("任命失败：未找到账号ID，请先登录");
            return;
        }

        // 发送任命请求（如果职位已有玩家，会自动顶替）
        StartCoroutine(发送任命家族职位请求(accountId, 当前玩家.家族.家族ID, 当前要任命的玩家.ID, 当前选中职位.role));
    }

    /// <summary>
    /// 发送任命家族职位请求到服务器
    /// </summary>
    IEnumerator 发送任命家族职位请求(int accountId, int clanId, int playerId, string role)
    {
        string json数据 = $"{{\"accountId\":{accountId},\"clanId\":{clanId},\"playerId\":{playerId},\"role\":\"{role}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(任命家族职位地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("任命家族职位出错: " + 请求.error);
                通用提示框.显示("任命失败：网络错误");
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("任命家族职位响应: " + 返回文本);

                任命家族职位响应 响应 = JsonUtility.FromJson<任命家族职位响应>(返回文本);
                if (响应 != null)
                {
                    if (响应.success)
                    {
                        通用提示框.显示(响应.message);
                        
                        // 关闭官职列表显示UI
                        this.gameObject.SetActive(false);
                        
                        // 刷新玩家列表显示（重新获取家族成员列表）
                        if (玩家列表显示 != null)
                        {
                            // 重新获取家族成员列表
                            玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
                            if (当前玩家 != null && 当前玩家.家族 != null && 当前玩家.家族.家族ID > 0)
                            {
                                玩家列表显示.StartCoroutine(玩家列表显示.获取家族成员列表(当前玩家.家族.家族ID));
                            }
                            else
                            {
                                // 如果无法获取家族信息，至少刷新显示
                                玩家列表显示.刷新显示();
                            }
                        }
                    }
                    else
                    {
                        通用提示框.显示(响应.message);
                    }
                }
                else
                {
                    通用提示框.显示("任命失败：解析响应失败");
                }
            }
        }
    }
}

// =================== 服务端返回的数据结构 ===================

[System.Serializable]
public class 获取家族职位列表响应
{
    public bool success;
    public string message;
    public 家族职位列表数据 data;
}

[System.Serializable]
public class 家族职位列表数据
{
    public int clanId;
    public int clanLevel;
    public 职位槽位数据[] roles;
}

[System.Serializable]
public class 职位槽位数据
{
    public string role;  // "副族长" 或 "精英"
    public int slotIndex;  // 职位槽位索引
    public int playerId;  // 玩家ID，0表示未任命
    public string playerName;  // 玩家姓名，"无"表示未任命
    public bool isOccupied;  // 是否已任命
}

[System.Serializable]
public class 任命家族职位响应
{
    public bool success;
    public string message;
}
