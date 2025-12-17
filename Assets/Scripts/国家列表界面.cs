using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Text;

/// <summary>
/// 国家列表界面：负责从服务器获取9个国家，并让玩家选择加入。
/// 不使用 Find 系列方法，所有引用都通过 Inspector 拖拽。
/// </summary>
public class 国家列表界面 : MonoBehaviour
{
    [Header("UI 引用")]
    public GameObject 国家列表面板;          // 整个面板（用于显示/隐藏）
    public Transform 国家按钮容器;          // 放国家按钮的父节点
    public Button 国家按钮预制体;           // 一个通用按钮预制体，文本可改
    public Text 提示文本;

    [Header("脚本引用")]
    public 玩家数据管理 玩家管理;           // 在 Inspector 中拖拽

    [Header("接口地址")]
    public string 国家列表地址 = "http://43.139.181.191:5000/api/countries";
    public string 加入国家地址 = "http://43.139.181.191:5000/api/joinCountry";

    /// <summary>
    /// 打开国家列表（例如在按钮点击事件中调用）
    /// </summary>
    public void 打开国家列表()
    {
        if (国家列表面板 != null)
            国家列表面板.SetActive(true);

        if (提示文本 != null)
            提示文本.text = "正在获取国家列表...";

        StartCoroutine(发送获取国家列表请求());
    }

    /// <summary>
    /// 关闭国家列表面板
    /// </summary>
    public void 关闭国家列表()
    {
        if (国家列表面板 != null)
            国家列表面板.SetActive(false);
    }

    IEnumerator 发送获取国家列表请求()
    {
        using (UnityWebRequest 请求 = UnityWebRequest.Get(国家列表地址))
        {
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取国家列表出错: " + 请求.error);
                if (提示文本 != null)
                    提示文本.text = "获取国家列表失败：" + 请求.error;
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("获取国家列表响应: " + 返回文本);

                国家列表响应 响应 = JsonUtility.FromJson<国家列表响应>(返回文本);
                if (响应 != null && 响应.success && 响应.data != null)
                {
                    刷新国家按钮(响应.data);
                    if (提示文本 != null)
                        提示文本.text = "请选择要加入的国家";
                }
                else
                {
                    if (提示文本 != null)
                        提示文本.text = 响应 != null ? ("获取国家列表失败：" + 响应.message) : "解析国家列表失败";
                }
            }
        }
    }

    void 刷新国家按钮(国家简要[] 国家数组)
    {
        if (国家按钮容器 == null || 国家按钮预制体 == null)
        {
            Debug.LogError("国家按钮容器或预制体未设置");
            return;
        }

        // 清空旧按钮
        for (int i = 国家按钮容器.childCount - 1; i >= 0; i--)
        {
            Destroy(国家按钮容器.GetChild(i).gameObject);
        }

        // 创建新按钮
        foreach (var 国家 in 国家数组)
        {
            Button 按钮 = Instantiate(国家按钮预制体, 国家按钮容器);
            Text 文本 = 按钮.GetComponentInChildren<Text>();
            if (文本 != null)
            {
                文本.text = $"{国家.name}（{国家.code}）";
            }

            int 国家ID = 国家.id;
            按钮.onClick.AddListener(() => 点击加入国家(国家ID, 国家.name));
        }
    }

    void 点击加入国家(int 国家ID, string 国名)
    {
        if (提示文本 != null)
            提示文本.text = $"正在申请加入 {国名}...";

        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId <= 0)
        {
            Debug.LogError("找不到 AccountId，可能尚未登录");
            if (提示文本 != null)
                提示文本.text = "加入失败：未登录";
            return;
        }

        StartCoroutine(发送加入国家请求(accountId, 国家ID, 国名));
    }

    IEnumerator 发送加入国家请求(int accountId, int 国家ID, string 国名)
    {
        string json数据 = $"{{\"accountId\":{accountId},\"countryId\":{国家ID}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(加入国家地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("加入国家出错: " + 请求.error);
                if (提示文本 != null)
                    提示文本.text = "加入国家失败：" + 请求.error;
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("加入国家响应: " + 返回文本);

                加入国家响应 响应 = JsonUtility.FromJson<加入国家响应>(返回文本);
                if (响应 != null && 响应.success)
                {
                    if (提示文本 != null)
                        提示文本.text = $"成功加入 {国名}";

                    // 成功加入后，刷新玩家数据，让国家显示更新
                    if (玩家管理 != null)
                    {
                        玩家管理.获取玩家数据(accountId);
                    }

                    // 可以选择自动关闭国家列表面板
                    // if (国家列表面板 != null)
                    //     国家列表面板.SetActive(false);
                }
                else
                {
                    if (提示文本 != null)
                        提示文本.text = 响应 != null ? ("加入国家失败：" + 响应.message) : "加入国家失败：解析错误";
                }
            }
        }
    }
}

// =================== 服务端国家列表返回的数据结构（给 JsonUtility 用） ===================

[System.Serializable]
public class 国家列表响应
{
    public bool success;
    public string message;
    public 国家简要[] data;
}

[System.Serializable]
public class 国家简要
{
    public int id;
    public string name;
    public string code;
    public string declaration;
    public string announcement;
    public int copperMoney;
    public int food;
    public int gold;
}


