using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using 玩家数据结构;
using 国家系统;

public class 玩家数据管理 : MonoBehaviour
{
    public static 玩家数据管理 实例 { get; private set; }

    [Header("接口地址")]
    private string 获取玩家地址 = "http://43.139.181.191:5000/api/getPlayer";
    private string 创建玩家地址 = "http://43.139.181.191:5000/api/createPlayer";
    private string 心跳地址 = "http://43.139.181.191:5000/api/heartbeat";
    private string 登出地址 = "http://43.139.181.191:5000/api/logout";
    
    // 心跳相关
    private Coroutine 心跳协程;
    private bool 是否已登录 = false;

    public 创建角色界面 创建界面;
    public 显示角色信息界面 显示角色信息界面;
    public 国家信息显示 国家信息显示; // 主场景的国家信息显示组件

    // 当前玩家数据（从服务器加载后存储在这里）
    public 玩家数据 当前玩家数据 { get; private set; }

    // 加载动画相关
    private GameObject 加载动画对象;
    public 加载动画 加载动画组件 { get; private set; }

    private void Awake()
    {
        // 简单单例：跨场景持久存在，避免重复创建
        if (实例 != null && 实例 != this)
        {
            Destroy(gameObject);
            return;
        }

        实例 = this;
        DontDestroyOnLoad(gameObject);

        // 初始化加载动画（从Resources加载预制体）
        初始化加载动画();
    }
    
    private void OnEnable()
    {
        // 场景切换后，如果已经登录但心跳未运行，重新启动心跳
        // 这可以防止场景切换导致心跳停止的问题
        if (是否已登录 && 心跳协程 == null)
        {
            int accountId = PlayerPrefs.GetInt("AccountId", -1);
            if (accountId > 0)
            {
                Debug.Log("检测到已登录但心跳未运行，重新启动心跳");
                启动心跳();
            }
        }
    }

    /// <summary>
    /// 初始化加载动画（从Resources加载预制体）
    /// </summary>
    private void 初始化加载动画()
    {
        // 从Resources文件夹加载预制体（路径：Resources/通用界面/加载动画）
        GameObject 加载动画预制体 = Resources.Load<GameObject>("通用界面/加载动画");
        if (加载动画预制体 != null)
        {
            加载动画对象 = Instantiate(加载动画预制体);
            加载动画组件 = 加载动画对象.GetComponent<加载动画>();
            if (加载动画组件 == null)
            {
                Debug.LogError("加载动画预制体上未找到 加载动画 组件！");
            }
            // 初始状态隐藏
            加载动画对象.SetActive(false);
            // 设置为DontDestroyOnLoad，确保跨场景存在
            DontDestroyOnLoad(加载动画对象);
        }
        else
        {
            Debug.LogError("无法从Resources/通用界面/加载动画加载预制体！请检查路径是否正确。");
        }
    }

    /// <summary>
    /// 获取玩家数据（登录成功后调用）
    /// </summary>
    /// <param name="accountId">账号ID</param>
    /// <param name="是否自动显示UI">是否自动显示角色信息界面，默认true（登录时使用），重连时传入false</param>
    public void 获取玩家数据(int accountId, bool 是否自动显示UI = true)
    {
        StartCoroutine(发送获取玩家请求(accountId, 是否自动显示UI));
    }

    /// <summary>
    /// 创建角色（当账号没有角色时调用）
    /// </summary>
    public void 创建角色(int accountId, string 姓名, string 性别)
    {
        StartCoroutine(发送创建玩家请求(accountId, 姓名, 性别));
    }

    IEnumerator 发送获取玩家请求(int accountId, bool 是否自动显示UI = true)
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
                        
                        // 启动心跳（如果还没有启动）
                        if (!是否已登录)
                        {
                            启动心跳();
                            
                            // 建立 SignalR 连接
                            if (SignalR连接管理.实例 != null)
                            {
                                SignalR连接管理.实例.建立连接();
                                
                                // 如果玩家有家族，加入家族组
                                if (当前玩家数据.家族 != null && 当前玩家数据.家族.家族ID > 0)
                                {
                                    SignalR连接管理.实例.加入家族组(当前玩家数据.家族.家族ID);
                                }
                            }
                        }
                        
                        // 只有在需要自动显示UI时才显示界面（登录时显示，重连时不显示）
                        if (是否自动显示UI)
                        {
                            if (创建界面 != null)
                            {
                                创建界面.gameObject.SetActive(false);
                            }

                            // 显示角色信息界面
                            if (显示角色信息界面 != null)
                            {
                                显示角色信息界面.gameObject.SetActive(true);
                                显示角色信息界面.显示角色信息(当前玩家数据);
                            }
                        }
                    }
                    else
                    {
                        // 没有角色，需要创建
                        Debug.Log("该账号尚未创建角色，需要创建角色");
                        
                        // 启动心跳（如果还没有启动）
                        if (!是否已登录)
                        {
                            启动心跳();
                        }

                        // 隐藏角色信息界面（如果有）
                        if (显示角色信息界面 != null)
                        {
                            显示角色信息界面.gameObject.SetActive(false);
                        }

                        // 显示创建角色界面，并传入账号ID
                        if (创建界面 != null)
                        {
                            创建界面.gameObject.SetActive(true);
                            创建界面.设置账号ID(accountId);
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
                        if (创建界面 != null)
                        {
                            创建界面.gameObject.SetActive(false);
                        }
                        
                        // 创建成功后，重新获取玩家数据（会自动显示角色信息界面）
                        获取玩家数据(accountId);
                    }
                    else
                    {
                        Debug.LogError("创建角色失败: " + 响应.message);
                        
                        // 创建失败时，在创建角色界面的提示文本中显示错误信息
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
        当前玩家数据.经验值 = 服务端数据.experience;  // 经验值
        当前玩家数据.称号名 = 服务端数据.titleName;
        当前玩家数据.铜钱 = 服务端数据.copperMoney;
        当前玩家数据.黄金 = 服务端数据.gold;
        当前玩家数据.家族贡献值 = 服务端数据.clanContribution;

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
            // 如果玩家原来有国家且不同于现在的国家，需要从原国家成员表中移除
            if (当前玩家数据.国家 != null && 当前玩家数据.国家.国家ID != 服务端数据.countryId)
            {
                if (当前玩家数据.国家.国家成员表.Contains(当前玩家数据))
                {
                    当前玩家数据.国家.国家成员表.Remove(当前玩家数据);
                }
            }

            // 优先通过国家ID从全局变量中查找国家（这样能找到完整信息）
            国家信息库 国家 = null;
            foreach (var 国家项 in 全局变量.所有国家列表)
            {
                if (国家项.国家ID == 服务端数据.countryId)
                {
                    国家 = 国家项;
                    break;
                }
            }

            // 如果通过ID找不到，再通过名字查找
            if (国家 == null)
            {
                国家 = 全局方法类.获取指定名字的国家(服务端数据.country.name);
            }

            // 如果还是找不到，创建一个新的（但需要从服务器获取完整信息）
            if (国家 == null)
            {
                国家 = new 国家信息库();
                国家.国家ID = 服务端数据.countryId;
                国家.国名 = 服务端数据.country.name;
                国家.国号 = 服务端数据.country.code;
                // 注意：这里创建的国家对象缺少黄金、铜钱等信息
                // 这些信息会在国家信息显示刷新时从服务器获取
                全局变量.所有国家列表.Add(国家);
            }
            else
            {
                // 如果找到了，确保国家信息是最新的（更新基本字段）
                国家.国家ID = 服务端数据.countryId;
                国家.国名 = 服务端数据.country.name;
                国家.国号 = 服务端数据.country.code;
            }

            // 把玩家加入到该国家的成员表中（如果还没有）
            if (!国家.国家成员表.Contains(当前玩家数据))
            {
                国家.国家成员表.Add(当前玩家数据);
            }

            当前玩家数据.国家 = 国家;
        }
        else
        {
            // 如果服务端返回没有国家，但本地还有旧国家引用，需要从旧国家成员表中移除
            if (当前玩家数据.国家 != null &&
                当前玩家数据.国家.国家成员表.Contains(当前玩家数据))
            {
                当前玩家数据.国家.国家成员表.Remove(当前玩家数据);
            }

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

        Debug.Log($"玩家数据转换完成：{当前玩家数据.姓名}，等级{当前玩家数据.等级}，铜钱{当前玩家数据.铜钱}，国家：{(当前玩家数据.国家 != null ? 当前玩家数据.国家.国名 : "无")}");

        // 建立 WebSocket 连接（用于接收实时事件通知）
        if (SignalR连接管理.实例 != null)
        {
            SignalR连接管理.实例.建立连接();
            
            // 如果玩家有家族，加入家族组
            if (当前玩家数据.家族 != null && 当前玩家数据.家族.家族ID > 0)
            {
                SignalR连接管理.实例.加入家族组(当前玩家数据.家族.家族ID);
            }
        }
        else
        {
            Debug.LogWarning("SignalR连接管理 实例不存在，无法建立 WebSocket 连接");
        }

        // 如果国家信息显示组件存在，自动刷新国家信息
        // 使用协程延迟一帧刷新，确保数据已经完全更新
        if (国家信息显示 != null)
        {
            StartCoroutine(延迟刷新国家信息());
        }
        else
        {
            Debug.LogWarning("国家信息显示组件未设置，无法自动刷新");
        }
    }

    /// <summary>
    /// 延迟一帧刷新国家信息，确保数据已经完全更新
    /// </summary>
    IEnumerator 延迟刷新国家信息()
    {
        // 等待一帧，确保数据已经完全更新
        yield return null;
        
        // 确保UI GameObject是激活的
        if (国家信息显示 != null && 国家信息显示.gameObject != null)
        {
            if (!国家信息显示.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("国家信息显示UI未激活，无法刷新");
            }
            else
            {
                Debug.Log("开始刷新国家信息显示");
                国家信息显示.刷新显示();
            }
        }
    }

    /// <summary>
    /// 延迟指定秒数后刷新国家信息（用于更换国家后的延迟刷新）
    /// 会显示加载动画，并在延迟期间平滑更新进度
    /// </summary>
    public void 延迟刷新国家信息(国家信息显示 国家信息显示组件, float 延迟秒数)
    {
        StartCoroutine(延迟刷新国家信息协程(国家信息显示组件, 延迟秒数));
    }

    IEnumerator 延迟刷新国家信息协程(国家信息显示 国家信息显示组件, float 延迟秒数)
    {
        Debug.Log($"准备等待{延迟秒数}秒后刷新国家信息显示");
        
        // 使用通用加载动画方法，让滑动条平滑移动
        if (加载动画组件 != null)
        {
            加载动画组件.开始加载动画(延迟秒数, "正在更新国家信息...");
        }
        
        // 等待指定时间
        yield return new WaitForSeconds(延迟秒数);
        
        Debug.Log($"{延迟秒数}秒等待完成，准备刷新国家信息显示");
        
        // 刷新国家信息显示
        if (国家信息显示组件 != null && 国家信息显示组件.gameObject != null)
        {
            Debug.Log($"国家信息显示组件存在，GameObject状态: {国家信息显示组件.gameObject.activeInHierarchy}");
            Debug.Log("开始刷新国家信息显示");
            国家信息显示组件.刷新显示();
        }
        else
        {
            Debug.LogError("国家信息显示组件为null或GameObject为null，无法刷新！");
        }
    }
    
    /// <summary>
    /// 启动心跳协程（每30秒发送一次心跳）
    /// </summary>
    private void 启动心跳()
    {
        if (心跳协程 != null)
        {
            StopCoroutine(心跳协程);
        }
        
        是否已登录 = true;
        心跳协程 = StartCoroutine(心跳协程方法());
        Debug.Log("心跳已启动，每30秒发送一次");
    }
    
    /// <summary>
    /// 停止心跳协程
    /// </summary>
    private void 停止心跳()
    {
        if (心跳协程 != null)
        {
            StopCoroutine(心跳协程);
            心跳协程 = null;
        }
        是否已登录 = false;
        Debug.Log("心跳已停止");
    }
    
    /// <summary>
    /// 心跳协程方法（每30秒发送一次心跳请求）
    /// </summary>
    IEnumerator 心跳协程方法()
    {
        while (是否已登录)
        {
            yield return new WaitForSeconds(30f); // 等待30秒
            
            int accountId = PlayerPrefs.GetInt("AccountId", -1);
            if (accountId <= 0)
            {
                Debug.LogWarning("心跳失败：未找到账号ID");
                停止心跳();
                yield break;
            }
            
            // 发送心跳请求
            StartCoroutine(发送心跳请求(accountId));
        }
    }
    
    /// <summary>
    /// 发送心跳请求到服务器
    /// </summary>
    IEnumerator 发送心跳请求(int accountId)
    {
        string json数据 = $"{{\"accountId\":{accountId}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(心跳地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogWarning("心跳请求失败: " + 请求.error);
                // 心跳失败可能是网络问题，不停止心跳，继续尝试
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                心跳响应 响应 = JsonUtility.FromJson<心跳响应>(返回文本);
                if (响应 != null)
                {
                    if (!响应.success)
                    {
                        Debug.LogWarning("心跳失败: " + 响应.message);
                        // 如果服务器返回账号未在线，停止心跳
                        if (响应.message.Contains("账号未在线"))
                        {
                            停止心跳();
                        }
                    }
                    else
                    {
                        //Debug.Log("心跳成功");
                        
                        // 检查家族ID是否有变化（用于检测被踢出家族等情况）
                        if (当前玩家数据 != null)
                        {
                            int 本地家族ID = 当前玩家数据.家族 != null ? 当前玩家数据.家族.家族ID : -1;
                            int 服务器家族ID = 响应.clanId;
                            
                            // 如果家族ID不一致，说明家族状态有变化，需要刷新玩家数据
                            if (本地家族ID != 服务器家族ID)
                            {
                                Debug.Log($"检测到家族ID变化：本地={本地家族ID}，服务器={服务器家族ID}，刷新玩家数据");
                                
                                // 如果从有家族变为无家族，显示提示
                                if (本地家族ID > 0 && 服务器家族ID <= 0)
                                {
                                    通用提示框.显示("你已被踢出家族");
                                }
                                
                                // 刷新玩家数据
                                获取玩家数据(accountId);
                            }
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// 登出（停止心跳并通知服务器）
    /// </summary>
    public void 登出()
    {
        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId > 0)
        {
            StartCoroutine(发送登出请求(accountId));
        }
        
        停止心跳();
    }
    
    /// <summary>
    /// 发送登出请求到服务器
    /// </summary>
    IEnumerator 发送登出请求(int accountId)
    {
        string json数据 = $"{{\"accountId\":{accountId}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(登出地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogWarning("登出请求失败: " + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("登出响应: " + 返回文本);
            }
        }
    }
    
    private void OnDestroy()
    {
        // 组件销毁时停止心跳
        停止心跳();
    }
}

// =================== 心跳响应数据类 ===================

[System.Serializable]
public class 心跳响应
{
    public bool success;
    public string message;
    public int clanId;  // 当前玩家的家族ID（-1表示没有家族）
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
    public int experience;  // 当前经验值
    public string titleName;
    public string office;
    public int copperMoney;
    public int gold;
    public int countryId; // Unity JsonUtility 不支持可空类型，用 -1 表示 null
    public int clanId;    // Unity JsonUtility 不支持可空类型，用 -1 表示 null
    public int clanContribution;  // 家族贡献值
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

