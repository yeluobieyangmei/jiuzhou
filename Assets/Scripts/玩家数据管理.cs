using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using 玩家数据结构;
using 国家系统;

public class 玩家数据管理 : MonoBehaviour
{
    [Header("接口地址")]
    private string 获取玩家地址 = "http://43.139.181.191:5000/api/getPlayer";
    private string 创建玩家地址 = "http://43.139.181.191:5000/api/createPlayer";

    // 当前玩家数据（从服务器加载后存储在这里）
    public 玩家数据 当前玩家数据 { get; private set; }

    /// <summary>
    /// 获取玩家数据（登录成功后调用）
    /// </summary>
    public void 获取玩家数据(int accountId)
    {
        StartCoroutine(发送获取玩家请求(accountId));
    }

    /// <summary>
    /// 创建角色（当账号没有角色时调用）
    /// </summary>
    public void 创建角色(int accountId, string 姓名, string 性别)
    {
        StartCoroutine(发送创建玩家请求(accountId, 姓名, 性别));
    }

    IEnumerator 发送获取玩家请求(int accountId)
    {
        string json数据 = $"{{\"accountId\":{accountId}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(获取玩家地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取玩家数据出错: " + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("获取玩家数据响应: " + 返回文本);

                获取玩家响应 响应 = JsonUtility.FromJson<获取玩家响应>(返回文本);
                if (响应 != null)
                {
                    if (响应.success && 响应.data != null)
                    {
                        // 有角色，转换数据
                        转换并保存玩家数据(响应.data);
                        Debug.Log("玩家数据加载成功！");
                        
                        // 显示角色信息界面
                        显示角色信息界面 信息界面 = FindObjectOfType<显示角色信息界面>();
                        if (信息界面 != null)
                        {
                            信息界面.显示角色信息(当前玩家数据);
                        }
                    }
                    else
                    {
                        // 没有角色，需要创建
                        Debug.Log("该账号尚未创建角色，需要创建角色");
                        
                        // 显示创建角色界面
                        创建角色界面 创建界面 = FindObjectOfType<创建角色界面>();
                        if (创建界面 != null)
                        {
                            创建界面.显示创建角色面板(accountId);
                        }
                        else
                        {
                            Debug.LogWarning("找不到创建角色界面组件！");
                        }
                    }
                }
            }
        }
    }

    IEnumerator 发送创建玩家请求(int accountId, string 姓名, string 性别)
    {
        string json数据 = $"{{\"accountId\":{accountId},\"name\":\"{姓名}\",\"gender\":\"{性别}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(创建玩家地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("创建角色出错: " + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("创建角色响应: " + 返回文本);

                创建玩家响应 响应 = JsonUtility.FromJson<创建玩家响应>(返回文本);
                if (响应 != null)
                {
                    if (响应.success)
                    {
                        Debug.Log("角色创建成功！");
                        
                        // 隐藏创建角色界面
                        创建角色界面 创建界面 = FindObjectOfType<创建角色界面>();
                        if (创建界面 != null)
                        {
                            创建界面.隐藏创建角色面板();
                        }
                        
                        // 创建成功后，重新获取玩家数据（会自动显示角色信息界面）
                        获取玩家数据(accountId);
                    }
                    else
                    {
                        Debug.LogError("创建角色失败: " + 响应.message);
                        
                        // 创建失败时，在创建角色界面的提示文本中显示错误信息
                        创建角色界面 创建界面 = FindObjectOfType<创建角色界面>();
                        if (创建界面 != null && 创建界面.提示文本 != null)
                        {
                            创建界面.提示文本.text = "创建失败：" + 响应.message;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 把服务端返回的数据转换成 Unity 的 玩家数据 类
    /// </summary>
    void 转换并保存玩家数据(服务端玩家数据 服务端数据)
    {
        // 创建或更新玩家数据对象
        if (当前玩家数据 == null)
        {
            当前玩家数据 = new 玩家数据();
        }

        // 基础信息
        当前玩家数据.ID = 服务端数据.id;
        当前玩家数据.姓名 = 服务端数据.name;
        当前玩家数据.性别 = 服务端数据.gender;
        当前玩家数据.等级 = 服务端数据.level;
        当前玩家数据.称号名 = 服务端数据.titleName;
        当前玩家数据.铜钱 = 服务端数据.copperMoney;
        当前玩家数据.黄金 = 服务端数据.gold;

        // 官职（字符串转枚举）
        if (System.Enum.TryParse<玩家数据结构.官职枚举>(服务端数据.office, out var 官职值))
        {
            当前玩家数据.官职 = 官职值;
        }
        else
        {
            当前玩家数据.官职 = 玩家数据结构.官职枚举.国民;
        }

        // 玩家属性
        if (服务端数据.attributes != null)
        {
            当前玩家数据.玩家属性.生命值 = 服务端数据.attributes.maxHp;
            当前玩家数据.玩家属性.当前生命值 = 服务端数据.attributes.currentHp;
            当前玩家数据.玩家属性.攻击力 = 服务端数据.attributes.attack;
            当前玩家数据.玩家属性.防御力 = 服务端数据.attributes.defense;
            当前玩家数据.玩家属性.暴击率 = 服务端数据.attributes.critRate;
        }

        // 国家信息（如果有，countryId > 0 表示有国家）
        if (服务端数据.country != null && 服务端数据.countryId > 0)
        {
            // 从全局变量中查找国家，如果不存在则创建
            国家信息库 国家 = 全局方法类.获取指定名字的国家(服务端数据.country.name);
            if (国家 == null)
            {
                国家 = new 国家信息库();
                国家.国家ID = 服务端数据.countryId;
                国家.国名 = 服务端数据.country.name;
                国家.国号 = 服务端数据.country.code;
                全局变量.所有国家列表.Add(国家);
            }
            当前玩家数据.国家 = 国家;
        }
        else
        {
            当前玩家数据.国家 = null;
        }

        // 家族信息（如果有，clanId > 0 表示有家族）
        if (服务端数据.clan != null && 服务端数据.clanId > 0)
        {
            // 从全局变量中查找家族，如果不存在则创建
            家族信息库 家族 = 全局方法类.获取指定名字的家族(服务端数据.clan.name);
            if (家族 == null)
            {
                家族 = new 家族信息库();
                家族.家族ID = 服务端数据.clanId;
                家族.家族名字 = 服务端数据.clan.name;
                全局变量.所有家族列表.Add(家族);
            }
            当前玩家数据.家族 = 家族;
        }
        else
        {
            当前玩家数据.家族 = null;
        }

        // 添加到全局玩家列表（如果还没有）
        if (!全局变量.所有玩家数据表.Contains(当前玩家数据))
        {
            全局变量.所有玩家数据表.Add(当前玩家数据);
        }

        Debug.Log($"玩家数据转换完成：{当前玩家数据.姓名}，等级{当前玩家数据.等级}，铜钱{当前玩家数据.铜钱}");
    }
}

// =================== 服务端返回的数据结构 ===================

[System.Serializable]
public class 获取玩家响应
{
    public bool success;
    public string message;
    public 服务端玩家数据 data;
}

[System.Serializable]
public class 创建玩家响应
{
    public bool success;
    public string message;
}

[System.Serializable]
public class 服务端玩家数据
{
    public int id;
    public string name;
    public string gender;
    public int level;
    public string titleName;
    public string office;
    public int copperMoney;
    public int gold;
    public int countryId; // Unity JsonUtility 不支持可空类型，用 -1 表示 null
    public int clanId;    // Unity JsonUtility 不支持可空类型，用 -1 表示 null
    public 服务端玩家属性 attributes;
    public 服务端国家数据 country;
    public 服务端家族数据 clan;
}

[System.Serializable]
public class 服务端玩家属性
{
    public int maxHp;
    public int currentHp;
    public int attack;
    public int defense;
    public float critRate;
}

[System.Serializable]
public class 服务端国家数据
{
    public int id;
    public string name;
    public string code;
}

[System.Serializable]
public class 服务端家族数据
{
    public int id;
    public string name;
}

