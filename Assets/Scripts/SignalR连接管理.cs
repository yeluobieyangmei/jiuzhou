using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;
using 玩家数据结构;

/// <summary>
/// SignalR 连接管理器
/// 用于管理与服务器的实时通信连接
/// 
/// 注意：此脚本需要 SignalR Unity 客户端库支持
/// 安装方法：
/// 1. 下载 Microsoft.AspNetCore.SignalR.Client NuGet 包
/// 2. 或者使用 Unity Package Manager 添加 SignalR 客户端库
/// 3. 或者手动添加 SignalR 客户端 DLL 到 Unity 项目
/// </summary>
public class SignalR连接管理 : MonoBehaviour
{
    public static SignalR连接管理 实例 { get; private set; }

    // SignalR Hub 连接地址
    private string hubUrl = "http://43.139.181.191:5000/gameHub";

    // SignalR 连接对象（需要根据实际使用的库来定义类型）
    // 示例：private HubConnection? hubConnection;
    
    // 连接状态
    private bool 是否已连接 = false;
    private bool 是否正在连接 = false;

    // 当前玩家所属的家族ID（用于加入/离开家族组）
    private int 当前家族ID = -1;

    private void Awake()
    {
        // 单例模式
        if (实例 != null && 实例 != this)
        {
            Destroy(gameObject);
            return;
        }

        实例 = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        // 断开连接
        断开连接();
    }

    /// <summary>
    /// 建立 SignalR 连接
    /// </summary>
    public async void 建立连接()
    {
        if (是否已连接 || 是否正在连接)
        {
            Debug.LogWarning("SignalR 连接已存在或正在连接中");
            return;
        }

        是否正在连接 = true;

        try
        {
            // TODO: 根据实际使用的 SignalR 客户端库实现连接逻辑
            // 示例代码（使用 Microsoft.AspNetCore.SignalR.Client）：
            /*
            hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .Build();

            // 注册事件处理方法
            hubConnection.On<ClanMemberKickedEvent>("OnGameEvent", 处理游戏事件);

            // 开始连接
            await hubConnection.StartAsync();
            是否已连接 = true;
            是否正在连接 = false;
            Debug.Log("SignalR 连接成功");
            */

            Debug.LogWarning("SignalR 连接功能需要安装 SignalR Unity 客户端库后才能使用");
        }
        catch (Exception ex)
        {
            Debug.LogError($"SignalR 连接失败: {ex.Message}");
            是否正在连接 = false;
        }
    }

    /// <summary>
    /// 断开 SignalR 连接
    /// </summary>
    public async void 断开连接()
    {
        if (!是否已连接)
        {
            return;
        }

        try
        {
            // TODO: 根据实际使用的 SignalR 客户端库实现断开逻辑
            /*
            if (hubConnection != null)
            {
                await hubConnection.StopAsync();
                await hubConnection.DisposeAsync();
                hubConnection = null;
            }
            */

            是否已连接 = false;
            当前家族ID = -1;
            Debug.Log("SignalR 连接已断开");
        }
        catch (Exception ex)
        {
            Debug.LogError($"断开 SignalR 连接失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加入家族组（当玩家加入家族时调用）
    /// </summary>
    public async void 加入家族组(int clanId)
    {
        if (!是否已连接)
        {
            Debug.LogWarning("SignalR 未连接，无法加入家族组");
            return;
        }

        try
        {
            // TODO: 根据实际使用的 SignalR 客户端库实现加入组逻辑
            /*
            if (hubConnection != null)
            {
                await hubConnection.InvokeAsync("JoinClanGroup", clanId);
                当前家族ID = clanId;
                Debug.Log($"已加入家族组: clan_{clanId}");
            }
            */
        }
        catch (Exception ex)
        {
            Debug.LogError($"加入家族组失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 离开家族组（当玩家离开家族时调用）
    /// </summary>
    public async void 离开家族组(int clanId)
    {
        if (!是否已连接)
        {
            return;
        }

        try
        {
            // TODO: 根据实际使用的 SignalR 客户端库实现离开组逻辑
            /*
            if (hubConnection != null)
            {
                await hubConnection.InvokeAsync("LeaveClanGroup", clanId);
                if (当前家族ID == clanId)
                {
                    当前家族ID = -1;
                }
                Debug.Log($"已离开家族组: clan_{clanId}");
            }
            */
        }
        catch (Exception ex)
        {
            Debug.LogError($"离开家族组失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理游戏事件（从服务器接收的事件）
    /// </summary>
    public void 处理游戏事件(GameEventMessage eventMessage)
    {
        if (eventMessage == null)
        {
            return;
        }

        Debug.Log($"收到游戏事件: {eventMessage.eventType}");

        switch (eventMessage.eventType)
        {
            case "ClanMemberKicked":
                处理成员被踢出事件(JsonUtility.FromJson<ClanMemberKickedEvent>(JsonUtility.ToJson(eventMessage)));
                break;
            case "ClanRoleAppointed":
                处理职位任命事件(JsonUtility.FromJson<ClanRoleAppointedEvent>(JsonUtility.ToJson(eventMessage)));
                break;
            case "ClanDisbanded":
                处理家族解散事件(JsonUtility.FromJson<ClanDisbandedEvent>(JsonUtility.ToJson(eventMessage)));
                break;
            case "ClanMemberJoined":
                处理成员加入事件(JsonUtility.FromJson<ClanMemberJoinedEvent>(JsonUtility.ToJson(eventMessage)));
                break;
            case "ClanMemberLeft":
                处理成员离开事件(JsonUtility.FromJson<ClanMemberLeftEvent>(JsonUtility.ToJson(eventMessage)));
                break;
            case "ClanDonated":
                处理家族捐献事件(JsonUtility.FromJson<ClanDonatedEvent>(JsonUtility.ToJson(eventMessage)));
                break;
            default:
                Debug.LogWarning($"未知的游戏事件类型: {eventMessage.eventType}");
                break;
        }
    }

    /// <summary>
    /// 处理成员被踢出事件
    /// </summary>
    private void 处理成员被踢出事件(ClanMemberKickedEvent evt)
    {
        if (evt == null || evt.clanId <= 0) return;

        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null) return;

        // 如果被踢出的是当前玩家自己
        if (当前玩家.ID == evt.kickedPlayerId)
        {
            通用提示框.显示($"你已被{evt.operatorName}踢出家族");
            
            // 清除本地家族数据
            if (当前玩家.家族 != null)
            {
                if (当前玩家.家族.家族成员.Contains(当前玩家))
                {
                    当前玩家.家族.家族成员.Remove(当前玩家);
                }
            }
            当前玩家.家族 = null;
            当前玩家.家族职位 = "";

            // 离开家族组
            离开家族组(evt.clanId);

            // 刷新玩家数据
            int accountId = PlayerPrefs.GetInt("AccountId", -1);
            if (accountId > 0 && 玩家数据管理.实例 != null)
            {
                玩家数据管理.实例.获取玩家数据(accountId);
            }

            // 刷新UI
            家族信息显示 家族信息显示组件1 = FindObjectOfType<家族信息显示>();
            if (家族信息显示组件1 != null)
            {
                家族信息显示组件1.刷新显示();
            }
        }
        else
        {
            // 被踢出的是其他玩家，刷新家族成员列表
            if (当前玩家.家族 != null && 当前玩家.家族.家族ID == evt.clanId)
            {
                通用提示框.显示($"{evt.kickedPlayerName}已被{evt.operatorName}踢出家族");
                
                // 刷新家族成员列表
                玩家列表显示 玩家列表显示组件 = FindObjectOfType<玩家列表显示>();
                if (玩家列表显示组件 != null && 玩家列表显示组件.当前显示类型 == 玩家列表显示.显示类型.家族玩家查看)
                {
                    玩家列表显示组件.StartCoroutine(玩家列表显示组件.获取家族成员列表(evt.clanId));
                }

                // 刷新家族信息显示
                家族信息显示 家族信息显示组件 = FindObjectOfType<家族信息显示>();
                if (家族信息显示组件 != null)
                {
                    家族信息显示组件.刷新显示();
                }
            }
        }
    }

    /// <summary>
    /// 处理职位任命事件
    /// </summary>
    private void 处理职位任命事件(ClanRoleAppointedEvent evt)
    {
        if (evt == null || evt.clanId <= 0) return;

        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null) return;

        // 如果被任命的是当前玩家自己
        if (当前玩家.ID == evt.playerId)
        {
            string 消息 = $"你已被{evt.operatorName}任命为{evt.role}";
            if (evt.replacedPlayerId.HasValue && evt.replacedPlayerId.Value > 0)
            {
                消息 += $"（顶替了{evt.replacedPlayerName}）";
            }
            通用提示框.显示(消息);

            // 更新当前玩家的家族职位
            当前玩家.家族职位 = evt.role;

            // 刷新家族信息显示
            家族信息显示 家族信息显示组件 = FindObjectOfType<家族信息显示>();
            if (家族信息显示组件 != null)
            {
                家族信息显示组件.刷新显示();
            }
        }

        // 如果被顶替的是当前玩家
        if (evt.replacedPlayerId.HasValue && evt.replacedPlayerId.Value > 0 && 当前玩家.ID == evt.replacedPlayerId.Value)
        {
            通用提示框.显示($"你被{evt.operatorName}降为成员（{evt.playerName}被任命为{evt.role}）");

            // 更新当前玩家的家族职位
            当前玩家.家族职位 = "成员";

            // 刷新家族信息显示
            家族信息显示 家族信息显示组件 = FindObjectOfType<家族信息显示>();
            if (家族信息显示组件 != null)
            {
                家族信息显示组件.刷新显示();
            }
        }

        // 刷新家族成员列表（如果正在查看）
        if (当前玩家.家族 != null && 当前玩家.家族.家族ID == evt.clanId)
        {
            玩家列表显示 玩家列表显示组件 = FindObjectOfType<玩家列表显示>();
            if (玩家列表显示组件 != null && 玩家列表显示组件.当前显示类型 == 玩家列表显示.显示类型.家族玩家查看)
            {
                玩家列表显示组件.StartCoroutine(玩家列表显示组件.获取家族成员列表(evt.clanId));
            }
        }
    }

    /// <summary>
    /// 处理家族解散事件
    /// </summary>
    private void 处理家族解散事件(ClanDisbandedEvent evt)
    {
        if (evt == null || evt.clanId <= 0) return;

        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null) return;

        // 检查当前玩家是否属于被解散的家族
        if (当前玩家.家族 != null && 当前玩家.家族.家族ID == evt.clanId)
        {
            通用提示框.显示($"家族{evt.clanName}已被{evt.operatorName}解散");

            // 清除本地家族数据
            if (当前玩家.家族.家族成员.Contains(当前玩家))
            {
                当前玩家.家族.家族成员.Remove(当前玩家);
            }
            当前玩家.家族 = null;
            当前玩家.家族职位 = "";

            // 离开家族组
            离开家族组(evt.clanId);

            // 刷新玩家数据
            int accountId = PlayerPrefs.GetInt("AccountId", -1);
            if (accountId > 0 && 玩家数据管理.实例 != null)
            {
                玩家数据管理.实例.获取玩家数据(accountId);
            }

            // 刷新UI
            家族信息显示 家族信息显示组件 = FindObjectOfType<家族信息显示>();
            if (家族信息显示组件 != null)
            {
                家族信息显示组件.刷新显示();
            }
        }
    }

    /// <summary>
    /// 处理成员加入事件
    /// </summary>
    private void 处理成员加入事件(ClanMemberJoinedEvent evt)
    {
        if (evt == null) return;

        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null) return;

        // 如果当前玩家属于该家族，刷新成员列表
        if (当前玩家.家族 != null && 当前玩家.家族.家族ID == evt.clanId)
        {
            玩家列表显示 玩家列表显示组件 = FindObjectOfType<玩家列表显示>();
            if (玩家列表显示组件 != null && 玩家列表显示组件.当前显示类型 == 玩家列表显示.显示类型.家族玩家查看)
            {
                玩家列表显示组件.StartCoroutine(玩家列表显示组件.获取家族成员列表(evt.clanId));
            }

            // 刷新家族信息显示
            家族信息显示 家族信息显示组件 = FindObjectOfType<家族信息显示>();
            if (家族信息显示组件 != null)
            {
                家族信息显示组件.刷新显示();
            }
        }
    }

    /// <summary>
    /// 处理成员离开事件
    /// </summary>
    private void 处理成员离开事件(ClanMemberLeftEvent evt)
    {
        if (evt == null || evt.clanId <= 0) return;

        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null) return;

        // 如果当前玩家属于该家族，刷新成员列表
        if (当前玩家.家族 != null && 当前玩家.家族.家族ID == evt.clanId)
        {
            玩家列表显示 玩家列表显示组件 = FindObjectOfType<玩家列表显示>();
            if (玩家列表显示组件 != null && 玩家列表显示组件.当前显示类型 == 玩家列表显示.显示类型.家族玩家查看)
            {
                玩家列表显示组件.StartCoroutine(玩家列表显示组件.获取家族成员列表(evt.clanId));
            }

            // 刷新家族信息显示
            家族信息显示 家族信息显示组件 = FindObjectOfType<家族信息显示>();
            if (家族信息显示组件 != null)
            {
                家族信息显示组件.刷新显示();
            }
        }
    }

    /// <summary>
    /// 处理家族捐献事件
    /// </summary>
    private void 处理家族捐献事件(ClanDonatedEvent evt)
    {
        if (evt == null || evt.clanId <= 0) return;

        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null) return;

        // 如果当前玩家属于该家族，刷新家族信息
        if (当前玩家.家族 != null && 当前玩家.家族.家族ID == evt.clanId)
        {
            // 刷新家族信息显示（显示最新的资金和繁荣值）
            家族信息显示 家族信息显示组件 = FindObjectOfType<家族信息显示>();
            if (家族信息显示组件 != null)
            {
                家族信息显示组件.刷新显示();
            }
        }
    }
}

// =================== 游戏事件消息类（与服务器端对应）===================

[System.Serializable]
public class GameEventMessage
{
    public string eventType;
    public string timestamp;
}

[System.Serializable]
public class ClanMemberKickedEvent : GameEventMessage
{
    public int clanId;
    public int kickedPlayerId;
    public string kickedPlayerName;
    public int operatorId;
    public string operatorName;
}

[System.Serializable]
public class ClanRoleAppointedEvent : GameEventMessage
{
    public int clanId;
    public int playerId;
    public string playerName;
    public string role;
    public int operatorId;
    public string operatorName;
    public int? replacedPlayerId;
    public string replacedPlayerName;
}

[System.Serializable]
public class ClanDisbandedEvent : GameEventMessage
{
    public int clanId;
    public string clanName;
    public int operatorId;
    public string operatorName;
}

[System.Serializable]
public class ClanMemberJoinedEvent : GameEventMessage
{
    public int clanId;
    public int playerId;
    public string playerName;
}

[System.Serializable]
public class ClanMemberLeftEvent : GameEventMessage
{
    public int clanId;
    public int playerId;
    public string playerName;
}

[System.Serializable]
public class ClanDonatedEvent : GameEventMessage
{
    public int clanId;
    public int playerId;
    public string playerName;
    public int donationAmount;
    public int fundsAdded;
    public int prosperityAdded;
}

