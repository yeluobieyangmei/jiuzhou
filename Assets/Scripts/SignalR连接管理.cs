using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Net.WebSockets;
using System.Text;
using 玩家数据结构;
using 国家系统;

/// <summary>
/// WebSocket 连接管理器
/// 用于管理与服务器的实时通信连接（使用自建的 WebSocket 端点）
/// </summary>
public class SignalR连接管理 : MonoBehaviour
{
    public static SignalR连接管理 实例 { get; private set; }

    // WebSocket 服务器地址（对应服务端自建的 /ws 端点）
    // 注意：如果是本地测试，请使用 "ws://localhost:5000/ws" 或 "ws://127.0.0.1:5000/ws"
    // 如果是远程服务器，请使用 "ws://服务器IP:端口/ws"
    private string hubUrl = "ws://43.139.181.191:5000/ws";
    
    // 本地测试地址（如果需要，可以取消注释使用）
    // private string hubUrl = "ws://localhost:5000/ws";

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
    private bool 是否正在重连 = false;

    // 当前玩家所属的家族ID（用于加入/离开家族组）
    private int 当前家族ID = -1;

    // 心跳机制
    private float 心跳间隔 = 20f; // 20秒发送一次心跳
    private float 上次心跳时间 = 0f;
    private Coroutine 心跳协程 = null;
    private Coroutine 重连协程 = null;

    // 重连机制
    private float 重连间隔 = 5f; // 5秒后重连
    private int 最大重连次数 = 10; // 最多重连10次
    private int 当前重连次数 = 0;

    // 应用生命周期状态
    private bool 应用在前台 = true;

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
        
        // 启动时自动建立连接
        建立连接();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        // 应用进入后台或恢复前台
        应用在前台 = !pauseStatus;
        if (pauseStatus)
        {
            Debug.Log("[WebSocket] 应用进入后台");
        }
        else
        {
            Debug.Log("[WebSocket] 应用恢复前台");
            // 恢复前台时检查连接状态
            if (!是否已连接 && !是否正在连接 && !是否正在重连)
            {
                建立连接();
            }
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        // 应用失去焦点或获得焦点
        应用在前台 = hasFocus;
        if (!hasFocus)
        {
            Debug.Log("[WebSocket] 应用失去焦点");
        }
        else
        {
            Debug.Log("[WebSocket] 应用获得焦点");
            // 获得焦点时检查连接状态
            if (!是否已连接 && !是否正在连接 && !是否正在重连)
            {
                建立连接();
            }
        }
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
            Debug.Log($"[WebSocket] 准备连接到服务器: {hubUrl}");
            
            // 创建 WebSocket 客户端
            webSocket = new ClientWebSocket();
            cancellationTokenSource = new CancellationTokenSource();

            // 连接到服务器（设置超时时间）
            Uri serverUri = new Uri(hubUrl);
            Debug.Log($"[WebSocket] 正在连接... URI: {serverUri}");
            
            // 使用超时任务
            var connectTask = webSocket.ConnectAsync(serverUri, cancellationTokenSource.Token);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), cancellationTokenSource.Token);
            
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("WebSocket连接超时（10秒）");
            }
            
            await connectTask; // 确保连接完成
            
            if (webSocket.State == WebSocketState.Open)
            {
                是否已连接 = true;
                是否正在连接 = false;
                是否正在重连 = false;
                当前重连次数 = 0; // 重置重连次数
                Debug.Log($"[WebSocket] ✓ 连接成功！状态: {webSocket.State}");

                // 启动消息接收循环（在后台线程）
                receiveTask = Task.Run(() => 接收消息循环(cancellationTokenSource.Token));

                // 延迟注册玩家ID到服务器（确保玩家数据已加载）
                StartCoroutine(延迟注册玩家ID());

                // 启动心跳协程
                启动心跳();

                // 重连成功后同步数据
                StartCoroutine(重连后同步数据());
            }
            else
            {
                throw new Exception($"WebSocket连接后状态异常: {webSocket.State}");
            }
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            Debug.LogError($"[WebSocket] 连接失败（网络错误）: {ex.Message}");
            Debug.LogError($"[WebSocket] 错误代码: {ex.SocketErrorCode}");
            Debug.LogError($"[WebSocket] 请检查：1. 服务端是否运行 2. 地址是否正确: {hubUrl} 3. 防火墙是否阻止");
            是否正在连接 = false;
            webSocket?.Dispose();
            webSocket = null;
            
            // 连接失败后尝试重连
            if (!是否正在重连 && 应用在前台)
            {
                尝试重连();
            }
        }
        catch (TimeoutException ex)
        {
            Debug.LogError($"[WebSocket] 连接超时: {ex.Message}");
            Debug.LogError($"[WebSocket] 请检查：1. 服务端是否运行 2. 网络是否正常 3. 地址是否正确: {hubUrl}");
            是否正在连接 = false;
            webSocket?.Dispose();
            webSocket = null;
            
            // 连接失败后尝试重连
            if (!是否正在重连 && 应用在前台)
            {
                尝试重连();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WebSocket] 连接失败: {ex.GetType().Name}: {ex.Message}");
            Debug.LogError($"[WebSocket] 堆栈跟踪: {ex.StackTrace}");
            Debug.LogError($"[WebSocket] 连接地址: {hubUrl}");
            是否正在连接 = false;
            webSocket?.Dispose();
            webSocket = null;
            
            // 连接失败后尝试重连
            if (!是否正在重连 && 应用在前台)
            {
                尝试重连();
            }
        }
    }

    /// <summary>
    /// 延迟注册玩家ID到服务器（用于定向推送消息）
    /// </summary>
    private IEnumerator 延迟注册玩家ID()
    {
        Debug.Log("[WebSocket] 开始延迟注册玩家ID协程");
        
        // 等待1秒，确保玩家数据已加载完成
        yield return new WaitForSeconds(1f);

        if (!是否已连接 || webSocket == null)
        {
            Debug.LogWarning("[WebSocket] 连接未建立，无法注册玩家ID");
            yield break;
        }

        var 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null || 当前玩家.ID <= 0)
        {
            Debug.LogWarning($"[WebSocket] 玩家数据未加载，无法注册玩家ID。当前玩家: {当前玩家?.ID ?? -1}");
            // 再等待2秒，可能玩家数据还在加载
            yield return new WaitForSeconds(2f);
            当前玩家 = 玩家数据管理.实例?.当前玩家数据;
            if (当前玩家 == null || 当前玩家.ID <= 0)
            {
                Debug.LogError("[WebSocket] 等待后玩家数据仍未加载，无法注册玩家ID");
                yield break;
            }
        }

        // 直接调用异步方法（fire-and-forget，不等待完成）
        string message = $"{{\"type\":\"registerPlayerId\",\"playerId\":{当前玩家.ID}}}";
        Debug.Log($"[WebSocket] 准备发送注册消息: {message}");
        _ = 发送消息(message); // 使用 discard 操作符，忽略返回的 Task
        Debug.Log($"[WebSocket] 正在注册玩家ID: {当前玩家.ID}");
        
        // 延迟再次注册，确保服务端收到（有些情况下第一次可能丢失）
        yield return new WaitForSeconds(2f);
        if (是否已连接 && webSocket != null && webSocket.State == WebSocketState.Open)
        {
            _ = 发送消息(message);
            Debug.Log($"[WebSocket] 二次注册玩家ID: {当前玩家.ID}");
        }
        else
        {
            Debug.LogWarning($"[WebSocket] 二次注册失败：连接状态 - 已连接={是否已连接}, WebSocket状态={webSocket?.State}");
        }
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
            
            // 停止心跳
            停止心跳();
            
            Debug.Log("WebSocket 连接已断开");
            
            // 断开后尝试重连（如果应用在前台）
            if (应用在前台 && !是否正在重连)
            {
                尝试重连();
            }
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
                    
                    // 如果是战场倒计时事件，立即记录日志（用于调试）
                    if (message.Contains("BattlefieldCountdown"))
                    {
                        Debug.Log($"[WebSocket] 收到原始倒计时消息（接收循环）: {message}");
                    }
                    
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
            Debug.Log("[WebSocket] 接收消息循环已取消（正常关闭）");
        }
        catch (System.Net.WebSockets.WebSocketException ex)
        {
            Debug.LogError($"[WebSocket] 接收消息错误（WebSocket异常）: {ex.Message}");
            Debug.LogError($"[WebSocket] WebSocket状态: {ex.WebSocketErrorCode}");
            if (ex.InnerException != null)
            {
                Debug.LogError($"[WebSocket] 内部异常: {ex.InnerException.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WebSocket] 接收消息错误: {ex.GetType().Name}: {ex.Message}");
            Debug.LogError($"[WebSocket] 堆栈跟踪: {ex.StackTrace}");
        }
        finally
        {
            是否已连接 = false;
            
            // 接收循环结束时，触发重连（如果应用在前台）
            // 注意：这里在后台线程，需要通过协程在主线程执行
            if (应用在前台 && !是否正在重连)
            {
                // 使用协程在主线程执行重连
                StartCoroutine(延迟重连());
            }
        }
    }

    /// <summary>
    /// 延迟重连协程（用于在后台线程触发重连）
    /// </summary>
    private System.Collections.IEnumerator 延迟重连()
    {
        yield return null; // 等待一帧，确保在主线程
        尝试重连();
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
            // 如果是战场倒计时事件，先记录原始消息（用于调试）
            if (message.Contains("BattlefieldCountdown"))
            {
                Debug.Log($"[WebSocket] 在主线程处理倒计时消息: {message}");
            }
            
            // 先解析 eventType 字段
            var eventMessage = JsonUtility.FromJson<GameEventMessage>(message);
            if (eventMessage == null || string.IsNullOrEmpty(eventMessage.eventType))
            {
                Debug.LogWarning($"[WebSocket] 无法解析事件类型，消息内容: {message}");
                yield break;
            }
            
            Debug.Log($"[WebSocket] 解析到事件类型: {eventMessage.eventType}");

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
                case "BattlefieldCountdown":
                    var countdownEvent = JsonUtility.FromJson<BattlefieldCountdownEvent>(message);
                    Debug.Log($"[WebSocket] ========== 收到原始倒计时消息 ==========");
                    Debug.Log($"[WebSocket] 原始消息内容: {message}");
                    Debug.Log($"[WebSocket] 解析后 - 国家ID: {countdownEvent?.countryId}, 剩余秒数: {countdownEvent?.remainingSeconds}");
                    处理战场倒计时事件(countdownEvent);
                    处理成功 = true;
                    break;
                case "BattlefieldCountdownEnd":
                    var endEvent = JsonUtility.FromJson<BattlefieldCountdownEndEvent>(message);
                    Debug.Log($"[WebSocket] ========== 收到倒计时结束消息 ==========");
                    Debug.Log($"[WebSocket] 原始消息内容: {message}");
                    处理战场倒计时结束事件(endEvent);
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
        if (!是否已连接 || webSocket == null || webSocket.State != WebSocketState.Open)
        {
            Debug.LogWarning($"[WebSocket] 连接未建立，无法发送消息: {message}");
            return;
        }

        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
            Debug.Log($"[WebSocket] 消息已发送: {message}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WebSocket] 发送消息失败: {ex.Message}，消息内容: {message}");
            // 发送失败，可能连接已断开
            是否已连接 = false;
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

        // 检查是否是当前玩家自己发送的消息，如果是则忽略（因为已经在发送成功后立即在本地显示了）
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 != null && evt.playerId == 当前玩家.ID)
        {
            return; // 忽略自己发送的消息，避免重复显示
        }

        // 将消息添加到聊天界面
        if (聊天界面.实例 != null)
        {
            // 转换频道名称
            聊天频道 频道 = 聊天频道.世界;
            switch (evt.channel.ToLower())
            {
                case "world":
                    频道 = 聊天频道.世界;
                    break;
                case "country":
                    频道 = 聊天频道.国家;
                    break;
                case "clan":
                    频道 = 聊天频道.家族;
                    break;
            }

            // 格式化消息：HH:mm 玩家名 消息内容
            string 时间 = DateTime.Now.ToString("HH:mm");
            if (!string.IsNullOrEmpty(evt.messageTime))
            {
                try
                {
                    if (DateTime.TryParse(evt.messageTime, out DateTime 解析时间))
                    {
                        时间 = 解析时间.ToString("HH:mm");
                    }
                }
                catch
                {
                    // 解析失败，使用当前时间
                }
            }
            string 完整文本 = $"{时间} {evt.playerName} {evt.message}";
            
            聊天界面.实例.接收新消息(频道, 完整文本);
        }
    }


    /// <summary>
    /// 处理战场倒计时事件（从服务端WebSocket推送）
    /// 客户端只接收信息并显示，不做任何本地逻辑处理
    /// </summary>
    private void 处理战场倒计时事件(BattlefieldCountdownEvent evt)
    {
        if (evt == null || evt.countryId <= 0)
        {
            Debug.LogWarning($"[战场倒计时] 事件无效: evt={evt?.countryId ?? -1}");
            return;
        }

        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null)
        {
            Debug.LogWarning("[战场倒计时] 当前玩家数据为空");
            return;
        }
        
        if (当前玩家.国家 == null)
        {
            Debug.LogWarning("[战场倒计时] 当前玩家没有国家信息");
            return;
        }
        
        if (当前玩家.国家.国家ID != evt.countryId)
        {
            Debug.Log($"[战场倒计时] 国家ID不匹配: 当前国家ID={当前玩家.国家.国家ID}, 事件国家ID={evt.countryId}，忽略");
            return; // 不是当前玩家的国家，忽略
        }
        
        // 检查当前玩家是否属于宣战家族（使用服务端发送的家族ID）
        bool 属于宣战家族 = false;
        if (当前玩家.家族 != null)
        {
            属于宣战家族 = (当前玩家.家族.家族ID == evt.clan1Id) || (当前玩家.家族.家族ID == evt.clan2Id);
        }
        
        if (!属于宣战家族)
        {
            Debug.Log($"[战场倒计时] 玩家不属于宣战家族，忽略。玩家家族ID={当前玩家.家族?.家族ID ?? -1}, 宣战家族1={evt.clan1Id}, 宣战家族2={evt.clan2Id}");
            return;
        }
        
        // 在控制台每秒输出倒计时进度（重要：用于查看通信是否正常）
        Debug.Log($"[战场倒计时] ========== 收到服务端倒计时广播 ==========");
        Debug.Log($"[战场倒计时] 国家ID: {evt.countryId}, 剩余秒数: {evt.remainingSeconds}秒");
        Debug.Log($"[战场倒计时] 家族1: {evt.clan1Name}({evt.clan1Id}) VS 家族2: {evt.clan2Name}({evt.clan2Id})");
        Debug.Log($"[战场倒计时] 当前玩家ID: {当前玩家.ID}, 玩家家族ID: {当前玩家.家族.家族ID}");
        Debug.Log($"[战场倒计时] ==========================================");

        // 确保本地国家信息中的宣战家族信息已更新（使用服务端发送的数据）
        if (当前玩家.国家.宣战家族1 == null || 当前玩家.国家.宣战家族1.家族ID != evt.clan1Id)
        {
            if (当前玩家.国家.宣战家族1 == null)
            {
                当前玩家.国家.宣战家族1 = new 家族信息库();
            }
            当前玩家.国家.宣战家族1.家族ID = evt.clan1Id;
            当前玩家.国家.宣战家族1.家族名字 = evt.clan1Name;
        }
        
        if (当前玩家.国家.宣战家族2 == null || 当前玩家.国家.宣战家族2.家族ID != evt.clan2Id)
        {
            if (当前玩家.国家.宣战家族2 == null)
            {
                当前玩家.国家.宣战家族2 = new 家族信息库();
            }
            当前玩家.国家.宣战家族2.家族ID = evt.clan2Id;
            当前玩家.国家.宣战家族2.家族名字 = evt.clan2Name;
        }

        // 客户端只接收信息并显示，不做任何本地倒计时逻辑处理
        // 这里可以调用UI显示方法，例如显示倒计时UI
        Debug.Log($"[战场倒计时] ✓ 倒计时已更新: 剩余 {evt.remainingSeconds} 秒");
        
        // TODO: 如果需要显示倒计时UI，可以在这里调用UI显示方法
        // 例如：战场入口管理器.实例?.更新倒计时显示(evt.remainingSeconds);
    }

    /// <summary>
    /// 处理战场倒计时结束事件（从服务端WebSocket推送）
    /// </summary>
    private void 处理战场倒计时结束事件(BattlefieldCountdownEndEvent evt)
    {
        if (evt == null || evt.countryId <= 0)
        {
            Debug.LogWarning($"[战场倒计时] 结束事件无效: evt={evt?.countryId ?? -1}");
            return;
        }

        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null)
        {
            Debug.LogWarning("[战场倒计时] 当前玩家数据为空");
            return;
        }
        
        if (当前玩家.国家 == null)
        {
            Debug.LogWarning("[战场倒计时] 当前玩家没有国家信息");
            return;
        }
        
        if (当前玩家.国家.国家ID != evt.countryId)
        {
            Debug.Log($"[战场倒计时] 国家ID不匹配: 当前国家ID={当前玩家.国家.国家ID}, 事件国家ID={evt.countryId}，忽略");
            return;
        }
        
        // 检查当前玩家是否属于宣战家族
        bool 属于宣战家族 = false;
        if (当前玩家.家族 != null)
        {
            属于宣战家族 = (当前玩家.家族.家族ID == evt.clan1Id) || (当前玩家.家族.家族ID == evt.clan2Id);
        }
        
        if (!属于宣战家族)
        {
            Debug.Log($"[战场倒计时] 玩家不属于宣战家族，忽略。玩家家族ID={当前玩家.家族?.家族ID ?? -1}, 宣战家族1={evt.clan1Id}, 宣战家族2={evt.clan2Id}");
            return;
        }
        
        Debug.Log($"[战场倒计时] ========== 倒计时结束 ==========");
        Debug.Log($"[战场倒计时] 国家ID: {evt.countryId}");
        Debug.Log($"[战场倒计时] 家族1: {evt.clan1Name}({evt.clan1Id}) VS 家族2: {evt.clan2Name}({evt.clan2Id})");
        Debug.Log($"[战场倒计时] 当前玩家ID: {当前玩家.ID}, 玩家家族ID: {当前玩家.家族.家族ID}");
        Debug.Log($"[战场倒计时] ==========================================");

        // 显示战场入口窗口
        if (战场入口管理器.实例 != null && 战场入口管理器.实例.窗口对象 != null)
        {
            战场入口管理器.实例.窗口对象.SetActive(true);
            Debug.Log($"[战场倒计时] ✓ 已显示战场入口窗口");
        }
        else
        {
            Debug.LogWarning("[战场倒计时] 战场入口管理器实例或窗口对象为空，无法显示窗口");
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

        // 将消息添加到聊天界面的系统频道
        if (聊天界面.实例 != null)
        {
            // 格式化消息：HH:mm 系统 消息内容
            string 时间 = DateTime.Now.ToString("HH:mm");
            if (!string.IsNullOrEmpty(evt.messageTime))
            {
                try
                {
                    if (DateTime.TryParse(evt.messageTime, out DateTime 解析时间))
                    {
                        时间 = 解析时间.ToString("HH:mm");
                    }
                }
                catch
                {
                    // 解析失败，使用当前时间
                }
            }
            string 完整文本 = $"{时间} 系统 {evt.message}";
            
            聊天界面.实例.接收新消息(聊天频道.系统, 完整文本);

            // 如果消息是禁言通知，设置禁言状态
            if (evt.message.Contains("禁言"))
            {
                聊天界面.实例.设置禁言状态(true);
            }
        }
    }

    /// <summary>
    /// 启动心跳协程
    /// </summary>
    private void 启动心跳()
    {
        停止心跳(); // 先停止旧的协程
        上次心跳时间 = Time.time;
        心跳协程 = StartCoroutine(心跳循环());
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
    }

    /// <summary>
    /// 心跳循环协程
    /// </summary>
    private System.Collections.IEnumerator 心跳循环()
    {
        while (是否已连接 && webSocket != null && webSocket.State == WebSocketState.Open)
        {
            yield return new WaitForSeconds(心跳间隔);

            if (是否已连接 && webSocket != null && webSocket.State == WebSocketState.Open)
            {
                try
                {
                    string 心跳消息 = "{\"type\":\"heartbeat\",\"timestamp\":" + DateTimeOffset.Now.ToUnixTimeMilliseconds() + "}";
                    _ = 发送消息(心跳消息);
                    上次心跳时间 = Time.time;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[WebSocket] 发送心跳失败: {ex.Message}");
                    // 心跳失败，可能连接已断开
                    是否已连接 = false;
                }
            }
        }
    }

    /// <summary>
    /// 尝试重连
    /// </summary>
    private void 尝试重连()
    {
        if (是否正在重连 || !应用在前台)
        {
            return;
        }

        if (当前重连次数 >= 最大重连次数)
        {
            Debug.LogWarning($"[WebSocket] 已达到最大重连次数 ({最大重连次数})，停止重连");
            return;
        }

        是否正在重连 = true;
        当前重连次数++;
        Debug.Log($"[WebSocket] 开始第 {当前重连次数} 次重连尝试...");

        if (重连协程 != null)
        {
            StopCoroutine(重连协程);
        }
        重连协程 = StartCoroutine(重连协程方法());
    }

    /// <summary>
    /// 重连协程
    /// </summary>
    private System.Collections.IEnumerator 重连协程方法()
    {
        yield return new WaitForSeconds(重连间隔);

        if (!应用在前台)
        {
            是否正在重连 = false;
            yield break;
        }

        // 清理旧连接
        if (webSocket != null)
        {
            try
            {
                webSocket?.Dispose();
            }
            catch { }
            webSocket = null;
        }

        // 重新建立连接
        建立连接();
        
        // 等待连接结果
        yield return new WaitForSeconds(2f);

        if (!是否已连接)
        {
            // 连接失败，继续重连
            是否正在重连 = false;
            if (当前重连次数 < 最大重连次数)
            {
                尝试重连();
            }
        }
        else
        {
            // 连接成功
            是否正在重连 = false;
        }
    }

    /// <summary>
    /// 重连后同步数据
    /// </summary>
    private System.Collections.IEnumerator 重连后同步数据()
    {
        // 等待玩家数据加载完成
        yield return new WaitForSeconds(1f);

        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId <= 0)
        {
            yield break;
        }

        Debug.Log("[WebSocket] 重连后开始同步数据...");

        // 检查是否已登录（只有在已登录状态下才同步数据，避免在登录界面时自动弹出UI）
        // 如果当前玩家数据不存在，说明还在登录界面，不应该同步数据
        if (玩家数据管理.实例 == null || 玩家数据管理.实例.当前玩家数据 == null)
        {
            Debug.Log("[WebSocket] 未登录状态（玩家数据不存在），跳过数据同步");
            yield break;
        }

        // 1. 重新获取玩家数据（静默获取，不自动显示UI）
        if (玩家数据管理.实例 != null)
        {
            玩家数据管理.实例.获取玩家数据(accountId, false); // 传入false，不自动显示UI
            yield return new WaitForSeconds(0.5f);
        }

        // 2. 如果玩家有家族，重新加入家族组
        var 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 != null && 当前玩家.家族 != null && 当前玩家.家族.家族ID > 0)
        {
            加入家族组(当前玩家.家族.家族ID);
            yield return new WaitForSeconds(0.5f);
        }

        // 3. 刷新UI显示（只在主场景中刷新，避免在登录界面时触发）
        // 注意：这里不强制刷新，让UI自然更新即可，避免在登录界面时弹出UI
        // 如果玩家在主场景，UI会自动刷新；如果在登录界面，不应该刷新
        Debug.Log("[WebSocket] 数据同步完成");
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

[System.Serializable]
public class BattlefieldCountdownEvent : GameEventMessage
{
    public int countryId;
    public int clan1Id;
    public string clan1Name;
    public int clan2Id;
    public string clan2Name;
    public int remainingSeconds;
}

[System.Serializable]
public class BattlefieldCountdownEndEvent : GameEventMessage
{
    public int countryId;
    public int clan1Id;
    public string clan1Name;
    public int clan2Id;
    public string clan2Name;
}

