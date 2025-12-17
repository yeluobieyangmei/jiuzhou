using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System.Collections.Generic;
using 国家系统;

/// <summary>
/// 国家列表界面：
/// - 从服务端获取国家列表，填充到 全局变量.所有国家列表
/// - 用 Toggle 列出所有国家，记录当前选中
/// - 点击“加入国家”按钮时，请求服务端把玩家加入选中的国家
/// </summary>
public class 国家列表界面 : MonoBehaviour
{
    [Header("UI 引用")]
    public Transform 父对象;          // ScrollView 的 Content
    public GameObject 要克隆的对象;    // 每一行国家的模板（带 Toggle + 文本）

    [Header("脚本引用")]
    public 玩家数据管理 玩家管理;      // 在 Inspector 中拖拽

    [Header("接口地址")]
    public string 国家列表地址 = "http://43.139.181.191:5000/api/countries";
    public string 加入国家地址 = "http://43.139.181.191:5000/api/joinCountry";

    List<GameObject> 克隆池 = new List<GameObject>();
    public 国家信息库 当前选中国家 = null;

    void OnEnable()
    {
        // 第一次打开如果本地还没有国家列表，就从服务器拉取
        if (全局变量.所有国家列表.Count == 0)
        {
            StartCoroutine(获取国家列表然后刷新());
        }
        else
        {
            刷新显示();
        }
    }

    IEnumerator 获取国家列表然后刷新()
    {
        using (UnityWebRequest 请求 = UnityWebRequest.Get(国家列表地址))
        {
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取国家列表出错: " + 请求.error);
                yield break;
            }

            string 返回文本 = 请求.downloadHandler.text;
            Debug.Log("获取国家列表响应: " + 返回文本);

            国家列表响应 响应 = JsonUtility.FromJson<国家列表响应>(返回文本);
            if (响应 == null || !响应.success || 响应.data == null)
            {
                Debug.LogError("获取国家列表失败：" + 响应.message + "解析国家列表失败");
                yield break;
            }

            // 把服务端的国家列表转换成本地的 国家信息库，并放入 全局变量.所有国家列表
            全局变量.所有国家列表.Clear();
            foreach (var item in 响应.data)
            {
                国家信息库 国家 = new 国家信息库();
                国家.国家ID = item.id;
                国家.国名 = item.name;
                国家.国号 = item.code;
                国家.国家宣言 = item.declaration;
                国家.国家公告 = item.announcement;
                国家.铜钱 = item.copperMoney;
                国家.粮食 = item.food;
                国家.黄金 = item.gold;

                全局变量.所有国家列表.Add(国家);
            }

            刷新显示();
            Debug.Log("请选择要加入的国家");
        }
    }

    public void 刷新显示()
    {
        if (要克隆的对象 == null || 父对象 == null)
        {
            Debug.LogError("国家列表界面：要克隆的对象或父对象未设置");
            return;
        }

        要克隆的对象.gameObject.SetActive(false);
        int count = 全局变量.所有国家列表.Count;

        // 清理旧的克隆对象
        foreach (var obj in 克隆池)
        {
            if (obj != null) Destroy(obj);
        }
        克隆池.Clear();
        当前选中国家 = null;

        for (int i = 0; i < count; i++)
        {
            GameObject 克隆对象 = Instantiate(要克隆的对象, 父对象);
            克隆对象.transform.GetChild(0).GetComponent<Text>().text = 全局变量.所有国家列表[i].国号;
            克隆对象.transform.GetChild(1).GetComponent<Text>().text = 全局变量.所有国家列表[i].国名;
            克隆对象.gameObject.SetActive(true);
            克隆池.Add(克隆对象);

            // 处理 Toggle 选择逻辑
            Toggle t = 克隆对象.GetComponent<Toggle>();
            国家信息库 捕获国家 = 全局变量.所有国家列表[i]; // 闭包捕获
            t.onValueChanged.AddListener(isOn =>
            {
                if (isOn)
                {
                    当前选中国家 = 捕获国家;
                    //Debug.Log($"选中国家 {捕获国家.国名}({捕获国家.国号})");
                }
            });
        }
    }

    public void 加入国家()
    {
        if (当前选中国家 == null)
        {
            Debug.Log("请先选择一个国家");
            return;
        }

        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId <= 0)
        {
            Debug.LogError("加入国家失败：未找到 AccountId，可能尚未登录");
            return;
        }

        StartCoroutine(发送加入国家请求(accountId, 当前选中国家.国家ID, 当前选中国家.国名));
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
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("加入国家响应: " + 返回文本);

                加入国家响应 响应 = JsonUtility.FromJson<加入国家响应>(返回文本);
                if (响应 != null && 响应.success)
                {
                    Debug.Log($"成功加入 {国名}");

                    // 成功加入后，刷新玩家数据，让国家显示更新
                    if (玩家管理 != null)
                    {
                        玩家管理.获取玩家数据(accountId);
                    }
                }
                else
                {
                    Debug.Log(响应 != null ? ("加入国家失败：" + 响应.message) : "加入国家失败：解析错误");
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

[System.Serializable]
public class 加入国家响应
{
    public bool success;
    public string message;
}


