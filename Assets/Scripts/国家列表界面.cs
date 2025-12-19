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
/// 
public enum 显示类型
{
    加入国家,
    更换国家,
    国家排名
}
public class 国家列表界面 : MonoBehaviour
{
    [Header("UI 引用")]
    public Transform 父对象;          // ScrollView 的 Content
    public GameObject 要克隆的对象;    // 每一行国家的模板（带 Toggle + 文本）

    [Header("接口地址")]
    private string 国家列表地址 = "http://43.139.181.191:5000/api/countries";
    private string 加入国家地址 = "http://43.139.181.191:5000/api/joinCountry";
    private string 更换国家地址 = "http://43.139.181.191:5000/api/changeCountry";

    List<GameObject> 克隆池 = new List<GameObject>();
    public 国家信息库 当前选中国家 = null;

    public Button 加入国家按钮;
    public Button 更换国家按钮;
    public 显示类型 列表显示类型 = 显示类型.加入国家;

    public 国家信息显示 国家信息显示;

    void OnEnable()
    {
        // 如果是更换国家或国家排名模式，需要显示完整的国家列表，应该从服务器获取
        // 如果是加入国家模式，且本地已有国家列表（创建角色后选择国家），可以使用本地缓存
        if (列表显示类型 == 显示类型.更换国家 || 列表显示类型 == 显示类型.国家排名)
        {
            // 更换国家和国家排名需要完整的国家列表，强制从服务器获取
            StartCoroutine(获取国家列表然后刷新());
        }
        else if (全局变量.所有国家列表.Count == 0)
        {
            // 加入国家模式，但本地还没有国家列表，从服务器拉取
            StartCoroutine(获取国家列表然后刷新());
        }
        else
        {
            // 加入国家模式，且本地已有国家列表，直接刷新显示
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
        
        // 清理旧的克隆对象
        foreach (var obj in 克隆池)
        {
            if (obj != null) Destroy(obj);
        }
        克隆池.Clear();
        当前选中国家 = null;

        // 根据显示类型决定是否排序
        List<国家信息库> 要显示的国家列表 = new List<国家信息库>(全局变量.所有国家列表);
        
        // 如果是国家排名模式，按照国家资金（黄金）降序排序
        if (列表显示类型 == 显示类型.国家排名)
        {
            要显示的国家列表.Sort((a, b) => b.黄金.CompareTo(a.黄金));
            Debug.Log("国家排名模式：已按照国家资金（黄金）降序排序");
        }

        int count = 要显示的国家列表.Count;

        for (int i = 0; i < count; i++)
        {
            GameObject 克隆对象 = Instantiate(要克隆的对象, 父对象);
            
            // 如果是国家排名模式，显示排名序号
            if (列表显示类型 == 显示类型.国家排名)
            {
                克隆对象.transform.GetChild(0).GetComponent<Text>().text = $"{i + 1}.";
                克隆对象.transform.GetChild(0).GetComponent<Text>().fontSize = 30;
                克隆对象.transform.GetChild(0).GetComponent<Text>().color = new Color(0.94f, 0.97f, 0.21f);
                克隆对象.transform.GetChild(1).GetComponent<Text>().text = $"{要显示的国家列表[i].国号} {要显示的国家列表[i].国名}";
            }
            else
            {
                // 其他模式保持原样
                克隆对象.transform.GetChild(0).GetComponent<Text>().fontSize = 22;
                克隆对象.transform.GetChild(0).GetComponent<Text>().color = new Color(1f, 1f, 1f);
                克隆对象.transform.GetChild(0).GetComponent<Text>().text = 要显示的国家列表[i].国号;
                克隆对象.transform.GetChild(1).GetComponent<Text>().text = 要显示的国家列表[i].国名;
            }
            
            克隆对象.gameObject.SetActive(true);
            克隆池.Add(克隆对象);

            // 处理 Toggle 选择逻辑
            Toggle t = 克隆对象.GetComponent<Toggle>();
            国家信息库 捕获国家 = 要显示的国家列表[i]; // 闭包捕获
            t.onValueChanged.AddListener(isOn =>
            {
                if (isOn)
                {
                    当前选中国家 = 捕获国家;
                    //Debug.Log($"选中国家 {捕获国家.国名}({捕获国家.国号})");
                }
            });
        }
        
        switch (列表显示类型)
        {
            case 显示类型.加入国家:
                加入国家按钮.gameObject.SetActive(true);
                更换国家按钮.gameObject.SetActive(false);
                break;
            case 显示类型.更换国家:
                加入国家按钮.gameObject.SetActive(false);
                更换国家按钮.gameObject.SetActive(true);
                break;
            case 显示类型.国家排名:
                加入国家按钮.gameObject.SetActive(false);
                更换国家按钮.gameObject.SetActive(false);
                break;
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
                    if (玩家数据管理.实例 != null)
                    {
                        玩家数据管理.实例.获取玩家数据(accountId);
                    }

                    // 成功加入后，关闭当前国家列表界面的 UI
                    gameObject.SetActive(false);
                }
                else
                {
                    Debug.Log(响应 != null ? ("加入国家失败：" + 响应.message) : "加入国家失败：解析错误");
                }
            }
        }
    }

    public void 更换国家()
    {
        if (当前选中国家 == null)
        {
            Debug.Log("请先选择一个国家");
            return;
        }

        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId <= 0)
        {
            Debug.LogError("更换国家失败：未找到 AccountId，可能尚未登录");
            return;
        }

        StartCoroutine(发送更换国家请求(accountId, 当前选中国家.国家ID, 当前选中国家.国名));
    }

    IEnumerator 发送更换国家请求(int accountId, int 国家ID, string 国名)
    {
        string json数据 = $"{{\"accountId\":{accountId},\"countryId\":{国家ID}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(更换国家地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("更换国家出错: " + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("更换国家响应: " + 返回文本);

                更换国家响应 响应 = JsonUtility.FromJson<更换国家响应>(返回文本);
                if (响应 != null && 响应.success)
                {
                    Debug.Log($"成功更换到 {国名}");

                    // 成功更换后，刷新玩家数据，让国家显示更新
                    // 注意：获取玩家数据是异步的，数据更新完成后会自动刷新国家信息显示
                    if (玩家数据管理.实例 != null)
                    {
                        玩家数据管理.实例.获取玩家数据(accountId);
                    }

                    // 成功更换后，关闭当前国家列表界面的 UI
                    gameObject.SetActive(false);
                    
                    // 将延迟刷新任务交给玩家数据管理（单例，不会被销毁）来执行
                    // 因为当前GameObject被禁用后，挂载的协程会被停止
                    if (玩家数据管理.实例 != null && 国家信息显示 != null)
                    {
                        玩家数据管理.实例.延迟刷新国家信息(国家信息显示, 3f);
                    }
                    else
                    {
                        Debug.LogWarning($"无法延迟刷新：玩家数据管理.实例={玩家数据管理.实例 != null}, 国家信息显示={国家信息显示 != null}");
                    }
                }
                else
                {
                    Debug.Log(响应 != null ? ("更换国家失败：" + 响应.message) : "更换国家失败：解析错误");
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

[System.Serializable]
public class 更换国家响应
{
    public bool success;
    public string message;
}


