using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Net.WebSockets;
using System.Text;
using 玩家数据结构;

/// <summary>
/// WebSocket 连接管理器
/// 用于管理与服务器的实时通信连接（使用自建的 WebSocket 端点）
/// </summary>
public class SignalR连接管理 : MonoBehaviour
{
    public static SignalR连接管理 实例 { get; private set; }

    // WebSocket 服务器地址（对应服务端自建的 /ws 端点）
    private string hubUrl = "ws://43.139.181.191:5000/ws";

    // WebSocket 连接对象
    private ClientWebSocket webSocket;
    private CancellationTokenSource cancellationTokenSource;
    private Task receiveTask;
    
    // 消息队列（用于在主线程处理消息）
    private Queue<string> 消息队列 = new Queue<string>();
    private readonly object 消息队列锁 = new object();
    
    // 连接状态
    private bool 是否已连接 = false;
    private bool 是否正在连接 = false;

    // 当前玩家所属的家族ID（用于加入/离开家族组）
    private int 当前家族ID = -1;

    // UI 组件引用缓存（避免使用 FindObjectOfType）
    private 家族显示判断 家族显示判断组件缓存;
    private 家族信息显示 家族信息显示组件缓存;
    private 玩家列表显示 玩家列表显示组件缓存;

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

    private void Start()
    {
        // 缓存 UI 组件引用（在 Start 中获取，确保场景中的组件已初始化）
        // 注意：这些组件可能不在当前场景，所以缓存可能为 null，需要在需要时重新获取
    }

    /// <summary>
    /// 获取家族显示判断组件（带缓存）
    /// </summary>
    private 家族显示判断 获取家族显示判断()
    {
        if (家族显示判断组件缓存 == null)
        {
            家族显示判断组件缓存 = 家族显示判断.实例;
        }
        return 家族显示判断组件缓存;
    }

    /// <summary>
    /// 获取家族信息显示组件（带缓存）
    /// </summary>
    private 家族信息显示 获取家族信息显示()
    {
        if (家族信息显示组件缓存 == null)
        {
            // 通过家族显示判断获取引用（如果存在）
            var 显示判断 = 获取家族显示判断();
            if (显示判断 != null && 显示判断.家族信息显示 != null)
            {
                家族信息显示组件缓存 = 显示判断.家族信息显示;
            }
        }
        return 家族信息显示组件缓存;
    }

    /// <summary>
    /// 获取玩家列表显示组件（带缓存）
    /// </summary>
    private 玩家列表显示 获取玩家列表显示()
    {
        if (玩家列表显示组件缓存 == null)
        {
            // 通过家族信息显示获取引用（如果存在）
            var 家族信息 = 获取家族信息显示();
            if (家族信息 != null && 家族信息.玩家列表显示 != null)
            {
                玩家列表显示组件缓存 = 家族信息.玩家列表显示;
            }
        }
        return 玩家列表显示组件缓存;
    }

    private void OnDestroy()
    {
        // 断开连接
        断开连接();
    }

    /// <summary>
    /// 建立 WebSocket 连接
    /// </summary>
    public async void 建立连接()
    {
        if (是否已连接 || 是否正在连接)
        {
            Debug.LogWarning("WebSocket 连接已存在或正在连接中");
            return;
        }

        是否正在连接 = true;

        try
        {
            // 创建 WebSocket 客户端
            webSocket = new ClientWebSocket();
            cancellationTokenSource = new CancellationTokenSource();

            // 连接到服务器
            Uri serverUri = new Uri(hubUrl);
            await webSocket.ConnectAsync(serverUri, cancellationTokenSource.Token);

            是否已连接 = true;
            是否正在连接 = false;
            Debug.Log("WebSocket 连接成功");

            // 启动消息接收循环（在后台线程）
            receiveTask = Task.Run(() => 接收消息循环(cancellationTokenSource.Token));

            // 延迟注册玩家ID到服务器（确保玩家数据已加载）
            StartCoroutine(延迟注册玩家ID());
        }
        catch (Exception ex)
        {
            Debug.LogError($"WebSocket 连接失败: {ex.Message}");
            是否正在连接 = false;
            webSocket?.Dispose();
            webSocket = null;
        }
    }

    /// <summary>
    /// 延迟注册玩家ID到服务器（用于定向推送消息）
    /// </summary>
    private IEnumerator 延迟注册玩家ID()
    {
        // 等待1秒，确保玩家数据已加载完成
        yield return new WaitForSeconds(1f);

        if (!是否已连接 || webSocket == null) yield break;

        var 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null || 当前玩家.ID <= 0)
        {
            Debug.LogWarning("玩家数据未加载，无法注册玩家ID");
            yield break;
        }

        // 直接调用异步方法（fire-and-forget，不等待完成）
        string message = $"{{\"type\":\"registerPlayerId\",\"playerId\":{当前玩家.ID}}}";
        _ = 发送消息(message); // 使用 discard 操作符，忽略返回的 Task
        Debug.Log($"正在注册玩家ID: {当前玩家.ID}");
    }

    /// <summary>
    /// 断开 WebSocket 连接
    /// </summary>
    public async void 断开连接()
    {
        if (!是否已连接 && webSocket == null)
        {
            return;
        }

        try
        {
            // 取消接收任务
            cancellationTokenSource?.Cancel();

            // 等待接收任务完成
            if (receiveTask != null)
            {
                try
                {
                    await receiveTask;
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，忽略
                }
            }

            // 关闭 WebSocket 连接
            if (webSocket != null && webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "客户端断开", CancellationToken.None);
            }

            webSocket?.Dispose();
            webSocket = null;
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;

            是否已连接 = false;
            当前家族ID = -1;
            Debug.Log("WebSocket 连接已断开");
        }
        catch (Exception ex)
        {
            Debug.LogError($"断开 WebSocket 连接失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加入家族组（当玩家加入家族时调用）
    /// 发送 JSON 消息到服务器，服务器会根据消息内容将连接加入对应的家族组
    /// </summary>
    public async void 加入家族组(int clanId)
    {
        if (!是否已连接 || webSocket == null)
        {
            Debug.LogWarning("WebSocket 未连接，无法加入家族组");
            return;
        }

        try
        {
            string message = $"{{\"type\":\"joinGroup\",\"clanId\":{clanId}}}";
            await 发送消息(message);
            当前家族ID = clanId;
            Debug.Log($"已发送加入家族组消息: clan_{clanId}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"加入家族组失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 离开家族组（当玩家离开家族时调用）
    /// 发送 JSON 消息到服务器，服务器会将连接从对应的家族组中移除
    /// </summary>
    public async void 离开家族组(int clanId)
    {
        if (!是否已连接 || webSocket == null)
        {
            return;
        }

        try
        {
            string message = $"{{\"type\":\"leaveGroup\",\"clanId\":{clanId}}}";
            await 发送消息(message);
            if (当前家族ID == clanId)
            {
                当前家族ID = -1;
            }
            Debug.Log($"已发送离开家族组消息: clan_{clanId}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"离开家族组失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 消息接收循环（在后台线程运行）
    /// </summary>
    private async Task 接收消息循环(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        
        try
        {
            while (webSocket != null && webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Debug.Log("WebSocket 服务器关闭连接");
                    break;
                }
                
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    
                    // 将消息加入队列，等待在主线程处理
                    lock (消息队列锁)
                    {
                        消息队列.Enqueue(message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Debug.Log("WebSocket 接收消息循环已取消");
        }
        catch (Exception ex)
        {
            Debug.LogError($"WebSocket 接收消息错误: {ex.Message}");
        }
        finally
        {
            是否已连接 = false;
        }
    }

    /// <summary>
    /// 在主线程处理消息（由 Update 调用）
    /// </summary>
    private void Update()
    {
        // 在主线程处理消息队列
        lock (消息队列锁)
        {
            while (消息队列.Count > 0)
            {
                string message = 消息队列.Dequeue();
                StartCoroutine(在主线程处理消息(message));
            }
        }
    }

    /// <summary>
    /// 在主线程处理单条消息
    /// </summary>
    private IEnumerator 在主线程处理消息(string message)
    {
        bool 处理成功 = false;
        
        try
        {
            // 先解析 eventType 字段
            var eventMessage = JsonUtility.FromJson<GameEventMessage>(message);
            if (eventMessage == null || string.IsNullOrEmpty(eventMessage.eventType))
            {
                Debug.LogWarning($"无法解析事件类型，消息内容: {message}");
                yield break;
            }

            // 根据事件类型反序列化为对应的事件对象
            switch (eventMessage.eventType)
            {
                case "ClanMemberKicked":
                    var kickEvent = JsonUtility.FromJson<ClanMemberKickedEvent>(message);
                    处理成员被踢出事件(kickEvent);
                    处理成功 = true;
                    break;
                case "ClanRoleAppointed":
                    var appointEvent = JsonUtility.FromJson<ClanRoleAppointedEvent>(message);
                    处理职位任命事件(appointEvent);
                    处理成功 = true;
                    break;
                case "ClanDisbanded":
                    var disbandEvent = JsonUtility.FromJson<ClanDisbandedEvent>(message);
                    处理家族解散事件(disbandEvent);
                    处理成功 = true;
                    break;
                case "ClanMemberJoined":
                    var joinEvent = JsonUtility.FromJson<ClanMemberJoinedEvent>(message);
                    处理成员加入事件(joinEvent);
                    处理成功 = true;
                    break;
                case "ClanMemberLeft":
                    var leaveEvent = JsonUtility.FromJson<ClanMemberLeftEvent>(message);
                    处理成员离开事件(leaveEvent);
                    处理成功 = true;
                    break;
                case "ClanDonated":
                    var donateEvent = JsonUtility.FromJson<ClanDonatedEvent>(message);
                    处理家族捐献事件(donateEvent);
                    处理成功 = true;
                    break;
                case "ChatMessage":
                    var chatEvent = JsonUtility.FromJson<ChatMessageEvent>(message);
                    处理聊天消息事件(chatEvent);
                    处理成功 = true;
                    break;
                case "SystemMessage":
                    var systemEvent = JsonUtility.FromJson<SystemMessageEvent>(message);
                    处理系统消息事件(systemEvent);
                    处理成功 = true;
                    break;
                default:
                    Debug.LogWarning($"未知的游戏事件类型: {eventMessage.eventType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"处理 WebSocket 消息失败: {ex.Message}, 消息内容: {message}");
        }
        
        yield return null;
    }

    /// <summary>
    /// 发送消息到服务器
    /// </summary>
    private async Task 发送消息(string message)
    {
        if (webSocket == null || webSocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("WebSocket 未连接，无法发送消息");
            return;
        }

        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.LogError($"发送 WebSocket 消息失败: {ex.Message}");
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

            // 刷新UI - 使用家族显示判断来显示无家族界面
            家族显示判断 家族显示判断组件 = FindObjectOfType<家族显示判断>();
            if (家族显示判断组件 != null)
            {
                家族显示判断组件.刷新显示();
            }
            else
            {
                Debug.LogWarning("未找到 家族显示判断 组件，无法刷新无家族界面");
            }
        }
        else
        {
            // 被踢出的是其他玩家，刷新家族成员列表
            if (当前玩家.家族 != null && 当前玩家.家族.家族ID == evt.clanId)
            {
                通用提示框.显示($"{evt.kickedPlayerName}已被{evt.operatorName}踢出家族");
                
                // 刷新家族成员列表
                var 玩家列表显示组件 = 获取玩家列表显示();
                if (玩家列表显示组件 != null && 玩家列表显示组件.当前显示类型 == 玩家列表显示.显示类型.家族玩家查看)
                {
                    玩家列表显示组件.StartCoroutine(玩家列表显示组件.获取家族成员列表(evt.clanId));
                }

                // 刷新家族信息显示
                var 家族信息显示组件 = 获取家族信息显示();
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
            var 家族信息显示组件 = 获取家族信息显示();
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
            var 家族信息显示组件2 = 获取家族信息显示();
            if (家族信息显示组件2 != null)
            {
                家族信息显示组件2.刷新显示();
            }
        }

        // 刷新家族成员列表（如果正在查看）
        if (当前玩家.家族 != null && 当前玩家.家族.家族ID == evt.clanId)
        {
            var 玩家列表显示组件 = 获取玩家列表显示();
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

            // 刷新UI - 使用家族显示判断来显示无家族界面
            var 家族显示判断组件 = 获取家族显示判断();
            if (家族显示判断组件 != null)
            {
                家族显示判断组件.刷新显示();
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
            var 玩家列表显示组件 = 获取玩家列表显示();
            if (玩家列表显示组件 != null && 玩家列表显示组件.当前显示类型 == 玩家列表显示.显示类型.家族玩家查看)
            {
                玩家列表显示组件.StartCoroutine(玩家列表显示组件.获取家族成员列表(evt.clanId));
            }

            // 刷新家族信息显示
            var 家族信息显示组件 = 获取家族信息显示();
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
            var 玩家列表显示组件 = 获取玩家列表显示();
            if (玩家列表显示组件 != null && 玩家列表显示组件.当前显示类型 == 玩家列表显示.显示类型.家族玩家查看)
            {
                玩家列表显示组件.StartCoroutine(玩家列表显示组件.获取家族成员列表(evt.clanId));
            }

            // 刷新家族信息显示
            var 家族信息显示组件 = 获取家族信息显示();
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
            var 家族信息显示组件 = 获取家族信息显示();
            if (家族信息显示组件 != null)
            {
                家族信息显示组件.刷新显示();
            }
        }
    }

    /// <summary>
    /// 处理聊天消息事件
    /// </summary>
    private void 处理聊天消息事件(ChatMessageEvent evt)
    {
        if (evt == null || string.IsNullOrEmpty(evt.channel)) return;

        // 将消息添加到聊天系统
        if (聊天系统管理.实例 != null)
        {
            聊天系统管理.实例.添加消息(evt.channel, evt.playerName, evt.message, false);
        }
    }

    /// <summary>
    /// 处理系统消息事件
    /// </summary>
    private void 处理系统消息事件(SystemMessageEvent evt)
    {
        if (evt == null || string.IsNullOrEmpty(evt.message)) return;

        // 显示系统消息提示框
        通用提示框.显示(evt.message);

        // 将消息添加到聊天系统的系统频道
        if (聊天系统管理.实例 != null)
        {
            聊天系统管理.实例.添加消息("system", "系统", evt.message, true);
        }

        // 如果消息是禁言通知，设置禁言状态
        if (evt.message.Contains("禁言"))
        {
            if (聊天系统管理.实例 != null)
            {
                聊天系统管理.实例.设置禁言状态(true);
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

[System.Serializable]
public class ChatMessageEvent : GameEventMessage
{
    public string channel; // "world", "country", "clan"
    public int playerId;
    public string playerName;
    public string message;
    public string messageTime;
}

[System.Serializable]
public class SystemMessageEvent : GameEventMessage
{
    public string message;
    public string messageTime;
}

