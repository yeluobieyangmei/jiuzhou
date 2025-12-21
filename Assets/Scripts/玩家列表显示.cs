using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Text;
using 国家系统;
using 玩家数据结构;

public class 玩家列表显示 : MonoBehaviour
{
    public Transform 父对象;
    public GameObject 要克隆的对象;
    List<GameObject> 克隆池 = new List<GameObject>();
    public 玩家数据 当前选中玩家 = null;
    public 国家信息库 当前国家 { get; set; }
    public 显示类型 当前显示类型;
    public 官员类型 当前官员类型;
    public Button 任命按钮;
    public Button 家族任命按钮;
    public 国家信息显示 国家信息显示;
    public Text UI标题;

    [Header("接口地址")]
    private string 获取国家成员地址 = "http://43.139.181.191:5000/api/getCountryMembers";
    private string 获取所有玩家地址 = "http://43.139.181.191:5000/api/getAllPlayers";
    private string 获取家族成员地址 = "http://43.139.181.191:5000/api/getClanMembers";

    // 存储从服务器获取的玩家列表
    private List<玩家数据> 服务器玩家列表 = new List<玩家数据>();

    public enum 显示类型
    {
        国家任命官员,
        国家不任命官员,
        世界玩家排名查看,
        家族玩家查看,
    }

    public enum 官员类型
    {
        大都督,
        丞相,
        太尉,
        御史大夫,
        金吾卫
    }

    public enum 家族官职类型
    {
        副族长,
        精英
    }

    public void OnEnable()
    {
        // 根据显示类型从服务器获取数据
        if (当前显示类型 == 显示类型.世界玩家排名查看)
        {
            StartCoroutine(获取所有玩家列表());
        }
        else if (当前显示类型 == 显示类型.家族玩家查看)
        {
            // 家族玩家查看：获取当前家族的成员列表
            玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
            if (当前玩家 != null && 当前玩家.家族 != null && 当前玩家.家族.家族ID > 0)
            {
                StartCoroutine(获取家族成员列表(当前玩家.家族.家族ID));
            }
            else
            {
                Debug.LogWarning("当前玩家没有家族或家族ID无效，无法获取家族成员列表");
            }
        }
        else
        {
            // 国家任命官员 或 国家不任命官员：获取当前国家的成员列表
            if (当前国家 != null && 当前国家.国家ID > 0)
            {
                StartCoroutine(获取国家成员列表(当前国家.国家ID));
            }
            else
            {
                Debug.LogWarning("当前国家为空或国家ID无效，无法获取成员列表");
            }
        }
    }

    /// <summary>
    /// 从服务器获取指定国家的成员列表
    /// </summary>
    IEnumerator 获取国家成员列表(int 国家ID)
    {
        string json数据 = $"{{\"countryId\":{国家ID}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(获取国家成员地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取国家成员列表出错: " + 请求.error);
                yield break;
            }

            string 返回文本 = 请求.downloadHandler.text;
            Debug.Log("获取国家成员列表响应: " + 返回文本);

            获取国家成员响应 响应 = JsonUtility.FromJson<获取国家成员响应>(返回文本);
            if (响应 == null || !响应.success || 响应.data == null)
            {
                Debug.LogError("获取国家成员列表失败：" + (响应 != null ? 响应.message : "解析失败"));
                yield break;
            }

            // 将服务器数据转换为本地玩家数据
            服务器玩家列表.Clear();
            foreach (var item in 响应.data)
            {
                玩家数据 玩家 = 转换服务器数据为玩家数据(item);
                服务器玩家列表.Add(玩家);
            }

            // 按属性之和降序排序（服务端已排序，但客户端再次确认）
            服务器玩家列表.Sort((a, b) => 
            {
                int 属性之和A = a.玩家属性.生命值 + a.玩家属性.攻击力 + a.玩家属性.防御力;
                int 属性之和B = b.玩家属性.生命值 + b.玩家属性.攻击力 + b.玩家属性.防御力;
                return 属性之和B.CompareTo(属性之和A); // 降序
            });

            // 刷新显示
            刷新显示();
        }
    }

    /// <summary>
    /// 从服务器获取指定家族的成员列表
    /// </summary>
    IEnumerator 获取家族成员列表(int 家族ID)
    {
        string json数据 = $"{{\"clanId\":{家族ID}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(获取家族成员地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取家族成员列表出错: " + 请求.error);
                yield break;
            }

            string 返回文本 = 请求.downloadHandler.text;
            Debug.Log("获取家族成员列表响应: " + 返回文本);

            获取家族成员响应 响应 = JsonUtility.FromJson<获取家族成员响应>(返回文本);
            if (响应 == null || !响应.success || 响应.data == null)
            {
                Debug.LogError("获取家族成员列表失败：" + (响应 != null ? 响应.message : "解析失败"));
                yield break;
            }

            // 将服务器数据转换为本地玩家数据
            服务器玩家列表.Clear();
            foreach (var item in 响应.data)
            {
                玩家数据 玩家 = 转换服务器数据为玩家数据(item);
                服务器玩家列表.Add(玩家);
            }

            // 按家族贡献值降序排序（服务端已排序，但客户端再次确认）
            服务器玩家列表.Sort((a, b) => 
            {
                if (a.家族贡献值 != b.家族贡献值)
                {
                    return b.家族贡献值.CompareTo(a.家族贡献值); // 贡献值降序
                }
                return a.ID.CompareTo(b.ID); // 相同贡献值按ID升序
            });

            // 刷新显示
            刷新显示();
        }
    }

    /// <summary>
    /// 从服务器获取所有玩家列表（用于世界排名）
    /// </summary>
    IEnumerator 获取所有玩家列表()
    {
        // 使用 GET 方法，因为服务端不需要请求体
        using (UnityWebRequest 请求 = UnityWebRequest.Get(获取所有玩家地址))
        {
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取所有玩家列表出错: " + 请求.error);
                yield break;
            }

            string 返回文本 = 请求.downloadHandler.text;
            Debug.Log("获取所有玩家列表响应: " + 返回文本);

            获取所有玩家响应 响应 = JsonUtility.FromJson<获取所有玩家响应>(返回文本);
            if (响应 == null || !响应.success || 响应.data == null)
            {
                Debug.LogError("获取所有玩家列表失败：" + (响应 != null ? 响应.message : "解析失败"));
                yield break;
            }

            // 将服务器数据转换为本地玩家数据
            服务器玩家列表.Clear();
            foreach (var item in 响应.data)
            {
                玩家数据 玩家 = 转换服务器数据为玩家数据(item);
                服务器玩家列表.Add(玩家);
            }

            // 按属性之和降序排序（服务端已排序，但客户端再次确认）
            服务器玩家列表.Sort((a, b) => 
            {
                int 属性之和A = a.玩家属性.生命值 + a.玩家属性.攻击力 + a.玩家属性.防御力;
                int 属性之和B = b.玩家属性.生命值 + b.玩家属性.攻击力 + b.玩家属性.防御力;
                return 属性之和B.CompareTo(属性之和A); // 降序
            });

            // 刷新显示
            刷新显示();
        }
    }

    /// <summary>
    /// 将服务器返回的玩家数据转换为本地玩家数据对象
    /// </summary>
    玩家数据 转换服务器数据为玩家数据(玩家简要数据 服务器数据)
    {
        玩家数据 玩家 = new 玩家数据();
        玩家.ID = 服务器数据.id;
        玩家.姓名 = 服务器数据.name;
        玩家.性别 = 服务器数据.gender;
        玩家.等级 = 服务器数据.level;
        玩家.称号名 = 服务器数据.titleName;
        玩家.铜钱 = 服务器数据.copperMoney;
        玩家.黄金 = 服务器数据.gold;
        玩家.家族贡献值 = 服务器数据.clanContribution;

        // 转换官职
        if (!string.IsNullOrEmpty(服务器数据.office))
        {
            switch (服务器数据.office)
            {
                case "国王": 玩家.官职 = 官职枚举.国王; break;
                case "大都督": 玩家.官职 = 官职枚举.大都督; break;
                case "丞相": 玩家.官职 = 官职枚举.丞相; break;
                case "太尉": 玩家.官职 = 官职枚举.太尉; break;
                case "御史大夫": 玩家.官职 = 官职枚举.御史大夫; break;
                case "金吾卫": 玩家.官职 = 官职枚举.金吾卫; break;
                case "镖师": 玩家.官职 = 官职枚举.镖师; break;
                default: 玩家.官职 = 官职枚举.国民; break;
            }
        }
        else
        {
            玩家.官职 = 官职枚举.国民;
        }

        // 转换属性
        if (服务器数据.attributes != null)
        {
            玩家.玩家属性.生命值 = 服务器数据.attributes.maxHp;
            玩家.玩家属性.当前生命值 = 服务器数据.attributes.currentHp;
            玩家.玩家属性.攻击力 = 服务器数据.attributes.attack;
            玩家.玩家属性.防御力 = 服务器数据.attributes.defense;
            玩家.玩家属性.暴击率 = 服务器数据.attributes.critRate;
        }

        // 如果有国家信息，关联国家
        if (!string.IsNullOrEmpty(服务器数据.countryName) && 当前国家 != null && 
            当前国家.国家ID == 服务器数据.countryId)
        {
            玩家.国家 = 当前国家;
        }
        else if (服务器数据.countryId > 0)
        {
            // 尝试从全局变量中查找国家
            国家信息库 国家 = 全局方法类.获取指定ID的国家(服务器数据.countryId);
            if (国家 != null)
            {
                玩家.国家 = 国家;
            }
        }

        // 如果当前显示类型是家族玩家查看，关联当前玩家的家族
        if (当前显示类型 == 显示类型.家族玩家查看)
        {
            玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
            if (当前玩家 != null && 当前玩家.家族 != null)
            {
                玩家.家族 = 当前玩家.家族;
            }
        }

        return 玩家;
    }

    /// <summary>
    /// 刷新显示玩家列表
    /// </summary>
    public void 刷新显示()
    {
        if (要克隆的对象 == null || 父对象 == null)
        {
            Debug.LogError("玩家列表显示：要克隆的对象或父对象未设置");
            return;
        }
        
        // 设置家族任命按钮的显隐状态
        if (当前显示类型 == 显示类型.家族玩家查看)
        {
            玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
            if (当前玩家 != null && 当前玩家.家族 != null && 家族任命按钮 != null)
            {
                bool 是族长 = 当前玩家.家族.族长ID == 当前玩家.ID;
                家族任命按钮.gameObject.SetActive(是族长);
                Debug.Log($"家族任命按钮状态：显示类型={当前显示类型}，族长ID={当前玩家.家族.族长ID}，当前玩家ID={当前玩家.ID}，是族长={是族长}");
            }
            else
            {
                if (家族任命按钮 != null) 家族任命按钮.gameObject.SetActive(false);
                Debug.LogWarning($"无法设置家族任命按钮：当前玩家={当前玩家 != null}，家族={当前玩家?.家族 != null}，按钮={家族任命按钮 != null}");
            }
        }
        else
        {
            // 非家族玩家查看模式，隐藏家族任命按钮
            if (家族任命按钮 != null) 家族任命按钮.gameObject.SetActive(false);
        }
        
        // 设置国家任命按钮的显隐状态
        任命按钮.gameObject.SetActive(当前显示类型 == 显示类型.国家任命官员);
        要克隆的对象.gameObject.SetActive(false);

        // 清理旧的克隆对象
        foreach (var obj in 克隆池)
        {
            if (obj != null) Destroy(obj);
        }
        克隆池.Clear();
        当前选中玩家 = null;

        // 根据显示类型过滤玩家列表
        List<玩家数据> 要显示的玩家列表 = new List<玩家数据>(服务器玩家列表);

        if (当前显示类型 == 显示类型.国家任命官员)
        {
            // 任命官员模式：排除国王
            if (当前国家 != null && 当前国家.国王ID > 0)
            {
                要显示的玩家列表.RemoveAll(p => p.ID == 当前国家.国王ID);
            }
        }

        int count = 要显示的玩家列表.Count;

        for (int i = 0; i < count; i++)
        {
            玩家数据 玩家 = 要显示的玩家列表[i];
            GameObject 克隆对象 = Instantiate(要克隆的对象, 父对象);

            // 根据显示类型显示不同内容
            if (当前显示类型 == 显示类型.世界玩家排名查看)
            {
                // 世界排名：显示排名、姓名、等级、属性之和、国家
                string 国家信息 = !string.IsNullOrEmpty(玩家.国家?.国号) ? $"({玩家.国家.国号})" : "(无国家)";
                克隆对象.transform.GetChild(0).GetComponent<Text>().text = $"第{i + 1}名";
                克隆对象.transform.GetChild(0).GetComponent<Text>().fontSize = 30;
                克隆对象.transform.GetChild(0).GetComponent<Text>().color = new Color(0.94f, 0.97f, 0.21f);
                克隆对象.transform.GetChild(1).GetComponent<Text>().text = $"LV.{玩家.等级} {玩家.姓名} {国家信息}";
            }
            else if (当前显示类型 == 显示类型.家族玩家查看)
            {
                // 家族成员：显示排名、等级、姓名和帮贡
                克隆对象.transform.GetChild(0).GetComponent<Text>().text = $"{i + 1}.";
                克隆对象.transform.GetChild(0).GetComponent<Text>().fontSize = 30;
                克隆对象.transform.GetChild(0).GetComponent<Text>().color = new Color(0.94f, 0.97f, 0.21f);
                克隆对象.transform.GetChild(1).GetComponent<Text>().text = $"{玩家.姓名} 帮贡:{玩家.家族贡献值}";
            }
            else
            {
                // 国家成员：显示等级和姓名
                克隆对象.transform.GetChild(0).GetComponent<Text>().text = $"{i + 1}.";
                克隆对象.transform.GetChild(0).GetComponent<Text>().fontSize = 30;
                克隆对象.transform.GetChild(0).GetComponent<Text>().color = new Color(0.94f, 0.97f, 0.21f);
                克隆对象.transform.GetChild(1).GetComponent<Text>().text = $"LV.{玩家.等级} {玩家.姓名}";
            }

            克隆对象.gameObject.SetActive(true);
            克隆池.Add(克隆对象);

            // 处理 Toggle 选择逻辑
            Toggle t = 克隆对象.GetComponent<Toggle>();
            玩家数据 捕获玩家 = 玩家; // 闭包捕获
            t.onValueChanged.AddListener(isOn =>
            {
                if (isOn)
                {
                    当前选中玩家 = 捕获玩家;
                    Debug.Log($"当前选中玩家：{当前选中玩家.姓名}");
                }
            });
        }
    }

    public void 任命官员()
    {
        if (当前选中玩家 == null)
        {
            Debug.Log("请先选择一个玩家");
            return;
        }

        if (当前国家 == null)
        {
            Debug.LogError("当前国家为空，无法任命官员");
            return;
        }

        // TODO: 这里需要调用服务端API来任命官员
        // 目前先使用本地逻辑（后续需要改为服务端API）
        switch (当前官员类型)
        {
            case 官员类型.大都督:
                if (当前国家.大都督ID != -1)
                {
                    // 先撤销原大都督
                    // TODO: 调用服务端API
                }
                当前国家.大都督ID = 当前选中玩家.ID;
                当前选中玩家.官职 = 官职枚举.大都督;
                break;
            // 其他官职类型类似...
        }

        this.gameObject.SetActive(false);
        if (国家信息显示 != null)
        {
            国家信息显示.刷新显示();
        }
        Debug.Log($"已任命{当前选中玩家.姓名}为本国{当前官员类型}!");
    }
}

// =================== 服务端返回的数据结构 ===================

[System.Serializable]
public class 获取国家成员响应
{
    public bool success;
    public string message;
    public 玩家简要数据[] data;
}

[System.Serializable]
public class 获取所有玩家响应
{
    public bool success;
    public string message;
    public 玩家简要数据[] data;
}

[System.Serializable]
public class 获取家族成员响应
{
    public bool success;
    public string message;
    public 玩家简要数据[] data;
}

[System.Serializable]
public class 玩家简要数据
{
    public int id;
    public string name;
    public string gender;
    public int level;
    public string titleName;
    public string office;
    public int copperMoney;
    public int gold;
    public int countryId;
    public int clanContribution;  // 家族贡献值
    public 玩家属性简要 attributes;
    public string countryName;
    public string countryCode;
}

[System.Serializable]
public class 玩家属性简要
{
    public int maxHp;
    public int currentHp;
    public int attack;
    public int defense;
    public float critRate;
}
