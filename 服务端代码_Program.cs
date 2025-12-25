using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Net.WebSockets;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// 配置日志：只保留启动信息到控制台，其他日志写入文件
builder.Logging.ClearProviders();
builder.Logging.AddConsole(); // 保留控制台日志提供程序
builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information); // 设置为 Information 级别，只显示启动信息
// 过滤掉详细日志，只保留 HostingLifetime 的启动信息
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", Microsoft.Extensions.Logging.LogLevel.Information);
builder.Logging.AddFilter("Microsoft", Microsoft.Extensions.Logging.LogLevel.Warning); // Microsoft 命名空间的日志只显示 Warning 及以上
builder.Logging.AddFilter("System", Microsoft.Extensions.Logging.LogLevel.Warning); // System 命名空间的日志只显示 Warning 及以上
builder.Logging.AddFilter("", Microsoft.Extensions.Logging.LogLevel.Warning); // 其他所有日志只显示 Warning 及以上

// 添加控制器支持，配置 JSON 选项（支持 camelCase 和 UTF-8 编码）
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping; // 支持中文字符
        options.JsonSerializerOptions.ReadCommentHandling = JsonCommentHandling.Skip; // 允许注释
        options.JsonSerializerOptions.AllowTrailingCommas = true; // 允许尾随逗号
    });

// 注意：Minimal API 的 JSON 选项会使用 AddControllers 中配置的 JsonSerializerOptions

// 添加 SignalR 服务
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true; // 开发阶段启用详细错误信息
});

// MySQL 数据库连接字符串（密码已填入）
string 数据库连接字符串 = "Server=localhost;Database=jiuzhou;User=root;Password=!Cao1054675525;Charset=utf8mb4;";

// 登录失败次数限制配置
const int 最大失败次数 = 5; // 连续失败5次后锁定
const int 锁定时长小时 = 1; // 锁定1小时

// 在线账号集合（用于防止重复登录）
// 使用线程安全的 ConcurrentDictionary
// Key: 账号ID, Value: 最后心跳时间（DateTime）
// 注意：服务器重启后此集合会清空，这是合理的
var 在线账号集合 = new System.Collections.Concurrent.ConcurrentDictionary<int, DateTime>();

// 玩家ID到WebSocket连接的映射（用于定向推送消息）
// Key: 玩家ID, Value: WebSocket连接
var 玩家连接映射 = new System.Collections.Concurrent.ConcurrentDictionary<int, WebSocket>();

// 玩家最后发言时间记录（用于频率限制：5秒一次）
// Key: 玩家ID, Value: 最后发言时间（DateTime）
var 玩家最后发言时间 = new System.Collections.Concurrent.ConcurrentDictionary<int, DateTime>();

// 消息队列（优先级队列：系统(0) > 家族(1) > 国家(2) > 世界(3)）
var 消息队列 = new System.Collections.Concurrent.ConcurrentQueue<待处理消息>();

// 战场倒计时任务字典（Key: 国家ID, Value: 取消令牌）
var 战场倒计时任务 = new System.Collections.Concurrent.ConcurrentDictionary<int, CancellationTokenSource>();

// 心跳超时时间（秒）- 如果超过这个时间没有心跳，认为账号已离线
const int 心跳超时秒数 = 120; // 2分钟无心跳则认为离线

// 启动后台任务，定期清理超时的在线账号
var 清理超时账号任务 = Task.Run(async () =>
{
    while (true)
    {
        try
        {
            await Task.Delay(60000); // 每60秒检查一次
            
            var 当前时间 = DateTime.Now;
            var 需要移除的账号 = new List<int>();
            
            foreach (var kvp in 在线账号集合)
            {
                var 时间差 = (当前时间 - kvp.Value).TotalSeconds;
                if (时间差 > 心跳超时秒数)
                {
                    需要移除的账号.Add(kvp.Key);
                }
            }
            
            foreach (var 账号ID in 需要移除的账号)
            {
                在线账号集合.TryRemove(账号ID, out _);
            }
            
            // 静默清理超时账号，不输出日志（避免控制台刷屏）
            
            // 清理超时的WebSocket连接
            await WebSocketConnectionManager.CheckAndCleanTimeoutConnections(玩家连接映射, 在线账号集合, 数据库连接字符串);
        }
        catch (Exception ex)
        {
            日志记录器.错误($"[清理超时账号任务] 错误: {ex.Message}");
        }
    }
});

// 启动消息队列处理后台任务
var 消息处理任务 = Task.Run(async () =>
{
    while (true)
    {
        try
        {
            // 按优先级处理消息（先处理系统消息，再处理家族、国家、世界消息）
            var 待处理消息列表 = new List<待处理消息>();
            
            // 从队列中取出所有消息
            while (消息队列.TryDequeue(out var 消息))
            {
                待处理消息列表.Add(消息);
            }
            
            // 按优先级排序：系统(0) > 家族(1) > 国家(2) > 世界(3)
            if (待处理消息列表.Count > 0)
            {
                待处理消息列表.Sort((a, b) => a.优先级.CompareTo(b.优先级));
                
                foreach (var 消息 in 待处理消息列表)
                {
                    try
                    {
                        await 处理消息(消息, 数据库连接字符串, 玩家连接映射);
                    }
                    catch (Exception ex)
                    {
                        日志记录器.错误($"[消息处理任务] 处理消息失败: {ex.Message}");
                    }
                }
            }
            
            await Task.Delay(100); // 每100ms处理一次
        }
        catch (Exception ex)
        {
            日志记录器.错误($"[消息处理任务] 错误: {ex.Message}");
            await Task.Delay(1000); // 出错时等待1秒再继续
        }
    }
});

var app = builder.Build();

// 添加全局异常处理中间件（捕获JSON解析错误等）
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Microsoft.AspNetCore.Http.BadHttpRequestException badRequestEx) when (badRequestEx.Message.Contains("JSON"))
    {
        // JSON解析错误，返回友好的错误消息
        日志记录器.错误($"[全局异常处理] JSON解析错误: {badRequestEx.Message}");
        context.Response.StatusCode = 200; // 返回200，但success=false
        context.Response.ContentType = "application/json";
        var errorResponse = new LoginResponse(false, "请求格式错误：请检查JSON格式是否正确", "", -1);
        await context.Response.WriteAsJsonAsync(errorResponse);
    }
    catch (System.Text.Json.JsonException jsonEx)
    {
        // JSON解析错误
        日志记录器.错误($"[全局异常处理] JSON解析错误: {jsonEx.Message}");
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        var errorResponse = new LoginResponse(false, "请求格式错误：JSON格式不正确", "", -1);
        await context.Response.WriteAsJsonAsync(errorResponse);
    }
});

// 启用 WebSocket 支持（用于自建简单 WebSocket 端点，向 Unity 客户端推送事件）
app.UseWebSockets();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// =================== 简单 WebSocket 端点：/ws ===================
// 用途：维护所有在线 Unity WebSocket 连接，并向其广播家族相关事件（JSON 格式）

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    // 从查询参数获取玩家ID（可选，如果客户端在连接时传递）
    int? 玩家ID = null;
    if (context.Request.Query.TryGetValue("playerId", out var playerIdStr) && int.TryParse(playerIdStr, out var parsedPlayerId))
    {
        玩家ID = parsedPlayerId;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    日志记录器.信息($"[WebSocket] 客户端已连接{(玩家ID.HasValue ? $"，玩家ID: {玩家ID.Value}" : "")}");

    // 将连接加入全局管理器
    WebSocketConnectionManager.AddConnection(webSocket);

    // 如果提供了玩家ID，记录到连接映射
    if (玩家ID.HasValue)
    {
        玩家连接映射.AddOrUpdate(玩家ID.Value, webSocket, (key, oldValue) => webSocket);
    }

    var buffer = new byte[1024 * 4];
    bool 已注册玩家ID = 玩家ID.HasValue;

    try
    {
        while (webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                日志记录器.信息("[WebSocket] 客户端请求关闭连接");
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                break;
            }
            else if (result.MessageType == WebSocketMessageType.Text)
            {
                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                
                // 处理不同类型的消息
                try
                {
                    var jsonDoc = JsonDocument.Parse(msg);
                    if (jsonDoc.RootElement.TryGetProperty("type", out var typeProp))
                    {
                        string? 消息类型 = typeProp.GetString();
                        
                        // 处理心跳消息
                        if (消息类型 == "heartbeat")
                        {
                            WebSocketConnectionManager.UpdateHeartbeat(webSocket);
                            // 心跳消息不记录日志，避免日志过多
                            continue;
                        }
                        
                        // 处理玩家ID注册消息
                        if (!已注册玩家ID && 消息类型 == "registerPlayerId" &&
                            jsonDoc.RootElement.TryGetProperty("playerId", out var playerIdProp))
                        {
                            int 注册玩家ID = playerIdProp.GetInt32();
                            玩家连接映射.AddOrUpdate(注册玩家ID, webSocket, (key, oldValue) => webSocket);
                            已注册玩家ID = true;
                            WebSocketConnectionManager.UpdateHeartbeat(webSocket);
                            日志记录器.信息($"[WebSocket] 玩家ID已注册: {注册玩家ID}");
                            continue;
                        }
                    }
                }
                catch
                {
                    // 解析失败，忽略
                }
                
                日志记录器.信息($"[WebSocket] 收到客户端消息（忽略处理）：{msg}");
            }
        }
    }
    catch (Exception ex)
    {
        日志记录器.错误($"[WebSocket] 连接异常: {ex.Message}");
    }
    finally
    {
        // 从全局管理器中移除连接
        WebSocketConnectionManager.RemoveConnection(webSocket);
        
        // 从玩家连接映射中移除，并同时从在线账号集合中移除
        if (玩家ID.HasValue)
        {
            玩家连接映射.TryRemove(玩家ID.Value, out _);
            
            // 根据玩家ID查询账号ID，并从在线账号集合中移除
            try
            {
                using var connection = new MySqlConnection(数据库连接字符串);
                connection.Open();
                using var command = new MySqlCommand(
                    "SELECT account_id FROM players WHERE id = @player_id LIMIT 1",
                    connection
                );
                command.Parameters.AddWithValue("@player_id", 玩家ID.Value);
                var accountIdResult = command.ExecuteScalar();
                if (accountIdResult != null && !DBNull.Value.Equals(accountIdResult))
                {
                    int 账号ID = Convert.ToInt32(accountIdResult);
                    在线账号集合.TryRemove(账号ID, out _);
                    日志记录器.信息($"[WebSocket] 账号 {账号ID} (玩家 {玩家ID.Value}) 已从在线集合中移除");
                }
            }
            catch (Exception ex)
            {
                日志记录器.错误($"[WebSocket] 查询账号ID失败: {ex.Message}");
            }
        }
        else
        {
            // 如果没有玩家ID，尝试从映射中查找并移除
            var 要移除的玩家ID列表 = new List<int>();
            foreach (var kvp in 玩家连接映射)
            {
                if (kvp.Value == webSocket)
                {
                    要移除的玩家ID列表.Add(kvp.Key);
                }
            }
            foreach (var pid in 要移除的玩家ID列表)
            {
                玩家连接映射.TryRemove(pid, out _);
                
                // 根据玩家ID查询账号ID，并从在线账号集合中移除
                try
                {
                    using var connection = new MySqlConnection(数据库连接字符串);
                    connection.Open();
                    using var command = new MySqlCommand(
                        "SELECT account_id FROM players WHERE id = @player_id LIMIT 1",
                        connection
                    );
                    command.Parameters.AddWithValue("@player_id", pid);
                    var accountIdResult = command.ExecuteScalar();
                    if (accountIdResult != null && !DBNull.Value.Equals(accountIdResult))
                    {
                        int 账号ID = Convert.ToInt32(accountIdResult);
                        在线账号集合.TryRemove(账号ID, out _);
                        日志记录器.信息($"[WebSocket] 账号 {账号ID} (玩家 {pid}) 已从在线集合中移除");
                    }
                }
                catch (Exception ex)
                {
                    日志记录器.错误($"[WebSocket] 查询账号ID失败: {ex.Message}");
                }
            }
        }
        
        日志记录器.信息("[WebSocket] 客户端已断开并移除");
    }
});

// 映射 SignalR Hub
app.MapHub<GameHub>("/gameHub");

// =================== 登录接口：POST /api/login ===================

app.MapPost("/api/login", async (HttpContext context) =>
{
    try
    {
        // 读取原始请求体
        context.Request.EnableBuffering(); // 允许多次读取请求体
        context.Request.Body.Position = 0;
        using var bodyReader = new System.IO.StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        string rawBody = await bodyReader.ReadToEndAsync();
        context.Request.Body.Position = 0; // 重置位置
        
        日志记录器.信息($"[登录接口] 收到请求体: {rawBody}");
        
        // 手动解析JSON（使用更宽松的配置）
        LoginRequest? 请求 = null;
        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            
            请求 = JsonSerializer.Deserialize<LoginRequest>(rawBody, jsonOptions);
        }
        catch (Exception jsonEx)
        {
            日志记录器.错误($"[登录接口] JSON解析失败: {jsonEx.Message}, 原始请求体: {rawBody}");
            
            // 尝试手动提取username和password（容错处理）
            try
            {
                string? username = null;
                string? password = null;
                
                // 简单的正则表达式提取
                var usernameMatch = System.Text.RegularExpressions.Regex.Match(rawBody, @"""username""\s*:\s*""([^""]*)""");
                var passwordMatch = System.Text.RegularExpressions.Regex.Match(rawBody, @"""password""\s*:\s*""([^""]*)""");
                
                if (usernameMatch.Success) username = usernameMatch.Groups[1].Value;
                if (passwordMatch.Success) password = passwordMatch.Groups[1].Value;
                
                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    请求 = new LoginRequest(username, password);
                    日志记录器.信息($"[登录接口] 使用容错解析成功: username={username}");
                }
                else
                {
                    return Results.Ok(new LoginResponse(false, $"请求格式错误：无法解析JSON，原始内容: {rawBody.Substring(0, Math.Min(100, rawBody.Length))}", "", -1));
                }
            }
            catch
            {
                return Results.Ok(new LoginResponse(false, $"请求格式错误：JSON解析失败", "", -1));
            }
        }
        
        // 检查请求是否为空
        if (请求 == null)
        {
            return Results.Ok(new LoginResponse(false, "请求数据为空", "", -1));
        }
        
        // 清理密码字段中的换行符和回车符（防止客户端发送包含换行符的密码）
        string 清理后的密码 = 请求.Password?.Replace("\r", "").Replace("\n", "").Trim() ?? "";
        // 创建新的请求对象（避免修改原始对象）
        var 清理后的请求 = new LoginRequest(请求.Username, 清理后的密码);
        // 连接 MySQL 数据库
        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 查询账号信息（包括失败次数和锁定时间）
        using var command = new MySqlCommand(
            "SELECT id, password_hash, failed_login_count, locked_until FROM accounts WHERE username = @username",
            connection
        );
        command.Parameters.AddWithValue("@username", 清理后的请求.Username);

        using var reader = await command.ExecuteReaderAsync();
        
        if (await reader.ReadAsync())
        {
            // 账号存在
            int 账号ID = reader.GetInt32(0);
            string 数据库中的密码哈希 = reader.GetString(1);
            int 失败次数 = reader.GetInt32(2);
            DateTime? 锁定到期时间 = reader.IsDBNull(3) ? null : reader.GetDateTime(3);

            // 检查账号是否被锁定
            if (锁定到期时间.HasValue && 锁定到期时间.Value > DateTime.Now)
            {
                // 还在锁定期间
                TimeSpan 剩余时间 = 锁定到期时间.Value - DateTime.Now;
                int 剩余分钟 = (int)剩余时间.TotalMinutes;
                return Results.Ok(new LoginResponse(false, $"账号已锁定，请{剩余分钟}分钟后再试", "", -1));
            }

            // 账号未锁定或已过期，检查密码
            string 输入密码哈希 = 计算SHA256(清理后的请求.Password);

            if (数据库中的密码哈希 == 输入密码哈希)
            {
                // 密码正确，检查账号是否已在线
                if (在线账号集合.ContainsKey(账号ID))
                {
                    // 检查WebSocket连接是否真的活跃
                    bool 连接真的活跃 = false;
                    
                    // 查询该账号对应的玩家ID
                    reader.Close(); // 先关闭reader，才能执行新查询
                    using var playerCommand = new MySqlCommand(
                        "SELECT id FROM players WHERE account_id = @account_id LIMIT 1",
                        connection
                    );
                    playerCommand.Parameters.AddWithValue("@account_id", 账号ID);
                    var playerIdResult = await playerCommand.ExecuteScalarAsync();
                    
                    if (playerIdResult != null && !DBNull.Value.Equals(playerIdResult))
                    {
                        int 玩家ID = Convert.ToInt32(playerIdResult);
                        
                        // 检查玩家连接映射中是否有该玩家，且连接状态是Open
                        if (玩家连接映射.TryGetValue(玩家ID, out var existingSocket))
                        {
                            if (existingSocket != null && existingSocket.State == WebSocketState.Open)
                            {
                                连接真的活跃 = true;
                            }
                            else
                            {
                                // 连接已断开，从映射中移除
                                玩家连接映射.TryRemove(玩家ID, out _);
                                在线账号集合.TryRemove(账号ID, out _);
                                日志记录器.信息($"[登录] 检测到账号 {账号ID} 的连接已断开，允许重新登录");
                            }
                        }
                        else
                        {
                            // 连接映射中不存在，说明连接已断开
                            在线账号集合.TryRemove(账号ID, out _);
                            日志记录器.信息($"[登录] 账号 {账号ID} 的连接映射不存在，允许重新登录");
                        }
                    }
                    
                    if (连接真的活跃)
                    {
                        return Results.Ok(new LoginResponse(false, "当前账号已在线，禁止重复登录！", "", -1));
                    }
                    // 如果连接不活跃，继续执行登录流程
                }

                // 账号未在线，登录成功
                // 重置失败次数和锁定时间
                reader.Close(); // 先关闭 reader，才能执行更新
                using var updateCommand = new MySqlCommand(
                    "UPDATE accounts SET failed_login_count = 0, locked_until = NULL WHERE id = @account_id",
                    connection
                );
                updateCommand.Parameters.AddWithValue("@account_id", 账号ID);
                await updateCommand.ExecuteNonQueryAsync();

                // 将账号添加到在线集合
                在线账号集合.TryAdd(账号ID, DateTime.Now);

                return Results.Ok(new LoginResponse(true, "登录成功", "TOKEN_" + Guid.NewGuid().ToString(), 账号ID));
            }
            else
            {
                // 密码错误，增加失败次数
                reader.Close(); // 先关闭 reader，才能执行更新
                
                int 新失败次数 = 失败次数 + 1;
                DateTime? 新锁定时间 = null;

                // 如果达到最大失败次数，设置锁定时间（当前时间 + 1小时）
                if (新失败次数 >= 最大失败次数)
                {
                    新锁定时间 = DateTime.Now.AddHours(锁定时长小时);
                }

                using var updateCommand = new MySqlCommand(
                    "UPDATE accounts SET failed_login_count = @failed_count, locked_until = @locked_until WHERE id = @account_id",
                    connection
                );
                updateCommand.Parameters.AddWithValue("@account_id", 账号ID);
                updateCommand.Parameters.AddWithValue("@failed_count", 新失败次数);
                
                if (新锁定时间.HasValue)
                {
                    updateCommand.Parameters.AddWithValue("@locked_until", 新锁定时间.Value);
                }
                else
                {
                    updateCommand.Parameters.AddWithValue("@locked_until", DBNull.Value);
                }

                await updateCommand.ExecuteNonQueryAsync();

                if (新失败次数 >= 最大失败次数)
                {
                    return Results.Ok(new LoginResponse(false, $"密码错误，账号已锁定1小时", "", -1));
                }
                else
                {
                    int 剩余次数 = 最大失败次数 - 新失败次数;
                    return Results.Ok(new LoginResponse(false, $"密码错误，还可尝试{剩余次数}次", "", -1));
                }
            }
        }
        else
        {
            // 账号不存在
            return Results.Ok(new LoginResponse(false, "账号不存在", "", -1));
        }
    }
    catch (Exception ex)
    {
        // 如果数据库连接出错，返回错误信息（开发阶段方便调试）
        return Results.Ok(new LoginResponse(false, "服务器错误: " + ex.Message, "", -1));
    }
});

// =================== 登出接口：POST /api/logout ===================

app.MapPost("/api/logout", ([FromBody] LogoutRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new LogoutResponse(false, "账号ID无效"));
        }

        // 从在线集合中移除账号
        if (在线账号集合.TryRemove(请求.AccountId, out _))
        {
            return Results.Ok(new LogoutResponse(true, "登出成功"));
        }
        else
        {
            // 账号不在线集合中（可能已经登出或服务器重启）
            return Results.Ok(new LogoutResponse(true, "登出成功（账号未在线）"));
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new LogoutResponse(false, "服务器错误: " + ex.Message));
    }
});

// =================== 心跳接口：POST /api/heartbeat ===================

app.MapPost("/api/heartbeat", async ([FromBody] HeartbeatRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new HeartbeatResponse(false, "账号ID无效", -1));
        }

        // 更新在线账号的心跳时间
        if (在线账号集合.ContainsKey(请求.AccountId))
        {
            在线账号集合[请求.AccountId] = DateTime.Now;
            
            // 查询玩家当前的家族ID（用于客户端检测家族变化）
            int 家族ID = -1;
            using var connection = new MySqlConnection(数据库连接字符串);
            await connection.OpenAsync();
            
            using var clanCommand = new MySqlCommand(
                "SELECT clan_id FROM players WHERE account_id = @account_id LIMIT 1",
                connection
            );
            clanCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);
            
            var clanResult = await clanCommand.ExecuteScalarAsync();
            if (clanResult != null && !DBNull.Value.Equals(clanResult))
            {
                家族ID = Convert.ToInt32(clanResult);
            }
            
            return Results.Ok(new HeartbeatResponse(true, "心跳成功", 家族ID));
        }
        else
        {
            return Results.Ok(new HeartbeatResponse(false, "账号未在线", -1));
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new HeartbeatResponse(false, "服务器错误: " + ex.Message, -1));
    }
});

// =================== 注册接口：POST /api/register ===================

app.MapPost("/api/register", async ([FromBody] RegisterRequest 请求) =>
{
    try
    {
        // 验证输入
        if (string.IsNullOrWhiteSpace(请求.Username) || string.IsNullOrWhiteSpace(请求.Password))
        {
            return Results.Ok(new RegisterResponse(false, "账号和密码不能为空"));
        }

        // 连接 MySQL 数据库
        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 先检查账号是否已存在
        using var checkCommand = new MySqlCommand(
            "SELECT COUNT(*) FROM accounts WHERE username = @username",
            connection
        );
        checkCommand.Parameters.AddWithValue("@username", 请求.Username);
        
        var 账号数量结果 = await checkCommand.ExecuteScalarAsync();
        long 账号数量 = 账号数量结果 != null ? (long)账号数量结果 : 0;
        
        if (账号数量 > 0)
        {
            return Results.Ok(new RegisterResponse(false, "账号已存在，请使用其他账号名"));
        }

        // 账号不存在，可以注册
        // 把密码做 SHA256 哈希
        string 密码哈希 = 计算SHA256(请求.Password);

        // 插入新账号
        using var insertCommand = new MySqlCommand(
            "INSERT INTO accounts (username, password_hash) VALUES (@username, @password_hash)",
            connection
        );
        insertCommand.Parameters.AddWithValue("@username", 请求.Username);
        insertCommand.Parameters.AddWithValue("@password_hash", 密码哈希);

        await insertCommand.ExecuteNonQueryAsync();

        return Results.Ok(new RegisterResponse(true, "注册成功"));
    }
    catch (Exception ex)
    {
        // 如果数据库连接出错，返回错误信息（开发阶段方便调试）
        return Results.Ok(new RegisterResponse(false, "服务器错误: " + ex.Message));
    }
});

// =================== 获取玩家信息接口：POST /api/getPlayer ===================

app.MapPost("/api/getPlayer", async ([FromBody] GetPlayerRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new GetPlayerResponse(false, "账号ID无效", null));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 查询玩家信息（LEFT JOIN 关联属性、国家、家族）
        string sql = @"
            SELECT 
                p.id, p.name, p.gender, p.level, p.experience, p.title_name, p.office,
                p.copper_money, p.gold, p.country_id, p.clan_id, p.clan_contribution,
                pa.max_hp, pa.current_hp, pa.attack, pa.defense, pa.crit_rate,
                c.id as country_id_full, c.name as country_name, c.code as country_code,
                cl.id as clan_id_full, cl.name as clan_name
            FROM players p
            LEFT JOIN player_attributes pa ON p.id = pa.player_id
            LEFT JOIN countries c ON p.country_id = c.id
            LEFT JOIN clans cl ON p.clan_id = cl.id
            WHERE p.account_id = @account_id
            LIMIT 1";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@account_id", 请求.AccountId);

        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            // 玩家存在，构建返回数据
            // SQL 查询顺序：p.id(0), p.name(1), p.gender(2), p.level(3), p.experience(4), p.title_name(5), p.office(6),
            // p.copper_money(7), p.gold(8), p.country_id(9), p.clan_id(10), p.clan_contribution(11),
            // pa.max_hp(12), pa.current_hp(13), pa.attack(14), pa.defense(15), pa.crit_rate(16),
            // c.id(17), c.name(18), c.code(19), cl.id(20), cl.name(21)
            var 玩家数据 = new PlayerData
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Gender = reader.GetString(2),
                Level = reader.GetInt32(3),
                Experience = reader.GetInt32(4),  // 经验值
                TitleName = reader.GetString(5),
                Office = reader.GetString(6),
                CopperMoney = reader.GetInt32(7),
                Gold = reader.GetInt32(8),
                CountryId = reader.IsDBNull(9) ? -1 : reader.GetInt32(9),  // Unity JsonUtility 不支持可空类型，用 -1 表示 null
                ClanId = reader.IsDBNull(10) ? -1 : reader.GetInt32(10),    // Unity JsonUtility 不支持可空类型，用 -1 表示 null
                ClanContribution = reader.GetInt32(11),  // 家族贡献值
                Attributes = new PlayerAttributesData
                {
                    MaxHp = reader.GetInt32(12),
                    CurrentHp = reader.GetInt32(13),
                    Attack = reader.GetInt32(14),
                    Defense = reader.GetInt32(15),
                    CritRate = reader.GetFloat(16)
                },
                Country = reader.IsDBNull(17) ? null : new CountryData
                {
                    Id = reader.GetInt32(17),
                    Name = reader.GetString(18),
                    Code = reader.GetString(19)
                },
                Clan = reader.IsDBNull(20) ? null : new ClanData
                {
                    Id = reader.GetInt32(20),
                    Name = reader.GetString(21)
                }
            };

            return Results.Ok(new GetPlayerResponse(true, "获取成功", 玩家数据));
        }
        else
        {
            // 玩家不存在
            return Results.Ok(new GetPlayerResponse(false, "该账号尚未创建角色", null));
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetPlayerResponse(false, "服务器错误: " + ex.Message, null));
    }
});

// =================== 战斗结果接口：POST /api/battleResult ===================

app.MapPost("/api/battleResult", async ([FromBody] BattleResultRequest 请求) =>
{
    try
    {
        if (请求.PlayerId <= 0)
        {
            return Results.Ok(new BattleResultResponse(false, "玩家ID无效", 0, 0, 0, false, 0));
        }

        if (!请求.Victory)
        {
            // 战斗失败，只返回当前数据，不更新
            using var failConnection = new MySqlConnection(数据库连接字符串);
            await failConnection.OpenAsync();
            
            using var selectCommand = new MySqlCommand(
                "SELECT experience, copper_money, current_hp FROM players p LEFT JOIN player_attributes pa ON p.id = pa.player_id WHERE p.id = @player_id",
                failConnection
            );
            selectCommand.Parameters.AddWithValue("@player_id", 请求.PlayerId);
            
            using var reader = await selectCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int 当前经验值 = reader.GetInt32(0);
                int 当前铜钱 = reader.GetInt32(1);
                int 当前生命值 = reader.GetInt32(2);
                reader.Close();
                return Results.Ok(new BattleResultResponse(true, "战斗失败", 当前经验值, 当前铜钱, 当前生命值, false, 0));
            }
            else
            {
                return Results.Ok(new BattleResultResponse(false, "玩家不存在", 0, 0, 0, false, 0));
            }
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 开始事务
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 1. 查询玩家当前数据
            using var selectCommand = new MySqlCommand(
                @"SELECT p.level, p.experience, p.copper_money, pa.current_hp, pa.max_hp, pa.attack, pa.defense
                  FROM players p
                  LEFT JOIN player_attributes pa ON p.id = pa.player_id
                  WHERE p.id = @player_id",
                connection,
                transaction
            );
            selectCommand.Parameters.AddWithValue("@player_id", 请求.PlayerId);

            using var reader = await selectCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                reader.Close();
                await transaction.RollbackAsync();
                return Results.Ok(new BattleResultResponse(false, "玩家不存在", 0, 0, 0, false, 0));
            }

            int 当前等级 = reader.GetInt32(0);
            int 当前经验值 = reader.GetInt32(1);
            int 当前铜钱 = reader.GetInt32(2);
            int 当前生命值 = reader.GetInt32(3);
            int 最大生命值 = reader.GetInt32(4);
            int 攻击力 = reader.GetInt32(5);
            int 防御力 = reader.GetInt32(6);
            reader.Close();

            // 2. 计算新的经验值和铜钱
            int 新经验值 = 当前经验值 + 请求.Experience;
            int 新铜钱 = 当前铜钱 + 请求.CopperMoney;

            // 3. 计算升级（使用与客户端相同的公式）
            // 基础经验值 = 1000，每级经验增长 = 500
            // 升级到下一级所需经验 = 1000 + (当前等级 - 1) * 500
            int 基础经验值 = 1000;
            int 每级经验增长 = 500;
            bool 升级了 = false;
            int 新等级 = 当前等级;

            while (新经验值 >= (基础经验值 + (新等级 - 1) * 每级经验增长))
            {
                int 升级所需经验 = 基础经验值 + (新等级 - 1) * 每级经验增长;
                新经验值 -= 升级所需经验;
                新等级++;
                升级了 = true;
            }

            // 4. 更新玩家数据
            using var updatePlayerCommand = new MySqlCommand(
                @"UPDATE players 
                  SET level = @level, experience = @experience, copper_money = @copper_money
                  WHERE id = @player_id",
                connection,
                transaction
            );
            updatePlayerCommand.Parameters.AddWithValue("@level", 新等级);
            updatePlayerCommand.Parameters.AddWithValue("@experience", 新经验值);
            updatePlayerCommand.Parameters.AddWithValue("@copper_money", 新铜钱);
            updatePlayerCommand.Parameters.AddWithValue("@player_id", 请求.PlayerId);
            await updatePlayerCommand.ExecuteNonQueryAsync();

            // 5. 如果升级了，重新计算属性
            int 新最大生命值 = 最大生命值;
            int 新攻击力 = 攻击力;
            int 新防御力 = 防御力;
            int 新当前生命值 = 当前生命值;

            if (升级了)
            {
                // 重新计算属性（根据新等级）
                新最大生命值 = 新等级 * 200;
                新攻击力 = 100000; // 攻击力固定为100000
                新防御力 = 新等级 * 2;
                // 升级后恢复满血
                新当前生命值 = 新最大生命值;

                // 更新玩家属性
                using var updateAttrCommand = new MySqlCommand(
                    @"UPDATE player_attributes 
                      SET max_hp = @max_hp, current_hp = @current_hp, attack = @attack, defense = @defense
                      WHERE player_id = @player_id",
                    connection,
                    transaction
                );
                updateAttrCommand.Parameters.AddWithValue("@max_hp", 新最大生命值);
                updateAttrCommand.Parameters.AddWithValue("@current_hp", 新当前生命值);
                updateAttrCommand.Parameters.AddWithValue("@attack", 新攻击力);
                updateAttrCommand.Parameters.AddWithValue("@defense", 新防御力);
                updateAttrCommand.Parameters.AddWithValue("@player_id", 请求.PlayerId);
                await updateAttrCommand.ExecuteNonQueryAsync();
            }
            else
            {
                // 未升级，使用客户端发送的战斗后的当前生命值
                新当前生命值 = 请求.CurrentHp;
                // 确保当前生命值不超过最大生命值
                if (新当前生命值 > 最大生命值)
                {
                    新当前生命值 = 最大生命值;
                }
                // 确保当前生命值不为负数
                if (新当前生命值 < 0)
                {
                    新当前生命值 = 0;
                }

                // 更新当前生命值
                using var updateHpCommand = new MySqlCommand(
                    @"UPDATE player_attributes 
                      SET current_hp = @current_hp
                      WHERE player_id = @player_id",
                    connection,
                    transaction
                );
                updateHpCommand.Parameters.AddWithValue("@current_hp", 新当前生命值);
                updateHpCommand.Parameters.AddWithValue("@player_id", 请求.PlayerId);
                await updateHpCommand.ExecuteNonQueryAsync();
            }

            // 提交事务
            await transaction.CommitAsync();

            return Results.Ok(new BattleResultResponse(
                true,
                "战斗结果处理成功",
                新经验值,
                新铜钱,
                新当前生命值,
                升级了,
                新等级
            ));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new BattleResultResponse(false, "服务器错误: " + ex.Message, 0, 0, 0, false, 0));
    }
});

// =================== 创建玩家接口：POST /api/createPlayer ===================

app.MapPost("/api/createPlayer", async ([FromBody] CreatePlayerRequest 请求) =>
{
    try
    {
        // 验证输入
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new CreatePlayerResponse(false, "账号ID无效"));
        }

        if (string.IsNullOrWhiteSpace(请求.Name))
        {
            return Results.Ok(new CreatePlayerResponse(false, "玩家姓名不能为空"));
        }

        // 调试：打印收到的值
        string 收到的性别 = 请求.Gender ?? "NULL";
        string 收到的姓名 = 请求.Name ?? "NULL";
        int 收到的账号ID = 请求.AccountId;
        
        if (string.IsNullOrWhiteSpace(请求.Gender) || (请求.Gender != "男" && 请求.Gender != "女"))
        {
            return Results.Ok(new CreatePlayerResponse(false, $"性别必须是'男'或'女'，实际收到：'{收到的性别}'，姓名：'{收到的姓名}'，账号ID：{收到的账号ID}"));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 先检查该账号是否已有角色
        using var checkCommand = new MySqlCommand(
            "SELECT COUNT(*) FROM players WHERE account_id = @account_id",
            connection
        );
        checkCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);
        
        var 玩家数量结果 = await checkCommand.ExecuteScalarAsync();
        long 玩家数量 = 玩家数量结果 != null ? (long)玩家数量结果 : 0;
        
        if (玩家数量 > 0)
        {
            return Results.Ok(new CreatePlayerResponse(false, "该账号已创建角色，无法重复创建"));
        }

        // 检查姓名是否重复
        using var checkNameCommand = new MySqlCommand(
            "SELECT COUNT(*) FROM players WHERE name = @name",
            connection
        );
        checkNameCommand.Parameters.AddWithValue("@name", 请求.Name);
        
        var 同名数量结果 = await checkNameCommand.ExecuteScalarAsync();
        long 同名数量 = 同名数量结果 != null ? (long)同名数量结果 : 0;
        
        if (同名数量 > 0)
        {
            return Results.Ok(new CreatePlayerResponse(false, "该姓名已被使用，请使用其他姓名"));
        }

        // 开始事务
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 插入玩家数据
            using var insertPlayerCommand = new MySqlCommand(
                @"INSERT INTO players (account_id, name, gender, level, experience, title_name, office, copper_money, gold)
                  VALUES (@account_id, @name, @gender, 1, 0, '无', '国民', 50000000, 2000000)",
                connection,
                transaction
            );
            insertPlayerCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);
            insertPlayerCommand.Parameters.AddWithValue("@name", 请求.Name);
            insertPlayerCommand.Parameters.AddWithValue("@gender", 请求.Gender);

            await insertPlayerCommand.ExecuteNonQueryAsync();

            // 获取刚插入的玩家ID
            long 玩家ID = insertPlayerCommand.LastInsertedId;

            // 计算初始属性（根据等级）
            int 初始生命值 = 1 * 200; // 等级 * 200
            int 初始攻击力 = 100000;
            int 初始防御力 = 1 * 2; // 等级 * 2

            // 插入玩家属性
            using var insertAttrCommand = new MySqlCommand(
                @"INSERT INTO player_attributes (player_id, max_hp, current_hp, attack, defense, crit_rate)
                  VALUES (@player_id, @max_hp, @current_hp, @attack, @defense, 0.0)",
                connection,
                transaction
            );
            insertAttrCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            insertAttrCommand.Parameters.AddWithValue("@max_hp", 初始生命值);
            insertAttrCommand.Parameters.AddWithValue("@current_hp", 初始生命值);
            insertAttrCommand.Parameters.AddWithValue("@attack", 初始攻击力);
            insertAttrCommand.Parameters.AddWithValue("@defense", 初始防御力);

            await insertAttrCommand.ExecuteNonQueryAsync();

            // 提交事务
            await transaction.CommitAsync();

            return Results.Ok(new CreatePlayerResponse(true, "角色创建成功"));
        }
        catch
        {
            // 回滚事务
            await transaction.RollbackAsync();
            throw;
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new CreatePlayerResponse(false, "服务器错误: " + ex.Message));
    }
});

// =================== 获取国家列表接口：GET /api/countries ===================

app.MapGet("/api/countries", async () =>
{
    try
    {
        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        using var command = new MySqlCommand(
            @"SELECT id, name, code, declaration, announcement, copper_money, food, gold
              FROM countries
              ORDER BY id",
            connection
        );

        using var reader = await command.ExecuteReaderAsync();

        var 列表 = new List<CountrySummary>();

        while (await reader.ReadAsync())
        {
            var 国家 = new CountrySummary
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Code = reader.GetString(2),
                Declaration = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Announcement = reader.IsDBNull(4) ? "" : reader.GetString(4),
                CopperMoney = reader.GetInt32(5),
                Food = reader.GetInt32(6),
                Gold = reader.GetInt32(7)
            };

            列表.Add(国家);
        }

        return Results.Ok(new CountryListResponse(true, "获取国家列表成功", 列表));
    }
    catch (Exception ex)
    {
        return Results.Ok(new CountryListResponse(false, "服务器错误: " + ex.Message, new List<CountrySummary>()));
    }
});

// =================== 加入国家接口：POST /api/joinCountry ===================

app.MapPost("/api/joinCountry", async ([FromBody] JoinCountryRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0 || 请求.CountryId <= 0)
        {
            return Results.Ok(new JoinCountryResponse(false, "账号ID或国家ID无效"));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 检查国家是否存在
        using (var checkCountryCmd = new MySqlCommand(
            "SELECT COUNT(*) FROM countries WHERE id = @country_id",
            connection))
        {
            checkCountryCmd.Parameters.AddWithValue("@country_id", 请求.CountryId);
            var countryCountObj = await checkCountryCmd.ExecuteScalarAsync();
            long countryCount = countryCountObj != null ? (long)countryCountObj : 0;

            if (countryCount == 0)
            {
                return Results.Ok(new JoinCountryResponse(false, "指定的国家不存在"));
            }
        }

        // 查找该账号对应的玩家
        int 玩家ID = -1;
        int 当前国家ID = -1;

        using (var findPlayerCmd = new MySqlCommand(
            "SELECT id, country_id FROM players WHERE account_id = @account_id LIMIT 1",
            connection))
        {
            findPlayerCmd.Parameters.AddWithValue("@account_id", 请求.AccountId);

            using var reader = await findPlayerCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                玩家ID = reader.GetInt32(0);
                当前国家ID = reader.IsDBNull(1) ? -1 : reader.GetInt32(1);
            }
            else
            {
                return Results.Ok(new JoinCountryResponse(false, "该账号尚未创建角色"));
            }
        }

        // 如果已经在这个国家，就直接返回成功
        if (当前国家ID == 请求.CountryId)
        {
            return Results.Ok(new JoinCountryResponse(true, "你已经在该国家中"));
        }

        // 更新玩家的国家ID
        using (var updateCmd = new MySqlCommand(
            "UPDATE players SET country_id = @country_id WHERE id = @player_id",
            connection))
        {
            updateCmd.Parameters.AddWithValue("@country_id", 请求.CountryId);
            updateCmd.Parameters.AddWithValue("@player_id", 玩家ID);

            int rows = await updateCmd.ExecuteNonQueryAsync();
            if (rows > 0)
            {
                return Results.Ok(new JoinCountryResponse(true, "加入国家成功"));
            }
            else
            {
                return Results.Ok(new JoinCountryResponse(false, "加入国家失败，未能更新玩家数据"));
            }
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new JoinCountryResponse(false, "服务器错误: " + ex.Message));
    }
});

// =================== 获取国家信息接口（成员总数 + 排名）：POST /api/getCountryInfo ===================

app.MapPost("/api/getCountryInfo", async ([FromBody] GetCountryInfoRequest 请求) =>
{
    try
    {
        if (请求.CountryId <= 0)
        {
            return Results.Ok(new GetCountryInfoResponse(false, "国家ID无效", 0, 0, null, null, null, null, null));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 检查国家是否存在
        using (var checkCountryCmd = new MySqlCommand(
            "SELECT COUNT(*) FROM countries WHERE id = @country_id",
            connection))
        {
            checkCountryCmd.Parameters.AddWithValue("@country_id", 请求.CountryId);
            var countryCountObj = await checkCountryCmd.ExecuteScalarAsync();
            long countryCount = countryCountObj != null ? (long)countryCountObj : 0;

            if (countryCount == 0)
            {
                return Results.Ok(new GetCountryInfoResponse(false, "指定的国家不存在", 0, 0, null, null, null, null, null));
            }
        }

        int 成员总数 = 0;
        int 排名 = 0;

        // 统计该国家的成员总数（基于 players 表）
        using (var countCmd = new MySqlCommand(
            "SELECT COUNT(*) FROM players WHERE country_id = @country_id",
            connection))
        {
            countCmd.Parameters.AddWithValue("@country_id", 请求.CountryId);
            var memberCountObj = await countCmd.ExecuteScalarAsync();
            long memberCount = memberCountObj != null ? (long)memberCountObj : 0;
            成员总数 = (int)memberCount;
        }

        // 计算该国家的排名（按照黄金从高到低排序，相同黄金用 id 作为次排序）
        using (var rankCmd = new MySqlCommand(
            @"SELECT COUNT(*) + 1 
              FROM countries 
              WHERE gold > (SELECT gold FROM countries WHERE id = @country_id)
                 OR (gold = (SELECT gold FROM countries WHERE id = @country_id) AND id < @country_id)",
            connection))
        {
            rankCmd.Parameters.AddWithValue("@country_id", 请求.CountryId);
            var rankObj = await rankCmd.ExecuteScalarAsync();
            long rankVal = rankObj != null ? (long)rankObj : 0;
            排名 = (int)rankVal;
        }

        // 查询宣战家族信息
        int? 宣战家族1ID = null;
        string? 宣战家族1名称 = null;
        int? 宣战家族2ID = null;
        string? 宣战家族2名称 = null;
        DateTime? 战场开始时间 = null;

            using (var warCmd = new MySqlCommand(
            @"SELECT 
                c.war_clan1_id, c1.name as clan1_name,
                c.war_clan2_id, c2.name as clan2_name,
                b.start_time
              FROM countries c
              LEFT JOIN clans c1 ON c.war_clan1_id = c1.id
              LEFT JOIN clans c2 ON c.war_clan2_id = c2.id
              LEFT JOIN battlefields b ON b.country_id = c.id AND b.battlefield_status = 'preparing'
              WHERE c.id = @country_id
              LIMIT 1",
            connection))
        {
            warCmd.Parameters.AddWithValue("@country_id", 请求.CountryId);
            using var warReader = await warCmd.ExecuteReaderAsync();
            if (await warReader.ReadAsync())
            {
                if (!warReader.IsDBNull(0))
                {
                    宣战家族1ID = warReader.GetInt32(0);
                    宣战家族1名称 = warReader.IsDBNull(1) ? null : warReader.GetString(1);
                }
                if (!warReader.IsDBNull(2))
                {
                    宣战家族2ID = warReader.GetInt32(2);
                    宣战家族2名称 = warReader.IsDBNull(3) ? null : warReader.GetString(3);
                }
                战场开始时间 = warReader.IsDBNull(4) ? null : warReader.GetDateTime(4);
            }
        }

        return Results.Ok(new GetCountryInfoResponse(
            true, 
            "获取国家信息成功", 
            成员总数, 
            排名,
            宣战家族1ID,
            宣战家族1名称,
            宣战家族2ID,
            宣战家族2名称,
            战场开始时间
        ));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetCountryInfoResponse(false, "服务器错误: " + ex.Message, 0, 0, null, null, null, null, null));
    }
});

// =================== 王城战宣战接口：POST /api/declareWar ===================

app.MapPost("/api/declareWar", async ([FromBody] DeclareWarRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0 || 请求.CountryId <= 0)
        {
            return Results.Ok(new DeclareWarResponse(false, "账号ID或国家ID无效", false));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 开始事务
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 1. 查询玩家信息
            int 玩家ID = -1;
            int? 家族ID = null;
            using (var playerCmd = new MySqlCommand(
                "SELECT id, clan_id FROM players WHERE account_id = @account_id LIMIT 1",
                connection,
                transaction))
            {
                playerCmd.Parameters.AddWithValue("@account_id", 请求.AccountId);
                using var playerReader = await playerCmd.ExecuteReaderAsync();
                if (await playerReader.ReadAsync())
                {
                    玩家ID = playerReader.GetInt32(0);
                    家族ID = playerReader.IsDBNull(1) ? null : playerReader.GetInt32(1);
                }
                else
                {
                    await transaction.RollbackAsync();
                    return Results.Ok(new DeclareWarResponse(false, "玩家不存在", false));
                }
            }

            // 2. 检查玩家是否有家族
            if (!家族ID.HasValue || 家族ID.Value <= 0)
            {
                await transaction.RollbackAsync();
                return Results.Ok(new DeclareWarResponse(false, "请先加入或创建家族", false));
            }

            // 3. 检查玩家是否是族长或副族长
            using (var roleCmd = new MySqlCommand(
                @"SELECT leader_id, deputy_leader_id FROM clans WHERE id = @clan_id",
                connection,
                transaction))
            {
                roleCmd.Parameters.AddWithValue("@clan_id", 家族ID.Value);
                using var roleReader = await roleCmd.ExecuteReaderAsync();
                if (await roleReader.ReadAsync())
                {
                    int 族长ID = roleReader.IsDBNull(0) ? -1 : roleReader.GetInt32(0);
                    int 副族长ID = roleReader.IsDBNull(1) ? -1 : roleReader.GetInt32(1);
                    
                    if (玩家ID != 族长ID && 玩家ID != 副族长ID)
                    {
                        await transaction.RollbackAsync();
                        return Results.Ok(new DeclareWarResponse(false, "只有族长或副族长可以宣战", false));
                    }
                }
                else
                {
                    await transaction.RollbackAsync();
                    return Results.Ok(new DeclareWarResponse(false, "家族不存在", false));
                }
            }

            // 4. 检查家族资金是否足够（需要10家族资金）
            int 家族资金 = 0;
            using (var fundsCmd = new MySqlCommand(
                "SELECT funds FROM clans WHERE id = @clan_id",
                connection,
                transaction))
            {
                fundsCmd.Parameters.AddWithValue("@clan_id", 家族ID.Value);
                var fundsObj = await fundsCmd.ExecuteScalarAsync();
                if (fundsObj != null)
                {
                    家族资金 = Convert.ToInt32(fundsObj);
                }
            }

            if (家族资金 < 10)
            {
                await transaction.RollbackAsync();
                return Results.Ok(new DeclareWarResponse(false, "家族资金不足，需要10家族资金", false));
            }

            // 5. 查询当前国家的宣战状态
            int? 宣战家族1ID = null;
            int? 宣战家族2ID = null;
            using (var countryCmd = new MySqlCommand(
                "SELECT war_clan1_id, war_clan2_id FROM countries WHERE id = @country_id",
                connection,
                transaction))
            {
                countryCmd.Parameters.AddWithValue("@country_id", 请求.CountryId);
                using var countryReader = await countryCmd.ExecuteReaderAsync();
                if (await countryReader.ReadAsync())
                {
                    宣战家族1ID = countryReader.IsDBNull(0) ? null : countryReader.GetInt32(0);
                    宣战家族2ID = countryReader.IsDBNull(1) ? null : countryReader.GetInt32(1);
                }
                else
                {
                    await transaction.RollbackAsync();
                    return Results.Ok(new DeclareWarResponse(false, "国家不存在", false));
                }
            }

            // 6. 检查是否已经宣战
            if (宣战家族1ID == 家族ID.Value || 宣战家族2ID == 家族ID.Value)
            {
                await transaction.RollbackAsync();
                return Results.Ok(new DeclareWarResponse(false, "你的家族已经宣战", false));
            }

            // 7. 检查是否已经有两个家族宣战
            if (宣战家族1ID.HasValue && 宣战家族2ID.HasValue)
            {
                await transaction.RollbackAsync();
                return Results.Ok(new DeclareWarResponse(false, "当前已有两个家族宣战，无法再宣战", false));
            }

            // 8. 扣除家族资金并设置宣战家族
            bool 两个家族都就绪 = false;
            if (!宣战家族1ID.HasValue)
            {
                // 设置宣战家族1
                using (var updateCmd = new MySqlCommand(
                    @"UPDATE countries SET war_clan1_id = @clan_id WHERE id = @country_id;
                      UPDATE clans SET funds = funds - 10, is_war_declared = TRUE WHERE id = @clan_id",
                    connection,
                    transaction))
                {
                    updateCmd.Parameters.AddWithValue("@clan_id", 家族ID.Value);
                    updateCmd.Parameters.AddWithValue("@country_id", 请求.CountryId);
                    await updateCmd.ExecuteNonQueryAsync();
                }
            }
            else
            {
                // 设置宣战家族2
                using (var updateCmd = new MySqlCommand(
                    @"UPDATE countries SET war_clan2_id = @clan_id WHERE id = @country_id;
                      UPDATE clans SET funds = funds - 10, is_war_declared = TRUE WHERE id = @clan_id",
                    connection,
                    transaction))
                {
                    updateCmd.Parameters.AddWithValue("@clan_id", 家族ID.Value);
                    updateCmd.Parameters.AddWithValue("@country_id", 请求.CountryId);
                    await updateCmd.ExecuteNonQueryAsync();
                }
                两个家族都就绪 = true;
            }

            // 9. 如果两个家族都就绪，创建战场记录并开始倒计时
            if (两个家族都就绪)
            {
                // 查询两个家族ID
                int 最终家族1ID = 宣战家族1ID ?? 家族ID.Value;
                int 最终家族2ID = 家族ID.Value;

                // 创建或更新战场记录
                DateTime 战场开始时间 = DateTime.Now.AddSeconds(30); // 30秒后开始
                using (var battlefieldCmd = new MySqlCommand(
                    @"INSERT INTO battlefields (country_id, clan_a_id, clan_b_id, battlefield_status, start_time, created_at)
                      VALUES (@country_id, @clan1_id, @clan2_id, 'preparing', @battle_start_time, NOW())
                      ON DUPLICATE KEY UPDATE
                          clan_a_id = @clan1_id,
                          clan_b_id = @clan2_id,
                          battlefield_status = 'preparing',
                          start_time = @battle_start_time",
                    connection,
                    transaction))
                {
                    battlefieldCmd.Parameters.AddWithValue("@country_id", 请求.CountryId);
                    battlefieldCmd.Parameters.AddWithValue("@clan1_id", 最终家族1ID);
                    battlefieldCmd.Parameters.AddWithValue("@clan2_id", 最终家族2ID);
                    battlefieldCmd.Parameters.AddWithValue("@battle_start_time", 战场开始时间);
                    await battlefieldCmd.ExecuteNonQueryAsync();
                }

                // 启动后台倒计时任务（这里简化处理，实际应该用后台服务）
                启动战场倒计时(请求.CountryId, 最终家族1ID, 最终家族2ID, 战场开始时间);
            }

            // 提交事务
            await transaction.CommitAsync();

            return Results.Ok(new DeclareWarResponse(true, "宣战成功", 两个家族都就绪));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new DeclareWarResponse(false, "服务器错误: " + ex.Message, false));
    }
});

// =================== 更换国家接口：POST /api/changeCountry ===================

app.MapPost("/api/changeCountry", async ([FromBody] ChangeCountryRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0 || 请求.CountryId <= 0)
        {
            return Results.Ok(new ChangeCountryResponse(false, "账号ID或国家ID无效"));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 检查国家是否存在
        using (var checkCountryCmd = new MySqlCommand(
            "SELECT COUNT(*) FROM countries WHERE id = @country_id",
            connection))
        {
            checkCountryCmd.Parameters.AddWithValue("@country_id", 请求.CountryId);
            var countryCountObj = await checkCountryCmd.ExecuteScalarAsync();
            long countryCount = countryCountObj != null ? (long)countryCountObj : 0;

            if (countryCount == 0)
            {
                return Results.Ok(new ChangeCountryResponse(false, "指定的国家不存在"));
            }
        }

        // 查找该账号对应的玩家，获取当前国家ID
        int 玩家ID = -1;
        int 当前国家ID = -1;

        using (var findPlayerCmd = new MySqlCommand(
            "SELECT id, country_id FROM players WHERE account_id = @account_id LIMIT 1",
            connection))
        {
            findPlayerCmd.Parameters.AddWithValue("@account_id", 请求.AccountId);

            using var reader = await findPlayerCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                玩家ID = reader.GetInt32(0);
                当前国家ID = reader.IsDBNull(1) ? -1 : reader.GetInt32(1);
            }
            else
            {
                return Results.Ok(new ChangeCountryResponse(false, "该账号尚未创建角色"));
            }
        }

        // 如果要更换的国家和当前国家相同，直接返回成功
        if (当前国家ID == 请求.CountryId)
        {
            return Results.Ok(new ChangeCountryResponse(true, "你已在该国家中，无需更换"));
        }

        // 开始事务，确保操作的原子性
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 如果玩家当前有国家，先从旧国家中移除（将 country_id 设为 NULL）
            // 注意：实际上由于一个玩家只能有一个 country_id，更新为新国家ID就会自动"离开"旧国家
            // 但为了满足用户需求，我们明确地先查询当前国家，然后更新
            if (当前国家ID > 0)
            {
                // 这里实际上不需要单独操作，因为更新 country_id 就会自动"离开"旧国家
                // 但为了逻辑清晰，我们可以记录日志或进行其他操作
            }

            // 更新玩家的国家ID为新国家
            using (var updateCmd = new MySqlCommand(
                "UPDATE players SET country_id = @country_id WHERE id = @player_id",
                connection,
                transaction))
            {
                updateCmd.Parameters.AddWithValue("@country_id", 请求.CountryId);
                updateCmd.Parameters.AddWithValue("@player_id", 玩家ID);

                int rows = await updateCmd.ExecuteNonQueryAsync();
                if (rows > 0)
                {
                    // 提交事务
                    await transaction.CommitAsync();
                    return Results.Ok(new ChangeCountryResponse(true, "更换国家成功"));
                }
                else
                {
                    await transaction.RollbackAsync();
                    return Results.Ok(new ChangeCountryResponse(false, "更换国家失败，未能更新玩家数据"));
                }
            }
        }
        catch
        {
            // 回滚事务
            await transaction.RollbackAsync();
            throw;
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new ChangeCountryResponse(false, "服务器错误: " + ex.Message));
    }
});

// =================== 获取国家成员列表接口：POST /api/getCountryMembers ===================

app.MapPost("/api/getCountryMembers", async ([FromBody] GetCountryMembersRequest 请求) =>
{
    try
    {
        if (请求.CountryId <= 0)
        {
            return Results.Ok(new GetCountryMembersResponse(false, "国家ID无效", new List<PlayerSummary>()));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 查询指定国家的所有成员，按属性之和降序排序（生命值+攻击力+防御力）
        string sql = @"
            SELECT 
                p.id, p.name, p.gender, p.level, p.title_name, p.office,
                p.copper_money, p.gold, p.clan_contribution,
                pa.max_hp, pa.current_hp, pa.attack, pa.defense, pa.crit_rate
            FROM players p
            LEFT JOIN player_attributes pa ON p.id = pa.player_id
            WHERE p.country_id = @country_id
            ORDER BY (COALESCE(pa.max_hp, 0) + COALESCE(pa.attack, 0) + COALESCE(pa.defense, 0)) DESC";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@country_id", 请求.CountryId);

        using var reader = await command.ExecuteReaderAsync();

        var 成员列表 = new List<PlayerSummary>();
        while (await reader.ReadAsync())
        {
            var 成员 = new PlayerSummary
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Gender = reader.GetString(2),
                Level = reader.GetInt32(3),
                TitleName = reader.GetString(4),
                Office = reader.GetString(5),
                CopperMoney = reader.GetInt32(6),
                Gold = reader.GetInt32(7),
                ClanContribution = reader.GetInt32(8),
                Attributes = new PlayerAttributesData
                {
                    MaxHp = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                    CurrentHp = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                    Attack = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                    Defense = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                    CritRate = reader.IsDBNull(13) ? 0f : reader.GetFloat(13)
                }
            };
            成员列表.Add(成员);
        }

        return Results.Ok(new GetCountryMembersResponse(true, "获取成功", 成员列表));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetCountryMembersResponse(false, "服务器错误: " + ex.Message, new List<PlayerSummary>()));
    }
});

// =================== 获取所有玩家列表接口：GET /api/getAllPlayers ===================

app.MapGet("/api/getAllPlayers", async () =>
{
    try
    {
        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 查询所有玩家，按属性之和降序排序（生命值+攻击力+防御力）
        string sql = @"
            SELECT 
                p.id, p.name, p.gender, p.level, p.title_name, p.office,
                p.copper_money, p.gold, p.country_id, p.clan_contribution,
                pa.max_hp, pa.current_hp, pa.attack, pa.defense, pa.crit_rate,
                c.name as country_name, c.code as country_code
            FROM players p
            LEFT JOIN player_attributes pa ON p.id = pa.player_id
            LEFT JOIN countries c ON p.country_id = c.id
            ORDER BY (COALESCE(pa.max_hp, 0) + COALESCE(pa.attack, 0) + COALESCE(pa.defense, 0)) DESC";

        using var command = new MySqlCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();

        var 玩家列表 = new List<PlayerSummary>();
        while (await reader.ReadAsync())
        {
            var 玩家 = new PlayerSummary
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Gender = reader.GetString(2),
                Level = reader.GetInt32(3),
                TitleName = reader.GetString(4),
                Office = reader.GetString(5),
                CopperMoney = reader.GetInt32(6),
                Gold = reader.GetInt32(7),
                CountryId = reader.IsDBNull(8) ? -1 : reader.GetInt32(8),
                ClanContribution = reader.GetInt32(9),
                Attributes = new PlayerAttributesData
                {
                    MaxHp = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                    CurrentHp = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                    Attack = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                    Defense = reader.IsDBNull(13) ? 0 : reader.GetInt32(13),
                    CritRate = reader.IsDBNull(14) ? 0f : reader.GetFloat(14)
                },
                CountryName = reader.IsDBNull(15) ? "" : reader.GetString(15),
                CountryCode = reader.IsDBNull(16) ? "" : reader.GetString(16)
            };
            玩家列表.Add(玩家);
        }

        return Results.Ok(new GetAllPlayersResponse(true, "获取成功", 玩家列表));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetAllPlayersResponse(false, "服务器错误: " + ex.Message, new List<PlayerSummary>()));
    }
});

// =================== 获取怪物模板接口：GET /api/getMonsterTemplates ===================

app.MapGet("/api/getMonsterTemplates", async () =>
{
    try
    {
        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 查询所有怪物模板（只返回普通怪物，不返回Boss）
        string sql = @"
            SELECT 
                id, monster_type, name, base_level, base_hp, base_attack, base_defense,
                base_copper_money, base_experience, level_growth_rate, is_boss, description
            FROM monster_templates
            WHERE is_boss = FALSE
            ORDER BY monster_type";

        using var command = new MySqlCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();

        var 模板列表 = new List<MonsterTemplateData>();
        while (await reader.ReadAsync())
        {
            var 模板 = new MonsterTemplateData
            {
                Id = reader.GetInt32(0),
                MonsterType = reader.GetInt32(1),
                Name = reader.GetString(2),
                BaseLevel = reader.GetInt32(3),
                BaseHp = reader.GetInt32(4),
                BaseAttack = reader.GetInt32(5),
                BaseDefense = reader.GetInt32(6),
                BaseCopperMoney = reader.GetInt32(7),
                BaseExperience = reader.GetInt32(8),
                LevelGrowthRate = reader.GetDecimal(9),
                IsBoss = reader.GetBoolean(10),
                Description = reader.IsDBNull(11) ? "" : reader.GetString(11)
            };
            模板列表.Add(模板);
        }

        return Results.Ok(new GetMonsterTemplatesResponse(true, "获取成功", 模板列表));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetMonsterTemplatesResponse(false, "服务器错误: " + ex.Message, new List<MonsterTemplateData>()));
    }
});

// =================== 创建家族接口：POST /api/createClan ===================

app.MapPost("/api/createClan", async ([FromBody] CreateClanRequest 请求) =>
{
    try
    {
        // 验证输入
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new CreateClanResponse(false, "账号ID无效", -1));
        }

        if (string.IsNullOrWhiteSpace(请求.ClanName))
        {
            return Results.Ok(new CreateClanResponse(false, "家族名字不能为空", -1));
        }

        // 验证家族名字长度
        if (请求.ClanName.Length > 50)
        {
            return Results.Ok(new CreateClanResponse(false, "家族名字长度不能超过50个字符", -1));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 1. 查询玩家信息，确认玩家存在且有国家归属
        using var playerCommand = new MySqlCommand(
            "SELECT id, country_id FROM players WHERE account_id = @account_id LIMIT 1",
            connection
        );
        playerCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);

        using var playerReader = await playerCommand.ExecuteReaderAsync();
        if (!await playerReader.ReadAsync())
        {
            return Results.Ok(new CreateClanResponse(false, "玩家不存在或尚未创建角色", -1));
        }

        int 玩家ID = playerReader.GetInt32(0);
        int? 国家ID = playerReader.IsDBNull(1) ? null : playerReader.GetInt32(1);
        playerReader.Close();

        // 检查玩家是否有国家归属
        if (!国家ID.HasValue || 国家ID.Value <= 0)
        {
            return Results.Ok(new CreateClanResponse(false, "创建家族失败：玩家必须属于某个国家", -1));
        }

        // 检查解散家族冷却时间（1小时）
        using var cooldownCommand = new MySqlCommand(
            "SELECT last_clan_disband_time FROM players WHERE id = @player_id",
            connection
        );
        cooldownCommand.Parameters.AddWithValue("@player_id", 玩家ID);
        
        var cooldownResult = await cooldownCommand.ExecuteScalarAsync();
        if (cooldownResult != null && !DBNull.Value.Equals(cooldownResult))
        {
            DateTime 最后解散时间 = (DateTime)cooldownResult;
            TimeSpan 时间差 = DateTime.Now - 最后解散时间;
            const int 冷却时间小时 = 1;
            
            if (时间差.TotalHours < 冷却时间小时)
            {
                int 剩余分钟 = (int)((冷却时间小时 - 时间差.TotalHours) * 60);
                return Results.Ok(new CreateClanResponse(false, $"创建家族失败：解散家族后需要等待{冷却时间小时}小时才能再次创建，还需等待{剩余分钟}分钟", -1));
            }
        }

        // 检查玩家是否已有家族
        using var checkClanCommand = new MySqlCommand(
            "SELECT clan_id FROM players WHERE id = @player_id",
            connection
        );
        checkClanCommand.Parameters.AddWithValue("@player_id", 玩家ID);

        var clanIdResult = await checkClanCommand.ExecuteScalarAsync();
        if (clanIdResult != null && !DBNull.Value.Equals(clanIdResult))
        {
            return Results.Ok(new CreateClanResponse(false, "创建家族失败：玩家已属于某个家族", -1));
        }

        // 3. 检查家族名字是否重复
        using var checkNameCommand = new MySqlCommand(
            "SELECT COUNT(*) FROM clans WHERE name = @clan_name",
            connection
        );
        checkNameCommand.Parameters.AddWithValue("@clan_name", 请求.ClanName);

        var nameCountResult = await checkNameCommand.ExecuteScalarAsync();
        long nameCount = nameCountResult != null ? (long)nameCountResult : 0;
        if (nameCount > 0)
        {
            return Results.Ok(new CreateClanResponse(false, "家族名字已被使用，请使用其他名字", -1));
        }

        // 4. 检查玩家黄金是否足够（创建家族需要5000黄金）
        using var checkGoldCommand = new MySqlCommand(
            "SELECT gold FROM players WHERE id = @player_id",
            connection
        );
        checkGoldCommand.Parameters.AddWithValue("@player_id", 玩家ID);
        
        var goldResult = await checkGoldCommand.ExecuteScalarAsync();
        int 当前黄金 = goldResult != null ? Convert.ToInt32(goldResult) : 0;
        const int 创建家族消耗黄金 = 5000;
        
        if (当前黄金 < 创建家族消耗黄金)
        {
            return Results.Ok(new CreateClanResponse(false, $"创建家族失败：需要{创建家族消耗黄金}黄金，当前只有{当前黄金}黄金", -1));
        }

        // 5. 开始事务，创建家族并更新玩家信息
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 插入家族数据
            using var insertClanCommand = new MySqlCommand(
                @"INSERT INTO clans (name, level, leader_id, deputy_leader_id, prosperity, funds, 
                                    war_score, is_war_declared, is_war_fighting, country_id)
                  VALUES (@name, 1, @leader_id, NULL, 0, 0, 0, FALSE, FALSE, @country_id)",
                connection,
                transaction
            );
            insertClanCommand.Parameters.AddWithValue("@name", 请求.ClanName);
            insertClanCommand.Parameters.AddWithValue("@leader_id", 玩家ID);
            insertClanCommand.Parameters.AddWithValue("@country_id", 国家ID.Value);

            await insertClanCommand.ExecuteNonQueryAsync();

            // 获取刚插入的家族ID
            long 家族ID = insertClanCommand.LastInsertedId;

            // 更新玩家的家族ID并扣除黄金
            using var updatePlayerCommand = new MySqlCommand(
                "UPDATE players SET clan_id = @clan_id, gold = gold - @cost_gold WHERE id = @player_id",
                connection,
                transaction
            );
            updatePlayerCommand.Parameters.AddWithValue("@clan_id", 家族ID);
            updatePlayerCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            updatePlayerCommand.Parameters.AddWithValue("@cost_gold", 创建家族消耗黄金);

            await updatePlayerCommand.ExecuteNonQueryAsync();

            // 在家族成员职位表中添加族长职位记录
            using var insertRoleCommand = new MySqlCommand(
                "INSERT INTO clan_member_roles (clan_id, player_id, role) VALUES (@clan_id, @player_id, 'leader')",
                connection,
                transaction
            );
            insertRoleCommand.Parameters.AddWithValue("@clan_id", 家族ID);
            insertRoleCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            await insertRoleCommand.ExecuteNonQueryAsync();

            // 提交事务
            await transaction.CommitAsync();

            // 记录家族日志：创建家族
            string 族长姓名 = "";
            using var leaderNameCommand = new MySqlCommand(
                "SELECT name FROM players WHERE id = @player_id",
                connection
            );
            leaderNameCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            var leaderNameResult = await leaderNameCommand.ExecuteScalarAsync();
            if (leaderNameResult != null)
            {
                族长姓名 = leaderNameResult.ToString() ?? "";
            }
            await 记录家族日志(
                connection,
                (int)家族ID,
                "create",
                玩家ID,
                族长姓名,
                null,
                null,
                null,
                $"{族长姓名}创建了家族"
            );

            return Results.Ok(new CreateClanResponse(true, "家族创建成功", (int)家族ID));
        }
        catch
        {
            // 回滚事务
            await transaction.RollbackAsync();
            throw;
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new CreateClanResponse(false, "服务器错误: " + ex.Message, -1));
    }
});

// =================== 获取家族详细信息接口：POST /api/getClanInfo ===================

app.MapPost("/api/getClanInfo", async ([FromBody] GetClanInfoRequest 请求) =>
{
    try
    {
        if (请求.ClanId <= 0)
        {
            return Results.Ok(new GetClanInfoResponse(false, "家族ID无效", null));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 1. 查询家族基本信息
        using var clanCommand = new MySqlCommand(
            @"SELECT id, name, level, leader_id, prosperity, funds, country_id
              FROM clans WHERE id = @clan_id",
            connection
        );
        clanCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);

        using var clanReader = await clanCommand.ExecuteReaderAsync();
        if (!await clanReader.ReadAsync())
        {
            return Results.Ok(new GetClanInfoResponse(false, "家族不存在", null));
        }

        int 家族ID = clanReader.GetInt32(0);
        string 家族名字 = clanReader.GetString(1);
        int 家族等级 = clanReader.GetInt32(2);
        int 族长ID = clanReader.GetInt32(3);
        int 家族繁荣值 = clanReader.GetInt32(4);
        int 家族资金 = clanReader.GetInt32(5);
        // 处理国家ID可能为NULL的情况
        int 国家ID = clanReader.IsDBNull(6) ? 0 : clanReader.GetInt32(6);
        clanReader.Close();

        // 2. 查询族长姓名
        string 族长姓名 = "";
        using var leaderCommand = new MySqlCommand(
            "SELECT name FROM players WHERE id = @leader_id",
            connection
        );
        leaderCommand.Parameters.AddWithValue("@leader_id", 族长ID);
        var leaderNameResult = await leaderCommand.ExecuteScalarAsync();
        if (leaderNameResult != null)
        {
            族长姓名 = leaderNameResult.ToString() ?? "";
        }

        // 3. 查询家族成员数
        using var memberCountCommand = new MySqlCommand(
            "SELECT COUNT(*) FROM players WHERE clan_id = @clan_id",
            connection
        );
        memberCountCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
        var memberCountResult = await memberCountCommand.ExecuteScalarAsync();
        int 成员数 = memberCountResult != null ? Convert.ToInt32(memberCountResult) : 0;

        // 4. 查询当前玩家在家族中的职位
        string 玩家职位 = "成员";
        if (请求.PlayerId > 0)
        {
            if (请求.PlayerId == 族长ID)
            {
                玩家职位 = "族长";
            }
            else
            {
                using var roleCommand = new MySqlCommand(
                    "SELECT role FROM clan_member_roles WHERE clan_id = @clan_id AND player_id = @player_id",
                    connection
                );
                roleCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
                roleCommand.Parameters.AddWithValue("@player_id", 请求.PlayerId);
                var roleResult = await roleCommand.ExecuteScalarAsync();
                if (roleResult != null)
                {
                    string 数据库职位 = roleResult.ToString() ?? "member";
                    // 将数据库中的职位转换为中文显示
                    switch (数据库职位)
                    {
                        case "leader":
                            玩家职位 = "族长";
                            break;
                        case "副族长":
                            玩家职位 = "副族长";
                            break;
                        case "精英":
                            玩家职位 = "精英";
                            break;
                        case "member":
                        default:
                            玩家职位 = "成员";
                            break;
                    }
                }
            }
        }

        // 5. 计算国家排名（基于家族繁荣值，同一国家内的排名，由高到低降序）
        // 注意：如果国家ID为0或NULL，则无法计算国家排名，返回0
        int 国家排名 = 0;
        if (国家ID > 0)
        {
            // 计算有多少个同国家的家族繁荣值比当前家族高
            // 如果繁荣值相同，则按ID排序（ID小的排名靠前）
            using var countryRankCommand = new MySqlCommand(
                @"SELECT COUNT(*) + 1 
                  FROM clans 
                  WHERE country_id = @country_id 
                    AND (prosperity > @prosperity OR (prosperity = @prosperity AND id < @clan_id))",
                connection
            );
            countryRankCommand.Parameters.AddWithValue("@country_id", 国家ID);
            countryRankCommand.Parameters.AddWithValue("@prosperity", 家族繁荣值);
            countryRankCommand.Parameters.AddWithValue("@clan_id", 家族ID);
            var countryRankResult = await countryRankCommand.ExecuteScalarAsync();
            if (countryRankResult != null)
            {
                国家排名 = Convert.ToInt32(countryRankResult);
            }
        }

        // 6. 计算世界排名（基于家族繁荣值，所有家族中的排名，由高到低降序）
        // 如果繁荣值相同，则按ID排序（ID小的排名靠前）
        int 世界排名 = 1;
        using var worldRankCommand = new MySqlCommand(
            @"SELECT COUNT(*) + 1 
              FROM clans 
              WHERE prosperity > @prosperity OR (prosperity = @prosperity AND id < @clan_id)",
            connection
        );
        worldRankCommand.Parameters.AddWithValue("@prosperity", 家族繁荣值);
        worldRankCommand.Parameters.AddWithValue("@clan_id", 家族ID);
        var worldRankResult = await worldRankCommand.ExecuteScalarAsync();
        if (worldRankResult != null)
        {
            世界排名 = Convert.ToInt32(worldRankResult);
        }

        var 家族信息 = new ClanInfoData
        {
            Id = 家族ID,
            Name = 家族名字,
            Level = 家族等级,
            LeaderId = 族长ID,
            LeaderName = 族长姓名,
            MemberCount = 成员数,
            Prosperity = 家族繁荣值,
            Funds = 家族资金,
            CountryRank = 国家排名,
            WorldRank = 世界排名,
            PlayerRole = 玩家职位
        };

        return Results.Ok(new GetClanInfoResponse(true, "获取成功", 家族信息));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetClanInfoResponse(false, "服务器错误: " + ex.Message, null));
    }
});

// =================== 解散家族接口：POST /api/disbandClan ===================

app.MapPost("/api/disbandClan", async ([FromBody] DisbandClanRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new DisbandClanResponse(false, "账号ID无效"));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 1. 查询玩家信息，确认玩家存在且有家族
        using var playerCommand = new MySqlCommand(
            "SELECT id, clan_id FROM players WHERE account_id = @account_id LIMIT 1",
            connection
        );
        playerCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);

        using var playerReader = await playerCommand.ExecuteReaderAsync();
        if (!await playerReader.ReadAsync())
        {
            return Results.Ok(new DisbandClanResponse(false, "玩家不存在或尚未创建角色"));
        }

        int 玩家ID = playerReader.GetInt32(0);
        int? 家族ID = playerReader.IsDBNull(1) ? null : playerReader.GetInt32(1);
        playerReader.Close();

        // 检查玩家是否有家族
        if (!家族ID.HasValue || 家族ID.Value <= 0)
        {
            return Results.Ok(new DisbandClanResponse(false, "解散家族失败：玩家不属于任何家族"));
        }

        // 2. 查询家族信息，确认玩家是族长
        using var clanCommand = new MySqlCommand(
            "SELECT id, leader_id, country_id FROM clans WHERE id = @clan_id",
            connection
        );
        clanCommand.Parameters.AddWithValue("@clan_id", 家族ID.Value);

        using var clanReader = await clanCommand.ExecuteReaderAsync();
        if (!await clanReader.ReadAsync())
        {
            return Results.Ok(new DisbandClanResponse(false, "解散家族失败：家族不存在"));
        }

        int 族长ID = clanReader.GetInt32(1);
        int? 国家ID = clanReader.IsDBNull(2) ? null : clanReader.GetInt32(2);
        clanReader.Close();

        // 检查玩家是否是族长
        if (玩家ID != 族长ID)
        {
            return Results.Ok(new DisbandClanResponse(false, "解散家族失败：只有族长可以解散家族"));
        }

        // 3. 检查该家族是否是执政家族，如果是，需要清除国家的国王和执政家族
        int? 执政家族国家ID = null;
        if (国家ID.HasValue)
        {
            using var rulingClanCommand = new MySqlCommand(
                "SELECT id, ruling_clan_id, king_id FROM countries WHERE id = @country_id",
                connection
            );
            rulingClanCommand.Parameters.AddWithValue("@country_id", 国家ID.Value);

            using var rulingClanReader = await rulingClanCommand.ExecuteReaderAsync();
            if (await rulingClanReader.ReadAsync())
            {
                int? 执政家族ID = rulingClanReader.IsDBNull(1) ? null : rulingClanReader.GetInt32(1);
                if (执政家族ID.HasValue && 执政家族ID.Value == 家族ID.Value)
                {
                    执政家族国家ID = 国家ID.Value;
                }
            }
            rulingClanReader.Close();
        }

        // 4. 开始事务，执行解散家族操作
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 4.1 如果是执政家族，清除国家的国王和执政家族
            if (执政家族国家ID.HasValue)
            {
                using var updateCountryCommand = new MySqlCommand(
                    "UPDATE countries SET king_id = NULL, ruling_clan_id = NULL WHERE id = @country_id",
                    connection,
                    transaction
                );
                updateCountryCommand.Parameters.AddWithValue("@country_id", 执政家族国家ID.Value);
                await updateCountryCommand.ExecuteNonQueryAsync();
            }

            // 4.2 将该家族所有成员的 clan_id 设置为 NULL，清空家族贡献值，并记录族长解散家族的时间（用于冷却时间）
            using var updateMembersCommand = new MySqlCommand(
                @"UPDATE players 
                  SET clan_id = NULL,
                      clan_contribution = 0,
                      last_clan_disband_time = CASE WHEN id = @leader_id THEN NOW() ELSE last_clan_disband_time END
                  WHERE clan_id = @clan_id",
                connection,
                transaction
            );
            updateMembersCommand.Parameters.AddWithValue("@clan_id", 家族ID.Value);
            updateMembersCommand.Parameters.AddWithValue("@leader_id", 族长ID);
            await updateMembersCommand.ExecuteNonQueryAsync();

            // 4.3 删除家族成员职位表中的所有记录（外键会自动处理，但显式删除更清晰）
            using var deleteRolesCommand = new MySqlCommand(
                "DELETE FROM clan_member_roles WHERE clan_id = @clan_id",
                connection,
                transaction
            );
            deleteRolesCommand.Parameters.AddWithValue("@clan_id", 家族ID.Value);
            await deleteRolesCommand.ExecuteNonQueryAsync();

            // 4.4 在删除家族之前，先查询家族名称和族长姓名（用于日志和事件消息）
            string 家族名称 = "";
            string 族长姓名 = "";
            using var clanInfoCommand = new MySqlCommand(
                @"SELECT c.name, p.name 
                  FROM clans c
                  LEFT JOIN players p ON c.leader_id = p.id
                  WHERE c.id = @clan_id",
                connection,
                transaction
            );
            clanInfoCommand.Parameters.AddWithValue("@clan_id", 家族ID.Value);
            using var clanInfoReader = await clanInfoCommand.ExecuteReaderAsync();
            if (await clanInfoReader.ReadAsync())
            {
                家族名称 = clanInfoReader.IsDBNull(0) ? "" : clanInfoReader.GetString(0);
                族长姓名 = clanInfoReader.IsDBNull(1) ? "" : clanInfoReader.GetString(1);
            }
            clanInfoReader.Close();

            // 4.5 在删除家族之前记录日志（必须在事务中，因为需要访问clans表）
            await 记录家族日志(
                connection,
                家族ID.Value,
                "disband",
                玩家ID,
                族长姓名,
                null,
                null,
                null,
                $"{族长姓名}解散了家族"
            );

            // 4.6 删除家族记录（外键会自动处理相关数据）
            using var deleteClanCommand = new MySqlCommand(
                "DELETE FROM clans WHERE id = @clan_id",
                connection,
                transaction
            );
            deleteClanCommand.Parameters.AddWithValue("@clan_id", 家族ID.Value);
            await deleteClanCommand.ExecuteNonQueryAsync();

            // 提交事务
            await transaction.CommitAsync();

            // 通过 SignalR 广播事件：家族解散（向所有家族成员广播）
            var hubContext = app.Services.GetRequiredService<IHubContext<GameHub>>();
            var disbandEvent = new ClanDisbandedEvent
            {
                ClanId = 家族ID.Value,
                ClanName = 家族名称,
                OperatorId = 玩家ID,
                OperatorName = 族长姓名
            };
            await hubContext.Clients.Group($"clan_{家族ID.Value}").SendAsync("OnGameEvent", disbandEvent);

            // 通过自建 WebSocket 通道广播相同事件（JSON 格式）
            await WebSocketConnectionManager.BroadcastAsync(disbandEvent);

            return Results.Ok(new DisbandClanResponse(true, "家族解散成功"));
        }
        catch
        {
            // 回滚事务
            await transaction.RollbackAsync();
            throw;
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new DisbandClanResponse(false, "服务器错误: " + ex.Message));
    }
});

// =================== 家族捐献接口：POST /api/donateClan ===================

app.MapPost("/api/donateClan", async ([FromBody] DonateClanRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new DonateClanResponse(false, "账号ID无效", false));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 1. 查询玩家信息，确认玩家存在且有家族
        using var playerCommand = new MySqlCommand(
            "SELECT id, clan_id, copper_money, last_donate_date FROM players WHERE account_id = @account_id LIMIT 1",
            connection
        );
        playerCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);

        using var playerReader = await playerCommand.ExecuteReaderAsync();
        if (!await playerReader.ReadAsync())
        {
            return Results.Ok(new DonateClanResponse(false, "玩家不存在或尚未创建角色", false));
        }

        int 玩家ID = playerReader.GetInt32(0);
        int? 家族ID = playerReader.IsDBNull(1) ? null : playerReader.GetInt32(1);
        int 玩家铜钱 = playerReader.GetInt32(2);
        DateTime? 最后捐献日期 = playerReader.IsDBNull(3) ? null : playerReader.GetDateTime(3);
        playerReader.Close();

        // 检查玩家是否有家族
        if (!家族ID.HasValue || 家族ID.Value <= 0)
        {
            return Results.Ok(new DonateClanResponse(false, "捐献失败：玩家不属于任何家族", false));
        }

        // 2. 检查今日是否已捐献（0点后刷新）
        DateTime 今天 = DateTime.Today;
        if (最后捐献日期.HasValue && 最后捐献日期.Value.Date == 今天)
        {
            return Results.Ok(new DonateClanResponse(false, "今日已捐献，请明天再来！", true));
        }

        // 3. 检查玩家铜钱是否足够（需要10000铜钱）
        const int 捐献消耗铜钱 = 10000;
        if (玩家铜钱 < 捐献消耗铜钱)
        {
            return Results.Ok(new DonateClanResponse(false, "捐献失败：需要10000铜钱", false));
        }

        // 4. 开始事务：扣除玩家铜钱，增加家族资金和繁荣值，更新最后捐献日期
        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // 4.1 扣除玩家铜钱，更新最后捐献日期，增加家族贡献值（+10）
            using var updatePlayerCommand = new MySqlCommand(
                @"UPDATE players 
                  SET copper_money = copper_money - @cost_copper, 
                      last_donate_date = CURDATE(),
                      clan_contribution = clan_contribution + 10
                  WHERE id = @player_id",
                connection,
                transaction
            );
            updatePlayerCommand.Parameters.AddWithValue("@cost_copper", 捐献消耗铜钱);
            updatePlayerCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            await updatePlayerCommand.ExecuteNonQueryAsync();

            // 4.2 增加家族资金（+100）和繁荣值（+10），防止整数溢出
            // 设置合理的上限值（避免使用int.MaxValue，因为游戏数值不需要那么大）
            const int 家族资金上限 = 2000000000;  // 20亿（足够大的游戏数值上限）
            const int 家族繁荣值上限 = 2000000000;  // 20亿（足够大的游戏数值上限）
            const int 增加资金 = 100;
            const int 增加繁荣值 = 10;
            
            using var updateClanCommand = new MySqlCommand(
                @"UPDATE clans 
                  SET funds = CASE 
                      WHEN funds + @add_funds >= @max_funds THEN @max_funds 
                      ELSE funds + @add_funds 
                  END,
                  prosperity = CASE 
                      WHEN prosperity + @add_prosperity >= @max_prosperity THEN @max_prosperity 
                      ELSE prosperity + @add_prosperity 
                  END
                  WHERE id = @clan_id",
                connection,
                transaction
            );
            updateClanCommand.Parameters.AddWithValue("@clan_id", 家族ID.Value);
            updateClanCommand.Parameters.AddWithValue("@add_funds", 增加资金);
            updateClanCommand.Parameters.AddWithValue("@add_prosperity", 增加繁荣值);
            updateClanCommand.Parameters.AddWithValue("@max_funds", 家族资金上限);
            updateClanCommand.Parameters.AddWithValue("@max_prosperity", 家族繁荣值上限);
            await updateClanCommand.ExecuteNonQueryAsync();

            // 提交事务
            await transaction.CommitAsync();

            // 查询玩家姓名（用于事件消息）
            string 玩家姓名 = "";
            using var playerNameCommand = new MySqlCommand(
                "SELECT name FROM players WHERE id = @player_id",
                connection
            );
            playerNameCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            var playerNameResult = await playerNameCommand.ExecuteScalarAsync();
            if (playerNameResult != null)
            {
                玩家姓名 = playerNameResult.ToString() ?? "";
            }

            // 通过 SignalR 广播事件：家族捐献
            var hubContext = app.Services.GetRequiredService<IHubContext<GameHub>>();
            var donateEvent = new ClanDonatedEvent
            {
                ClanId = 家族ID.Value,
                PlayerId = 玩家ID,
                PlayerName = 玩家姓名,
                DonationAmount = 捐献消耗铜钱,
                FundsAdded = 100,
                ProsperityAdded = 10
            };
            await hubContext.Clients.Group($"clan_{家族ID.Value}").SendAsync("OnGameEvent", donateEvent);

            // 通过自建 WebSocket 通道广播相同事件（JSON 格式）
            await WebSocketConnectionManager.BroadcastAsync(donateEvent);

            // 记录家族日志：捐献
            string 捐献详情 = $"{{\"amount\":{捐献消耗铜钱},\"funds_added\":100,\"prosperity_added\":10}}";
            await 记录家族日志(
                connection,
                家族ID.Value,
                "donate",
                玩家ID,
                玩家姓名,
                null,
                null,
                捐献详情,
                $"{玩家姓名}捐献了{捐献消耗铜钱}铜钱，家族资金+100，繁荣值+10"
            );

            return Results.Ok(new DonateClanResponse(true, "捐献成功！家族资金+100，繁荣值+10", false));
        }
        catch
        {
            // 回滚事务
            await transaction.RollbackAsync();
            throw;
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new DonateClanResponse(false, "服务器错误: " + ex.Message, false));
    }
});

// =================== 获取家族日志接口：POST /api/getClanLogs ===================

app.MapPost("/api/getClanLogs", async ([FromBody] GetClanLogsRequest 请求) =>
{
    try
    {
        if (请求.ClanId <= 0)
        {
            return Results.Ok(new GetClanLogsResponse(false, "家族ID无效", new List<ClanLogData>()));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 查询家族日志，按时间倒序排列（最新的在前）
        using var command = new MySqlCommand(
            @"SELECT id, clan_id, operation_type, operator_id, operator_name, 
                     target_player_id, target_player_name, details, description, created_at
              FROM clan_logs 
              WHERE clan_id = @clan_id 
              ORDER BY created_at DESC 
              LIMIT 300",
            connection
        );
        command.Parameters.AddWithValue("@clan_id", 请求.ClanId);

        var 日志列表 = new List<ClanLogData>();
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            日志列表.Add(new ClanLogData
            {
                Id = reader.GetInt32(0),
                ClanId = reader.GetInt32(1),
                OperationType = reader.GetString(2),
                OperatorId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                OperatorName = reader.IsDBNull(4) ? null : reader.GetString(4),
                TargetPlayerId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                TargetPlayerName = reader.IsDBNull(6) ? null : reader.GetString(6),
                Details = reader.IsDBNull(7) ? null : reader.GetString(7),
                Description = reader.GetString(8),
                CreatedAt = reader.GetDateTime(9)
            });
        }

        return Results.Ok(new GetClanLogsResponse(true, "获取成功", 日志列表));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetClanLogsResponse(false, "服务器错误: " + ex.Message, new List<ClanLogData>()));
    }
});

// =================== 检查今日是否已捐献接口：POST /api/checkDonateStatus ===================

app.MapPost("/api/checkDonateStatus", async ([FromBody] CheckDonateStatusRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new CheckDonateStatusResponse(false, "账号ID无效", false));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 先检查玩家是否存在
        using var checkPlayerCommand = new MySqlCommand(
            "SELECT id FROM players WHERE account_id = @account_id LIMIT 1",
            connection
        );
        checkPlayerCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);
        
        var playerExists = await checkPlayerCommand.ExecuteScalarAsync();
        if (playerExists == null)
        {
            return Results.Ok(new CheckDonateStatusResponse(false, "玩家不存在", false));
        }

        // 查询玩家最后捐献日期
        using var command = new MySqlCommand(
            "SELECT last_donate_date FROM players WHERE account_id = @account_id LIMIT 1",
            connection
        );
        command.Parameters.AddWithValue("@account_id", 请求.AccountId);

        var result = await command.ExecuteScalarAsync();
        
        // 如果result是DBNull.Value，说明玩家从未捐献过（last_donate_date为NULL）
        DateTime? 最后捐献日期 = (result == null || result == DBNull.Value) ? null : (DateTime?)result;
        DateTime 今天 = DateTime.Today;
        
        // 如果最后捐献日期是今天，说明今日已捐献
        bool 今日已捐献 = 最后捐献日期.HasValue && 最后捐献日期.Value.Date == 今天;

        return Results.Ok(new CheckDonateStatusResponse(true, 今日已捐献 ? "今日已捐献" : "今日未捐献", 今日已捐献));
    }
    catch (Exception ex)
    {
        return Results.Ok(new CheckDonateStatusResponse(false, "服务器错误: " + ex.Message, false));
    }
});

// =================== 加入家族接口：POST /api/joinClan ===================

app.MapPost("/api/joinClan", async ([FromBody] JoinClanRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new JoinClanResponse(false, "账号ID无效"));
        }

        if (请求.ClanId <= 0)
        {
            return Results.Ok(new JoinClanResponse(false, "家族ID无效"));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 1. 查询玩家信息，确认玩家存在且没有家族
        using var playerCommand = new MySqlCommand(
            "SELECT id, clan_id FROM players WHERE account_id = @account_id LIMIT 1",
            connection
        );
        playerCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);

        using var playerReader = await playerCommand.ExecuteReaderAsync();
        if (!await playerReader.ReadAsync())
        {
            return Results.Ok(new JoinClanResponse(false, "玩家不存在或尚未创建角色"));
        }

        int 玩家ID = playerReader.GetInt32(0);
        int? 当前家族ID = playerReader.IsDBNull(1) ? null : playerReader.GetInt32(1);
        playerReader.Close();

        // 检查玩家是否已经有家族
        if (当前家族ID.HasValue && 当前家族ID.Value > 0)
        {
            return Results.Ok(new JoinClanResponse(false, "加入家族失败：玩家已经属于某个家族"));
        }

        // 2. 查询家族信息，检查家族是否存在和人数是否已满
        using var clanCommand = new MySqlCommand(
            "SELECT id, level, country_id FROM clans WHERE id = @clan_id",
            connection
        );
        clanCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);

        using var clanReader = await clanCommand.ExecuteReaderAsync();
        if (!await clanReader.ReadAsync())
        {
            return Results.Ok(new JoinClanResponse(false, "加入家族失败：家族不存在"));
        }

        int 家族ID = clanReader.GetInt32(0);
        int 家族等级 = clanReader.GetInt32(1);
        int? 家族国家ID = clanReader.IsDBNull(2) ? null : clanReader.GetInt32(2);
        clanReader.Close();

        // 3. 计算家族人数上限（1级10人，每级+10人，最高5级50人）
        int 人数上限 = 10 + (家族等级 - 1) * 10;
        if (人数上限 > 50) 人数上限 = 50;

        // 4. 查询当前家族成员数
        using var memberCountCommand = new MySqlCommand(
            "SELECT COUNT(*) FROM players WHERE clan_id = @clan_id",
            connection
        );
        memberCountCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
        var memberCountResult = await memberCountCommand.ExecuteScalarAsync();
        int 当前成员数 = memberCountResult != null ? Convert.ToInt32(memberCountResult) : 0;

        // 5. 检查人数是否已满
        if (当前成员数 >= 人数上限)
        {
            return Results.Ok(new JoinClanResponse(false, $"加入家族失败：家族人数已满（{当前成员数}/{人数上限}）"));
        }

        // 6. 开始事务：更新玩家的家族ID，添加成员职位记录
        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // 6.1 更新玩家的家族ID
            using var updatePlayerCommand = new MySqlCommand(
                "UPDATE players SET clan_id = @clan_id WHERE id = @player_id",
                connection,
                transaction
            );
            updatePlayerCommand.Parameters.AddWithValue("@clan_id", 家族ID);
            updatePlayerCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            await updatePlayerCommand.ExecuteNonQueryAsync();

            // 6.2 在家族成员职位表中添加成员职位记录（默认是"成员"）
            using var insertRoleCommand = new MySqlCommand(
                "INSERT INTO clan_member_roles (clan_id, player_id, role) VALUES (@clan_id, @player_id, 'member')",
                connection,
                transaction
            );
            insertRoleCommand.Parameters.AddWithValue("@clan_id", 家族ID);
            insertRoleCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            await insertRoleCommand.ExecuteNonQueryAsync();

            // 提交事务
            await transaction.CommitAsync();

            // 查询玩家姓名（用于事件消息）
            string 玩家姓名 = "";
            using var playerNameCommand = new MySqlCommand(
                "SELECT name FROM players WHERE id = @player_id",
                connection
            );
            playerNameCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            var playerNameResult = await playerNameCommand.ExecuteScalarAsync();
            if (playerNameResult != null)
            {
                玩家姓名 = playerNameResult.ToString() ?? "";
            }

            // 通过 SignalR 广播事件：成员加入家族
            var hubContext = app.Services.GetRequiredService<IHubContext<GameHub>>();
            var joinEvent = new ClanMemberJoinedEvent
            {
                ClanId = 家族ID,
                PlayerId = 玩家ID,
                PlayerName = 玩家姓名
            };
            await hubContext.Clients.Group($"clan_{家族ID}").SendAsync("OnGameEvent", joinEvent);

            // 通过自建 WebSocket 通道广播相同事件（JSON 格式）
            await WebSocketConnectionManager.BroadcastAsync(joinEvent);

            // 记录家族日志：加入家族
            await 记录家族日志(
                connection,
                家族ID,
                "join",
                玩家ID,
                玩家姓名,
                null,
                null,
                null,
                $"{玩家姓名}加入了家族"
            );

            return Results.Ok(new JoinClanResponse(true, "加入家族成功！"));
        }
        catch
        {
            // 回滚事务
            await transaction.RollbackAsync();
            throw;
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new JoinClanResponse(false, "服务器错误: " + ex.Message));
    }
});

// =================== 退出家族接口：POST /api/leaveClan ===================

app.MapPost("/api/leaveClan", async ([FromBody] LeaveClanRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new LeaveClanResponse(false, "账号ID无效"));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 1. 查询玩家信息，确认玩家存在且有家族
        using var playerCommand = new MySqlCommand(
            "SELECT id, clan_id FROM players WHERE account_id = @account_id LIMIT 1",
            connection
        );
        playerCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);

        using var playerReader = await playerCommand.ExecuteReaderAsync();
        if (!await playerReader.ReadAsync())
        {
            return Results.Ok(new LeaveClanResponse(false, "玩家不存在或尚未创建角色"));
        }

        int 玩家ID = playerReader.GetInt32(0);
        int? 家族ID = playerReader.IsDBNull(1) ? null : playerReader.GetInt32(1);
        playerReader.Close();

        // 检查玩家是否有家族
        if (!家族ID.HasValue || 家族ID.Value <= 0)
        {
            return Results.Ok(new LeaveClanResponse(false, "退出家族失败：玩家不属于任何家族"));
        }

        // 2. 查询家族信息，确认玩家不是族长（族长不能退出，只能解散）
        using var clanCommand = new MySqlCommand(
            "SELECT leader_id FROM clans WHERE id = @clan_id",
            connection
        );
        clanCommand.Parameters.AddWithValue("@clan_id", 家族ID.Value);

        using var clanReader = await clanCommand.ExecuteReaderAsync();
        if (!await clanReader.ReadAsync())
        {
            return Results.Ok(new LeaveClanResponse(false, "退出家族失败：家族不存在"));
        }

        int 族长ID = clanReader.GetInt32(0);
        clanReader.Close();

        // 检查玩家是否是族长
        if (玩家ID == 族长ID)
        {
            return Results.Ok(new LeaveClanResponse(false, "退出家族失败：族长不能退出家族，请使用解散家族功能"));
        }

        // 3. 开始事务：更新玩家的家族ID为NULL，删除成员职位记录，更新退出家族时间
        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // 3.1 更新玩家的家族ID为NULL，清空家族贡献值，并记录退出家族时间
            using var updatePlayerCommand = new MySqlCommand(
                @"UPDATE players 
                  SET clan_id = NULL, 
                      clan_contribution = 0,
                      last_clan_leave_time = NOW() 
                  WHERE id = @player_id",
                connection,
                transaction
            );
            updatePlayerCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            await updatePlayerCommand.ExecuteNonQueryAsync();

            // 3.2 删除家族成员职位记录
            using var deleteRoleCommand = new MySqlCommand(
                "DELETE FROM clan_member_roles WHERE clan_id = @clan_id AND player_id = @player_id",
                connection,
                transaction
            );
            deleteRoleCommand.Parameters.AddWithValue("@clan_id", 家族ID.Value);
            deleteRoleCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            await deleteRoleCommand.ExecuteNonQueryAsync();

            // 提交事务
            await transaction.CommitAsync();

            // 查询玩家姓名（用于事件消息）
            string 玩家姓名 = "";
            using var playerNameCommand = new MySqlCommand(
                "SELECT name FROM players WHERE id = @player_id",
                connection
            );
            playerNameCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            var playerNameResult = await playerNameCommand.ExecuteScalarAsync();
            if (playerNameResult != null)
            {
                玩家姓名 = playerNameResult.ToString() ?? "";
            }

            // 通过 SignalR 广播事件：成员离开家族
            var hubContext = app.Services.GetRequiredService<IHubContext<GameHub>>();
            var leaveEvent = new ClanMemberLeftEvent
            {
                ClanId = 家族ID.Value,
                PlayerId = 玩家ID,
                PlayerName = 玩家姓名
            };
            await hubContext.Clients.Group($"clan_{家族ID.Value}").SendAsync("OnGameEvent", leaveEvent);

            // 通过自建 WebSocket 通道广播相同事件（JSON 格式）
            await WebSocketConnectionManager.BroadcastAsync(leaveEvent);

            // 记录家族日志：离开家族
            await 记录家族日志(
                connection,
                家族ID.Value,
                "leave",
                玩家ID,
                玩家姓名,
                null,
                null,
                null,
                $"{玩家姓名}离开了家族"
            );

            return Results.Ok(new LeaveClanResponse(true, "退出家族成功！"));
        }
        catch
        {
            // 回滚事务
            await transaction.RollbackAsync();
            throw;
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new LeaveClanResponse(false, "服务器错误: " + ex.Message));
    }
});

// =================== 踢出家族成员接口：POST /api/kickClanMember ===================

app.MapPost("/api/kickClanMember", async ([FromBody] KickClanMemberRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new KickClanMemberResponse(false, "账号ID无效"));
        }

        if (请求.TargetPlayerId <= 0)
        {
            return Results.Ok(new KickClanMemberResponse(false, "目标玩家ID无效"));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 1. 验证操作者是否是族长或副族长
        using var operatorCommand = new MySqlCommand(
            @"SELECT p.id, p.clan_id, c.leader_id
              FROM players p
              LEFT JOIN clans c ON p.clan_id = c.id
              WHERE p.account_id = @account_id",
            connection
        );
        operatorCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);
        
        using var operatorReader = await operatorCommand.ExecuteReaderAsync();
        if (!await operatorReader.ReadAsync())
        {
            return Results.Ok(new KickClanMemberResponse(false, "操作者不存在"));
        }
        
        int 操作者ID = operatorReader.GetInt32(0);
        int? 操作者家族ID = operatorReader.IsDBNull(1) ? null : operatorReader.GetInt32(1);
        int 族长ID = operatorReader.IsDBNull(2) ? -1 : operatorReader.GetInt32(2);
        operatorReader.Close();
        
        if (!操作者家族ID.HasValue || 操作者家族ID.Value <= 0)
        {
            return Results.Ok(new KickClanMemberResponse(false, "操作者不属于任何家族"));
        }

        // 检查操作者是否是族长
        bool 是族长 = 操作者ID == 族长ID;
        
        // 检查操作者是否是副族长
        bool 是副族长 = false;
        if (!是族长)
        {
            using var deputyCommand = new MySqlCommand(
                "SELECT COUNT(*) FROM clan_member_roles WHERE clan_id = @clan_id AND player_id = @player_id AND role = '副族长'",
                connection
            );
            deputyCommand.Parameters.AddWithValue("@clan_id", 操作者家族ID.Value);
            deputyCommand.Parameters.AddWithValue("@player_id", 操作者ID);
            var deputyResult = await deputyCommand.ExecuteScalarAsync();
            是副族长 = deputyResult != null && Convert.ToInt32(deputyResult) > 0;
        }

        if (!是族长 && !是副族长)
        {
            return Results.Ok(new KickClanMemberResponse(false, "只有族长或副族长可以踢出成员"));
        }

        // 2. 验证目标玩家是否属于该家族
        using var targetCommand = new MySqlCommand(
            "SELECT clan_id FROM players WHERE id = @player_id",
            connection
        );
        targetCommand.Parameters.AddWithValue("@player_id", 请求.TargetPlayerId);
        
        var targetClanResult = await targetCommand.ExecuteScalarAsync();
        if (targetClanResult == null || DBNull.Value.Equals(targetClanResult))
        {
            return Results.Ok(new KickClanMemberResponse(false, "目标玩家不存在"));
        }
        
        int 目标玩家家族ID = Convert.ToInt32(targetClanResult);
        if (目标玩家家族ID != 操作者家族ID.Value)
        {
            return Results.Ok(new KickClanMemberResponse(false, "目标玩家不属于该家族"));
        }

        // 3. 检查目标玩家是否是族长（族长不能踢自己，也不能被踢）
        if (请求.TargetPlayerId == 族长ID)
        {
            if (请求.TargetPlayerId == 操作者ID)
            {
                return Results.Ok(new KickClanMemberResponse(false, "不能踢出自己"));
            }
            return Results.Ok(new KickClanMemberResponse(false, "不能踢出族长"));
        }

        // 4. 查询目标玩家的职位
        using var targetRoleCommand = new MySqlCommand(
            "SELECT role FROM clan_member_roles WHERE clan_id = @clan_id AND player_id = @player_id",
            connection
        );
        targetRoleCommand.Parameters.AddWithValue("@clan_id", 操作者家族ID.Value);
        targetRoleCommand.Parameters.AddWithValue("@player_id", 请求.TargetPlayerId);
        
        var targetRoleResult = await targetRoleCommand.ExecuteScalarAsync();
        string 目标玩家职位 = "";
        if (targetRoleResult != null && !DBNull.Value.Equals(targetRoleResult))
        {
            目标玩家职位 = targetRoleResult.ToString() ?? "";
        }

        // 5. 权限检查：根据操作者职位决定可以踢出谁
        if (是副族长)
        {
            // 副族长不能踢出族长和副族长
            if (目标玩家职位 == "副族长")
            {
                return Results.Ok(new KickClanMemberResponse(false, "副族长不能踢出副族长"));
            }
            // 副族长可以踢出精英和成员
        }
        else if (是族长)
        {
            // 族长可以踢出除自己外的所有人（包括副族长、精英、成员）
            // 已经在上面检查了不能踢自己（族长ID检查）
            // 所以这里不需要额外检查
        }

        // 4. 开始事务：踢出目标玩家
        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // 4.1 将目标玩家的 clan_id 设置为 NULL，清空家族贡献值
            using var updatePlayerCommand = new MySqlCommand(
                @"UPDATE players 
                  SET clan_id = NULL,
                      clan_contribution = 0
                  WHERE id = @player_id",
                connection,
                transaction
            );
            updatePlayerCommand.Parameters.AddWithValue("@player_id", 请求.TargetPlayerId);
            await updatePlayerCommand.ExecuteNonQueryAsync();

            // 4.2 删除目标玩家在家族成员职位表中的记录
            using var deleteRoleCommand = new MySqlCommand(
                "DELETE FROM clan_member_roles WHERE clan_id = @clan_id AND player_id = @player_id",
                connection,
                transaction
            );
            deleteRoleCommand.Parameters.AddWithValue("@clan_id", 操作者家族ID.Value);
            deleteRoleCommand.Parameters.AddWithValue("@player_id", 请求.TargetPlayerId);
            await deleteRoleCommand.ExecuteNonQueryAsync();

            // 提交事务
            await transaction.CommitAsync();

            // 查询目标玩家和操作者的姓名（用于事件消息）
            string 目标玩家姓名 = "";
            string 操作者姓名 = "";
            using var nameCommand = new MySqlCommand(
                @"SELECT 
                    (SELECT name FROM players WHERE id = @target_id) as target_name,
                    (SELECT name FROM players WHERE id = @operator_id) as operator_name",
                connection
            );
            nameCommand.Parameters.AddWithValue("@target_id", 请求.TargetPlayerId);
            nameCommand.Parameters.AddWithValue("@operator_id", 操作者ID);
            using var nameReader = await nameCommand.ExecuteReaderAsync();
            if (await nameReader.ReadAsync())
            {
                目标玩家姓名 = nameReader.IsDBNull(0) ? "" : nameReader.GetString(0);
                操作者姓名 = nameReader.IsDBNull(1) ? "" : nameReader.GetString(1);
            }
            nameReader.Close();

            // 通过 SignalR 广播事件：成员被踢出家族
            var hubContext = app.Services.GetRequiredService<IHubContext<GameHub>>();
            var kickEvent = new ClanMemberKickedEvent
            {
                ClanId = 操作者家族ID.Value,
                KickedPlayerId = 请求.TargetPlayerId,
                KickedPlayerName = 目标玩家姓名,
                OperatorId = 操作者ID,
                OperatorName = 操作者姓名
            };
            await hubContext.Clients.Group($"clan_{操作者家族ID.Value}").SendAsync("OnGameEvent", kickEvent);

            // 通过自建 WebSocket 通道广播相同事件（JSON 格式）
            await WebSocketConnectionManager.BroadcastAsync(kickEvent);

            // 记录家族日志：踢出成员
            await 记录家族日志(
                connection,
                操作者家族ID.Value,
                "kick",
                操作者ID,
                操作者姓名,
                请求.TargetPlayerId,
                目标玩家姓名,
                null,
                $"{操作者姓名}将{目标玩家姓名}踢出家族"
            );

            // 发送系统消息给被踢出的玩家
            var systemMessage = new 待处理消息
            {
                优先级 = 0, // 系统消息优先级最高
                频道类型 = "system",
                消息内容 = $"你被{操作者姓名}踢出了家族！",
                目标玩家ID = 请求.TargetPlayerId
            };
            消息队列.Enqueue(systemMessage);

            return Results.Ok(new KickClanMemberResponse(true, "成功踢出家族成员"));
        }
        catch
        {
            // 回滚事务
            await transaction.RollbackAsync();
            throw;
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new KickClanMemberResponse(false, "服务器错误: " + ex.Message));
    }
});

// =================== 获取家族成员列表接口：POST /api/getClanMembers ===================

app.MapPost("/api/getClanMembers", async ([FromBody] GetClanMembersRequest 请求) =>
{
    try
    {
        if (请求.ClanId <= 0)
        {
            return Results.Ok(new GetClanMembersResponse(false, "家族ID无效", new List<PlayerSummary>()));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 查询家族族长ID
        int 族长ID = -1;
        using var leaderCommand = new MySqlCommand(
            "SELECT leader_id FROM clans WHERE id = @clan_id",
            connection
        );
        leaderCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
        var leaderResult = await leaderCommand.ExecuteScalarAsync();
        if (leaderResult != null)
        {
            族长ID = Convert.ToInt32(leaderResult);
        }

        // 查询指定家族的所有成员，按家族贡献值降序排序（相同贡献值按ID升序）
        // 同时查询每个成员的职位信息
        string sql = @"
            SELECT 
                p.id, p.name, p.gender, p.level, p.title_name, p.office,
                p.copper_money, p.gold, p.clan_contribution,
                pa.max_hp, pa.current_hp, pa.attack, pa.defense, pa.crit_rate,
                COALESCE(cmr.role, 'member') as clan_role
            FROM players p
            LEFT JOIN player_attributes pa ON p.id = pa.player_id
            LEFT JOIN clan_member_roles cmr ON p.id = cmr.player_id AND cmr.clan_id = @clan_id
            WHERE p.clan_id = @clan_id
            ORDER BY p.clan_contribution DESC, p.id ASC";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@clan_id", 请求.ClanId);

        using var reader = await command.ExecuteReaderAsync();

        var 成员列表 = new List<PlayerSummary>();
        while (await reader.ReadAsync())
        {
            int 玩家ID = reader.GetInt32(0);
            string 数据库职位 = reader.IsDBNull(14) ? "member" : reader.GetString(14);
            
            // 如果是族长，职位设为"族长"
            string 玩家职位 = 玩家ID == 族长ID ? "族长" : 数据库职位;
            
            // 将数据库职位转换为中文
            if (玩家职位 == "member")
            {
                玩家职位 = "成员";
            }
            else if (玩家职位 == "leader")
            {
                玩家职位 = "族长";
            }
            // "副族长"和"精英"保持不变
            
            var 成员 = new PlayerSummary
            {
                Id = 玩家ID,
                Name = reader.GetString(1),
                Gender = reader.GetString(2),
                Level = reader.GetInt32(3),
                TitleName = reader.GetString(4),
                Office = reader.GetString(5),
                CopperMoney = reader.GetInt32(6),
                Gold = reader.GetInt32(7),
                ClanContribution = reader.GetInt32(8),
                ClanRole = 玩家职位,
                Attributes = new PlayerAttributesData
                {
                    MaxHp = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                    CurrentHp = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                    Attack = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                    Defense = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                    CritRate = reader.IsDBNull(13) ? 0f : reader.GetFloat(13)
                }
            };
            成员列表.Add(成员);
        }

        return Results.Ok(new GetClanMembersResponse(true, "获取成功", 成员列表));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetClanMembersResponse(false, "服务器错误: " + ex.Message, new List<PlayerSummary>()));
    }
});

// =================== 获取家族职位列表接口：POST /api/getClanRoles ===================

app.MapPost("/api/getClanRoles", async ([FromBody] GetClanRolesRequest 请求) =>
{
    try
    {
        if (请求.ClanId <= 0)
        {
            return Results.Ok(new GetClanRolesResponse(false, "家族ID无效", null));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 1. 查询家族等级
        using var clanCommand = new MySqlCommand(
            "SELECT level FROM clans WHERE id = @clan_id",
            connection
        );
        clanCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
        
        var levelResult = await clanCommand.ExecuteScalarAsync();
        if (levelResult == null)
        {
            return Results.Ok(new GetClanRolesResponse(false, "家族不存在", null));
        }
        
        int 家族等级 = Convert.ToInt32(levelResult);

        // 2. 根据家族等级计算职位数量
        int 副族长数量 = 0;
        int 精英数量 = 0;
        
        switch (家族等级)
        {
            case 1: 副族长数量 = 1; 精英数量 = 2; break;
            case 2: 副族长数量 = 1; 精英数量 = 3; break;
            case 3: 副族长数量 = 2; 精英数量 = 4; break;
            case 4: 副族长数量 = 2; 精英数量 = 5; break;
            case 5: 副族长数量 = 3; 精英数量 = 6; break;
            default: 副族长数量 = 1; 精英数量 = 2; break;
        }

        // 3. 查询当前家族的职位信息
        using var rolesCommand = new MySqlCommand(
            @"SELECT cmr.role, cmr.player_id, p.name as player_name
              FROM clan_member_roles cmr
              LEFT JOIN players p ON cmr.player_id = p.id
              WHERE cmr.clan_id = @clan_id AND cmr.role IN ('副族长', '精英')
              ORDER BY cmr.role, cmr.player_id",
            connection
        );
        rolesCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
        
        var 职位字典 = new Dictionary<string, List<ClanRoleInfo>>();
        职位字典["副族长"] = new List<ClanRoleInfo>();
        职位字典["精英"] = new List<ClanRoleInfo>();
        
        using var rolesReader = await rolesCommand.ExecuteReaderAsync();
        while (await rolesReader.ReadAsync())
        {
            string 职位 = rolesReader.GetString(0);
            int 玩家ID = rolesReader.GetInt32(1);
            string 玩家姓名 = rolesReader.IsDBNull(2) ? "" : rolesReader.GetString(2);
            
            if (职位字典.ContainsKey(职位))
            {
                职位字典[职位].Add(new ClanRoleInfo
                {
                    PlayerId = 玩家ID,
                    PlayerName = 玩家姓名
                });
            }
        }
        rolesReader.Close();

        // 4. 构建职位列表
        var 职位列表 = new List<ClanRoleSlot>();
        
        // 添加副族长职位
        for (int i = 0; i < 副族长数量; i++)
        {
            if (i < 职位字典["副族长"].Count)
            {
                职位列表.Add(new ClanRoleSlot
                {
                    Role = "副族长",
                    SlotIndex = i,
                    PlayerId = 职位字典["副族长"][i].PlayerId,
                    PlayerName = 职位字典["副族长"][i].PlayerName,
                    IsOccupied = true
                });
            }
            else
            {
                职位列表.Add(new ClanRoleSlot
                {
                    Role = "副族长",
                    SlotIndex = i,
                    PlayerId = 0,
                    PlayerName = "无",
                    IsOccupied = false
                });
            }
        }
        
        // 添加精英职位
        for (int i = 0; i < 精英数量; i++)
        {
            if (i < 职位字典["精英"].Count)
            {
                职位列表.Add(new ClanRoleSlot
                {
                    Role = "精英",
                    SlotIndex = i,
                    PlayerId = 职位字典["精英"][i].PlayerId,
                    PlayerName = 职位字典["精英"][i].PlayerName,
                    IsOccupied = true
                });
            }
            else
            {
                职位列表.Add(new ClanRoleSlot
                {
                    Role = "精英",
                    SlotIndex = i,
                    PlayerId = 0,
                    PlayerName = "无",
                    IsOccupied = false
                });
            }
        }

        var 响应数据 = new ClanRolesData
        {
            ClanId = 请求.ClanId,
            ClanLevel = 家族等级,
            Roles = 职位列表
        };

        return Results.Ok(new GetClanRolesResponse(true, "获取成功", 响应数据));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetClanRolesResponse(false, "服务器错误: " + ex.Message, null));
    }
});

// =================== 任命家族职位接口：POST /api/appointClanRole ===================

app.MapPost("/api/appointClanRole", async ([FromBody] AppointClanRoleRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new AppointClanRoleResponse(false, "账号ID无效"));
        }

        if (请求.ClanId <= 0 || 请求.PlayerId <= 0)
        {
            return Results.Ok(new AppointClanRoleResponse(false, "家族ID或玩家ID无效"));
        }

        if (string.IsNullOrWhiteSpace(请求.Role) || (请求.Role != "副族长" && 请求.Role != "精英"))
        {
            return Results.Ok(new AppointClanRoleResponse(false, "职位类型无效，只能是'副族长'或'精英'"));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 1. 验证操作者是否是族长
        using var leaderCommand = new MySqlCommand(
            @"SELECT p.id, c.leader_id 
              FROM players p
              LEFT JOIN clans c ON p.clan_id = c.id
              WHERE p.account_id = @account_id",
            connection
        );
        leaderCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);
        
        using var leaderReader = await leaderCommand.ExecuteReaderAsync();
        if (!await leaderReader.ReadAsync())
        {
            return Results.Ok(new AppointClanRoleResponse(false, "玩家不存在"));
        }
        
        int 操作者ID = leaderReader.GetInt32(0);
        int 族长ID = leaderReader.IsDBNull(1) ? -1 : leaderReader.GetInt32(1);
        leaderReader.Close();
        
        if (族长ID != 操作者ID)
        {
            return Results.Ok(new AppointClanRoleResponse(false, "只有族长可以任命职位"));
        }

        // 2. 验证目标玩家是否属于该家族
        using var playerCommand = new MySqlCommand(
            "SELECT clan_id FROM players WHERE id = @player_id",
            connection
        );
        playerCommand.Parameters.AddWithValue("@player_id", 请求.PlayerId);
        
        var playerClanResult = await playerCommand.ExecuteScalarAsync();
        if (playerClanResult == null || DBNull.Value.Equals(playerClanResult))
        {
            return Results.Ok(new AppointClanRoleResponse(false, "目标玩家不存在"));
        }
        
        int 目标玩家家族ID = Convert.ToInt32(playerClanResult);
        if (目标玩家家族ID != 请求.ClanId)
        {
            return Results.Ok(new AppointClanRoleResponse(false, "目标玩家不属于该家族"));
        }

        // 3. 检查目标玩家是否是族长（族长不能任命自己为其他职位）
        if (请求.PlayerId == 族长ID)
        {
            return Results.Ok(new AppointClanRoleResponse(false, "族长不能任命自己为其他职位"));
        }

        // 4. 查询家族等级，验证职位数量限制
        using var levelCommand = new MySqlCommand(
            "SELECT level FROM clans WHERE id = @clan_id",
            connection
        );
        levelCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
        
        var levelResult = await levelCommand.ExecuteScalarAsync();
        if (levelResult == null)
        {
            return Results.Ok(new AppointClanRoleResponse(false, "家族不存在"));
        }
        
        int 家族等级 = Convert.ToInt32(levelResult);
        
        // 计算该职位允许的最大数量
        int 最大数量 = 0;
        if (请求.Role == "副族长")
        {
            switch (家族等级)
            {
                case 1: case 2: 最大数量 = 1; break;
                case 3: case 4: 最大数量 = 2; break;
                case 5: 最大数量 = 3; break;
            }
        }
        else if (请求.Role == "精英")
        {
            switch (家族等级)
            {
                case 1: 最大数量 = 2; break;
                case 2: 最大数量 = 3; break;
                case 3: 最大数量 = 4; break;
                case 4: 最大数量 = 5; break;
                case 5: 最大数量 = 6; break;
            }
        }

        // 5. 查询当前该职位已任命的玩家列表（用于顶替）
        using var currentRolePlayersCommand = new MySqlCommand(
            "SELECT player_id FROM clan_member_roles WHERE clan_id = @clan_id AND role = @role ORDER BY player_id",
            connection
        );
        currentRolePlayersCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
        currentRolePlayersCommand.Parameters.AddWithValue("@role", 请求.Role);
        
        var 当前职位玩家列表 = new List<int>();
        using var currentRolePlayersReader = await currentRolePlayersCommand.ExecuteReaderAsync();
        while (await currentRolePlayersReader.ReadAsync())
        {
            当前职位玩家列表.Add(currentRolePlayersReader.GetInt32(0));
        }
        currentRolePlayersReader.Close();
        
        // 6. 检查目标玩家是否已有该职位
        if (当前职位玩家列表.Contains(请求.PlayerId))
        {
            return Results.Ok(new AppointClanRoleResponse(false, $"该玩家已经是{请求.Role}"));
        }

        // 7. 检查目标玩家是否已有其他职位（副族长或精英）
        using var existingRoleCommand = new MySqlCommand(
            "SELECT role FROM clan_member_roles WHERE clan_id = @clan_id AND player_id = @player_id AND role IN ('副族长', '精英')",
            connection
        );
        existingRoleCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
        existingRoleCommand.Parameters.AddWithValue("@player_id", 请求.PlayerId);
        
        var existingRoleResult = await existingRoleCommand.ExecuteScalarAsync();
        if (existingRoleResult != null && existingRoleResult != DBNull.Value)
        {
            string 现有职位 = existingRoleResult.ToString() ?? "";
            // 如果目标玩家已有其他职位，需要先撤销（在事务中处理）
        }

        // 被顶替玩家ID（如果发生顶替，在事务中赋值）
        int? 被顶替玩家ID = null;

        // 8. 开始事务：处理职位任命和顶替
        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // 8.1 如果目标玩家已有其他职位（副族长或精英），先删除
            using var deleteOldRoleCommand = new MySqlCommand(
                "DELETE FROM clan_member_roles WHERE clan_id = @clan_id AND player_id = @player_id AND role IN ('副族长', '精英')",
                connection,
                transaction
            );
            deleteOldRoleCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
            deleteOldRoleCommand.Parameters.AddWithValue("@player_id", 请求.PlayerId);
            await deleteOldRoleCommand.ExecuteNonQueryAsync();

            // 8.2 如果该职位已满，需要顶替：将第一个玩家降为族员
            if (当前职位玩家列表.Count >= 最大数量)
            {
                // 将第一个玩家降为族员
                被顶替玩家ID = 当前职位玩家列表[0];
                
                // 删除原职位
                using var deleteReplacedCommand = new MySqlCommand(
                    "DELETE FROM clan_member_roles WHERE clan_id = @clan_id AND player_id = @player_id AND role = @role",
                    connection,
                    transaction
                );
                deleteReplacedCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
                deleteReplacedCommand.Parameters.AddWithValue("@player_id", 被顶替玩家ID);
                deleteReplacedCommand.Parameters.AddWithValue("@role", 请求.Role);
                await deleteReplacedCommand.ExecuteNonQueryAsync();

                // 将被顶替的玩家设置为族员（如果还没有记录，则插入；如果有记录，则更新）
                using var setMemberCommand = new MySqlCommand(
                    @"INSERT INTO clan_member_roles (clan_id, player_id, role) 
                      VALUES (@clan_id, @player_id, 'member')
                      ON DUPLICATE KEY UPDATE role = 'member'",
                    connection,
                    transaction
                );
                setMemberCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
                setMemberCommand.Parameters.AddWithValue("@player_id", 被顶替玩家ID);
                await setMemberCommand.ExecuteNonQueryAsync();
            }

            // 8.3 删除目标玩家的"成员"职位（如果存在）
            using var deleteMemberCommand = new MySqlCommand(
                "DELETE FROM clan_member_roles WHERE clan_id = @clan_id AND player_id = @player_id AND role = 'member'",
                connection,
                transaction
            );
            deleteMemberCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
            deleteMemberCommand.Parameters.AddWithValue("@player_id", 请求.PlayerId);
            await deleteMemberCommand.ExecuteNonQueryAsync();

            // 8.4 任命目标玩家为新职位
            using var insertCommand = new MySqlCommand(
                @"INSERT INTO clan_member_roles (clan_id, player_id, role) 
                  VALUES (@clan_id, @player_id, @role)
                  ON DUPLICATE KEY UPDATE role = @role",
                connection,
                transaction
            );
            insertCommand.Parameters.AddWithValue("@clan_id", 请求.ClanId);
            insertCommand.Parameters.AddWithValue("@player_id", 请求.PlayerId);
            insertCommand.Parameters.AddWithValue("@role", 请求.Role);
            await insertCommand.ExecuteNonQueryAsync();

            // 提交事务
            await transaction.CommitAsync();

            // 查询玩家姓名（用于事件消息）
            string 目标玩家姓名 = "";
            string 操作者姓名 = "";
            string 被顶替玩家姓名 = "";
            
            using var nameCommand = new MySqlCommand(
                @"SELECT 
                    (SELECT name FROM players WHERE id = @target_id) as target_name,
                    (SELECT name FROM players WHERE id = @operator_id) as operator_name" +
                    (被顶替玩家ID.HasValue ? ", (SELECT name FROM players WHERE id = @replaced_id) as replaced_name" : ""),
                connection
            );
            nameCommand.Parameters.AddWithValue("@target_id", 请求.PlayerId);
            nameCommand.Parameters.AddWithValue("@operator_id", 操作者ID);
            if (被顶替玩家ID.HasValue)
            {
                nameCommand.Parameters.AddWithValue("@replaced_id", 被顶替玩家ID.Value);
            }
            using var nameReader = await nameCommand.ExecuteReaderAsync();
            if (await nameReader.ReadAsync())
            {
                目标玩家姓名 = nameReader.IsDBNull(0) ? "" : nameReader.GetString(0);
                操作者姓名 = nameReader.IsDBNull(1) ? "" : nameReader.GetString(1);
                if (被顶替玩家ID.HasValue && nameReader.FieldCount > 2)
                {
                    被顶替玩家姓名 = nameReader.IsDBNull(2) ? "" : nameReader.GetString(2);
                }
            }
            nameReader.Close();

            // 通过 SignalR 广播事件：家族职位任命
            var hubContext = app.Services.GetRequiredService<IHubContext<GameHub>>();
            var appointEvent = new ClanRoleAppointedEvent
            {
                ClanId = 请求.ClanId,
                PlayerId = 请求.PlayerId,
                PlayerName = 目标玩家姓名,
                Role = 请求.Role,
                OperatorId = 操作者ID,
                OperatorName = 操作者姓名,
                ReplacedPlayerId = 被顶替玩家ID,
                ReplacedPlayerName = 被顶替玩家姓名
            };
            await hubContext.Clients.Group($"clan_{请求.ClanId}").SendAsync("OnGameEvent", appointEvent);

            // 通过自建 WebSocket 通道广播相同事件（JSON 格式）
            await WebSocketConnectionManager.BroadcastAsync(appointEvent);

            // 记录家族日志：职位任命
            string 任命详情 = "";
            if (被顶替玩家ID.HasValue && 被顶替玩家ID.Value > 0)
            {
                任命详情 = $"{{\"role\":\"{请求.Role}\",\"old_role\":\"成员\",\"replaced_player_id\":{被顶替玩家ID.Value},\"replaced_player_name\":\"{被顶替玩家姓名}\"}}";
            }
            else
            {
                任命详情 = $"{{\"role\":\"{请求.Role}\",\"old_role\":\"成员\"}}";
            }
            string 任命描述 = 被顶替玩家ID.HasValue && 被顶替玩家ID.Value > 0
                ? $"{操作者姓名}任命{目标玩家姓名}为{请求.Role}（顶替了{被顶替玩家姓名}）"
                : $"{操作者姓名}任命{目标玩家姓名}为{请求.Role}";
            await 记录家族日志(
                connection,
                请求.ClanId,
                "appoint",
                操作者ID,
                操作者姓名,
                请求.PlayerId,
                目标玩家姓名,
                任命详情,
                任命描述
            );

            string 成功消息 = 当前职位玩家列表.Count >= 最大数量 
                ? $"成功任命玩家为{请求.Role}（已顶替原职位玩家）" 
                : $"成功任命玩家为{请求.Role}";
            return Results.Ok(new AppointClanRoleResponse(true, 成功消息));
        }
        catch
        {
            // 回滚事务
            await transaction.RollbackAsync();
            throw;
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new AppointClanRoleResponse(false, "服务器错误: " + ex.Message));
    }
});

// =================== 检查退出家族冷却时间接口：POST /api/checkLeaveClanCooldown ===================

app.MapPost("/api/checkLeaveClanCooldown", async ([FromBody] CheckLeaveClanCooldownRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new CheckLeaveClanCooldownResponse(false, "账号ID无效", false, 0));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 查询玩家退出家族时间
        using var command = new MySqlCommand(
            "SELECT last_clan_leave_time FROM players WHERE account_id = @account_id LIMIT 1",
            connection
        );
        command.Parameters.AddWithValue("@account_id", 请求.AccountId);

        var result = await command.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value)
        {
            return Results.Ok(new CheckLeaveClanCooldownResponse(false, "玩家不存在", false, 0));
        }

        DateTime? 退出家族时间 = result == DBNull.Value ? null : (DateTime?)result;
        
        // 如果从未退出过家族，没有冷却时间
        if (!退出家族时间.HasValue)
        {
            return Results.Ok(new CheckLeaveClanCooldownResponse(true, "可以加入家族", false, 0));
        }

        // 计算冷却时间（1小时 = 3600秒）
        TimeSpan 时间差 = DateTime.Now - 退出家族时间.Value;
        int 剩余秒数 = 3600 - (int)时间差.TotalSeconds;
        
        // 如果还在冷却中
        if (剩余秒数 > 0)
        {
            int 剩余分钟 = (剩余秒数 + 59) / 60; // 向上取整到分钟
            return Results.Ok(new CheckLeaveClanCooldownResponse(true, $"冷却中，剩余{剩余分钟}分钟", true, 剩余分钟));
        }
        else
        {
            return Results.Ok(new CheckLeaveClanCooldownResponse(true, "可以加入家族", false, 0));
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new CheckLeaveClanCooldownResponse(false, "服务器错误: " + ex.Message, false, 0));
    }
});

// =================== 获取指定国家的家族列表接口：POST /api/getClansByCountry ===================

app.MapPost("/api/getClansByCountry", async ([FromBody] GetClansByCountryRequest 请求) =>
{
    try
    {
        if (请求.CountryId <= 0)
        {
            return Results.Ok(new GetClansListResponse(false, "国家ID无效", new List<ClanSummary>()));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 查询指定国家的所有家族，按繁荣值降序排序（相同繁荣值按ID升序）
        // 使用子查询一次性获取成员数
        using var command = new MySqlCommand(
            @"SELECT c.id, c.name, c.level, c.prosperity, c.funds, c.leader_id, p.name as leader_name,
                     (SELECT COUNT(*) FROM players WHERE clan_id = c.id) as member_count
              FROM clans c
              LEFT JOIN players p ON c.leader_id = p.id
              WHERE c.country_id = @country_id
              ORDER BY c.prosperity DESC, c.id ASC",
            connection
        );
        command.Parameters.AddWithValue("@country_id", 请求.CountryId);

        var 家族列表 = new List<ClanSummary>();
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            int 家族ID = reader.GetInt32(0);
            string 家族名字 = reader.GetString(1);
            int 家族等级 = reader.GetInt32(2);
            int 家族繁荣值 = reader.GetInt32(3);
            int 家族资金 = reader.GetInt32(4);
            int 族长ID = reader.GetInt32(5);
            string 族长姓名 = reader.IsDBNull(6) ? "" : reader.GetString(6);
            int 成员数 = reader.GetInt32(7);

            家族列表.Add(new ClanSummary
            {
                Id = 家族ID,
                Name = 家族名字,
                Level = 家族等级,
                Prosperity = 家族繁荣值,
                Funds = 家族资金,
                LeaderId = 族长ID,
                LeaderName = 族长姓名,
                MemberCount = 成员数
            });
        }

        return Results.Ok(new GetClansListResponse(true, "获取成功", 家族列表));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetClansListResponse(false, "服务器错误: " + ex.Message, new List<ClanSummary>()));
    }
});

// =================== 获取所有家族列表接口：GET /api/getAllClans ===================

app.MapGet("/api/getAllClans", async () =>
{
    try
    {
        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 查询所有家族，按繁荣值降序排序（相同繁荣值按ID升序）
        // 使用子查询一次性获取成员数
        using var command = new MySqlCommand(
            @"SELECT c.id, c.name, c.level, c.prosperity, c.funds, c.leader_id, p.name as leader_name, 
                     c.country_id, co.name as country_name, co.code as country_code,
                     (SELECT COUNT(*) FROM players WHERE clan_id = c.id) as member_count
              FROM clans c
              LEFT JOIN players p ON c.leader_id = p.id
              LEFT JOIN countries co ON c.country_id = co.id
              ORDER BY c.prosperity DESC, c.id ASC",
            connection
        );

        var 家族列表 = new List<ClanSummary>();
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            int 家族ID = reader.GetInt32(0);
            string 家族名字 = reader.GetString(1);
            int 家族等级 = reader.GetInt32(2);
            int 家族繁荣值 = reader.GetInt32(3);
            int 家族资金 = reader.GetInt32(4);
            int 族长ID = reader.GetInt32(5);
            string 族长姓名 = reader.IsDBNull(6) ? "" : reader.GetString(6);
            int? 国家ID = reader.IsDBNull(7) ? null : reader.GetInt32(7);
            string 国家名字 = reader.IsDBNull(8) ? "" : reader.GetString(8);
            string 国家代码 = reader.IsDBNull(9) ? "" : reader.GetString(9);
            int 成员数 = reader.GetInt32(10);

            家族列表.Add(new ClanSummary
            {
                Id = 家族ID,
                Name = 家族名字,
                Level = 家族等级,
                Prosperity = 家族繁荣值,
                Funds = 家族资金,
                LeaderId = 族长ID,
                LeaderName = 族长姓名,
                MemberCount = 成员数,
                CountryId = 国家ID ?? -1,
                CountryName = 国家名字,
                CountryCode = 国家代码
            });
        }

        return Results.Ok(new GetClansListResponse(true, "获取成功", 家族列表));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetClansListResponse(false, "服务器错误: " + ex.Message, new List<ClanSummary>()));
    }
});

// =================== 聊天系统 API ===================

// 发送世界消息接口：POST /api/sendWorldMessage
app.MapPost("/api/sendWorldMessage", async ([FromBody] SendWorldMessageRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new SendMessageResponse(false, "账号ID无效"));
        }

        if (string.IsNullOrWhiteSpace(请求.Message))
        {
            return Results.Ok(new SendMessageResponse(false, "消息内容不能为空"));
        }

        // 消息长度限制：20字以内
        if (请求.Message.Length > 20)
        {
            return Results.Ok(new SendMessageResponse(false, "消息长度不能超过20字"));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 1. 查询玩家信息
        using var playerCommand = new MySqlCommand(
            "SELECT id, name, level, gold, country_id FROM players WHERE account_id = @account_id",
            connection
        );
        playerCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);

        using var playerReader = await playerCommand.ExecuteReaderAsync();
        if (!await playerReader.ReadAsync())
        {
            return Results.Ok(new SendMessageResponse(false, "玩家不存在"));
        }

        int 玩家ID = playerReader.GetInt32(0);
        string 玩家姓名 = playerReader.GetString(1);
        int 玩家等级 = playerReader.GetInt32(2);
        int 玩家黄金 = playerReader.GetInt32(3);
        int? 国家ID = playerReader.IsDBNull(4) ? null : playerReader.GetInt32(4);
        playerReader.Close();

        // 2. 等级检查：世界消息需要≥25级
        if (玩家等级 < 25)
        {
            return Results.Ok(new SendMessageResponse(false, "世界消息需要等级≥25级"));
        }

        // 3. 黄金检查：每次发送扣除5黄金
        if (玩家黄金 < 5)
        {
            return Results.Ok(new SendMessageResponse(false, "黄金不足，发送世界消息需要5黄金"));
        }

        // 4. 频率限制：5秒一次
        if (玩家最后发言时间.TryGetValue(玩家ID, out var 最后发言时间))
        {
            var 时间差 = (DateTime.Now - 最后发言时间).TotalSeconds;
            if (时间差 < 5)
            {
                int 剩余秒数 = 5 - (int)时间差;
                return Results.Ok(new SendMessageResponse(false, $"发送消息过于频繁，请{剩余秒数}秒后再试"));
            }
        }

        // 5. 防刷检测：1分钟内相同消息≥5次则禁言1小时
        string 消息哈希 = 计算SHA256(请求.Message);
        using var checkSpamCommand = new MySqlCommand(
            @"SELECT COUNT(*) FROM player_message_logs 
              WHERE player_id = @player_id 
              AND channel_type = 'world' 
              AND message_hash = @message_hash 
              AND created_at > DATE_SUB(NOW(), INTERVAL 1 MINUTE)",
            connection
        );
        checkSpamCommand.Parameters.AddWithValue("@player_id", 玩家ID);
        checkSpamCommand.Parameters.AddWithValue("@message_hash", 消息哈希);
        var spamCount = Convert.ToInt32(await checkSpamCommand.ExecuteScalarAsync());

        if (spamCount >= 5)
        {
            // 添加禁言记录（1小时）
            using var muteCommand = new MySqlCommand(
                "INSERT INTO player_mute_records (player_id, mute_until, reason) VALUES (@player_id, DATE_ADD(NOW(), INTERVAL 1 HOUR), @reason)",
                connection
            );
            muteCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            muteCommand.Parameters.AddWithValue("@reason", "刷屏行为：1分钟内发送相同消息超过5次");
            await muteCommand.ExecuteNonQueryAsync();

            // 发送禁言通知给玩家
            var muteEvent = new SystemMessageEvent
            {
                Message = "你已被禁言1小时！",
                MessageTime = DateTime.Now
            };
            await WebSocketConnectionManager.SendToPlayerAsync(玩家ID, muteEvent, 玩家连接映射);

            return Results.Ok(new SendMessageResponse(false, "你已被禁言1小时"));
        }

        // 6. 检查是否在禁言期内
        using var muteCheckCommand = new MySqlCommand(
            "SELECT mute_until FROM player_mute_records WHERE player_id = @player_id AND mute_until > NOW() ORDER BY mute_until DESC LIMIT 1",
            connection
        );
        muteCheckCommand.Parameters.AddWithValue("@player_id", 玩家ID);
        var muteResult = await muteCheckCommand.ExecuteScalarAsync();
        if (muteResult != null && !DBNull.Value.Equals(muteResult))
        {
            var muteUntil = Convert.ToDateTime(muteResult);
            var 剩余时间 = muteUntil - DateTime.Now;
            int 剩余分钟 = (int)剩余时间.TotalMinutes;
            return Results.Ok(new SendMessageResponse(false, $"你已被禁言，剩余{剩余分钟}分钟"));
        }

        // 7. 扣除黄金
        using var updateGoldCommand = new MySqlCommand(
            "UPDATE players SET gold = gold - 5 WHERE id = @player_id",
            connection
        );
        updateGoldCommand.Parameters.AddWithValue("@player_id", 玩家ID);
        await updateGoldCommand.ExecuteNonQueryAsync();

        // 8. 记录发言日志
        using var logCommand = new MySqlCommand(
            "INSERT INTO player_message_logs (player_id, channel_type, message_hash) VALUES (@player_id, 'world', @message_hash)",
            connection
        );
        logCommand.Parameters.AddWithValue("@player_id", 玩家ID);
        logCommand.Parameters.AddWithValue("@message_hash", 消息哈希);
        await logCommand.ExecuteNonQueryAsync();

        // 9. 更新最后发言时间
        玩家最后发言时间.AddOrUpdate(玩家ID, DateTime.Now, (key, oldValue) => DateTime.Now);

        // 10. 将消息加入队列（优先级3：世界消息）
        消息队列.Enqueue(new 待处理消息
        {
            优先级 = 3,
            频道类型 = "world",
            玩家ID = 玩家ID,
            玩家姓名 = 玩家姓名,
            消息内容 = 请求.Message
        });

        return Results.Ok(new SendMessageResponse(true, "消息发送成功"));
    }
    catch (Exception ex)
    {
        return Results.Ok(new SendMessageResponse(false, "服务器错误: " + ex.Message));
    }
});

// 发送国家消息接口：POST /api/sendCountryMessage
app.MapPost("/api/sendCountryMessage", async ([FromBody] SendCountryMessageRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new SendMessageResponse(false, "账号ID无效"));
        }

        if (string.IsNullOrWhiteSpace(请求.Message))
        {
            return Results.Ok(new SendMessageResponse(false, "消息内容不能为空"));
        }

        // 消息长度限制：20字以内
        if (请求.Message.Length > 20)
        {
            return Results.Ok(new SendMessageResponse(false, "消息长度不能超过20字"));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 1. 查询玩家信息
        using var playerCommand = new MySqlCommand(
            "SELECT id, name, level, country_id FROM players WHERE account_id = @account_id",
            connection
        );
        playerCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);

        using var playerReader = await playerCommand.ExecuteReaderAsync();
        if (!await playerReader.ReadAsync())
        {
            return Results.Ok(new SendMessageResponse(false, "玩家不存在"));
        }

        int 玩家ID = playerReader.GetInt32(0);
        string 玩家姓名 = playerReader.GetString(1);
        int 玩家等级 = playerReader.GetInt32(2);
        int? 国家ID = playerReader.IsDBNull(3) ? null : playerReader.GetInt32(3);
        playerReader.Close();

        // 2. 检查玩家是否有国家
        if (!国家ID.HasValue || 国家ID.Value <= 0)
        {
            return Results.Ok(new SendMessageResponse(false, "你还没有加入国家"));
        }

        // 3. 等级检查：国家消息需要≥15级
        if (玩家等级 < 15)
        {
            return Results.Ok(new SendMessageResponse(false, "国家消息需要等级≥15级"));
        }

        // 4. 频率限制：5秒一次
        if (玩家最后发言时间.TryGetValue(玩家ID, out var 最后发言时间))
        {
            var 时间差 = (DateTime.Now - 最后发言时间).TotalSeconds;
            if (时间差 < 5)
            {
                int 剩余秒数 = 5 - (int)时间差;
                return Results.Ok(new SendMessageResponse(false, $"发送消息过于频繁，请{剩余秒数}秒后再试"));
            }
        }

        // 5. 防刷检测：1分钟内相同消息≥5次则禁言1小时
        string 消息哈希 = 计算SHA256(请求.Message);
        using var checkSpamCommand = new MySqlCommand(
            @"SELECT COUNT(*) FROM player_message_logs 
              WHERE player_id = @player_id 
              AND channel_type = 'country' 
              AND message_hash = @message_hash 
              AND created_at > DATE_SUB(NOW(), INTERVAL 1 MINUTE)",
            connection
        );
        checkSpamCommand.Parameters.AddWithValue("@player_id", 玩家ID);
        checkSpamCommand.Parameters.AddWithValue("@message_hash", 消息哈希);
        var spamCount = Convert.ToInt32(await checkSpamCommand.ExecuteScalarAsync());

        if (spamCount >= 5)
        {
            // 添加禁言记录（1小时）
            using var muteCommand = new MySqlCommand(
                "INSERT INTO player_mute_records (player_id, mute_until, reason) VALUES (@player_id, DATE_ADD(NOW(), INTERVAL 1 HOUR), @reason)",
                connection
            );
            muteCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            muteCommand.Parameters.AddWithValue("@reason", "刷屏行为：1分钟内发送相同消息超过5次");
            await muteCommand.ExecuteNonQueryAsync();

            // 发送禁言通知给玩家
            var muteEvent = new SystemMessageEvent
            {
                Message = "你已被禁言1小时！",
                MessageTime = DateTime.Now
            };
            await WebSocketConnectionManager.SendToPlayerAsync(玩家ID, muteEvent, 玩家连接映射);

            return Results.Ok(new SendMessageResponse(false, "你已被禁言1小时"));
        }

        // 6. 检查是否在禁言期内
        using var muteCheckCommand = new MySqlCommand(
            "SELECT mute_until FROM player_mute_records WHERE player_id = @player_id AND mute_until > NOW() ORDER BY mute_until DESC LIMIT 1",
            connection
        );
        muteCheckCommand.Parameters.AddWithValue("@player_id", 玩家ID);
        var muteResult = await muteCheckCommand.ExecuteScalarAsync();
        if (muteResult != null && !DBNull.Value.Equals(muteResult))
        {
            var muteUntil = Convert.ToDateTime(muteResult);
            var 剩余时间 = muteUntil - DateTime.Now;
            int 剩余分钟 = (int)剩余时间.TotalMinutes;
            return Results.Ok(new SendMessageResponse(false, $"你已被禁言，剩余{剩余分钟}分钟"));
        }

        // 7. 记录发言日志
        using var logCommand = new MySqlCommand(
            "INSERT INTO player_message_logs (player_id, channel_type, message_hash) VALUES (@player_id, 'country', @message_hash)",
            connection
        );
        logCommand.Parameters.AddWithValue("@player_id", 玩家ID);
        logCommand.Parameters.AddWithValue("@message_hash", 消息哈希);
        await logCommand.ExecuteNonQueryAsync();

        // 8. 更新最后发言时间
        玩家最后发言时间.AddOrUpdate(玩家ID, DateTime.Now, (key, oldValue) => DateTime.Now);

        // 9. 将消息加入队列（优先级2：国家消息）
        消息队列.Enqueue(new 待处理消息
        {
            优先级 = 2,
            频道类型 = "country",
            玩家ID = 玩家ID,
            玩家姓名 = 玩家姓名,
            消息内容 = 请求.Message,
            国家ID = 国家ID
        });

        return Results.Ok(new SendMessageResponse(true, "消息发送成功"));
    }
    catch (Exception ex)
    {
        return Results.Ok(new SendMessageResponse(false, "服务器错误: " + ex.Message));
    }
});

// 发送家族消息接口：POST /api/sendClanMessage
app.MapPost("/api/sendClanMessage", async ([FromBody] SendClanMessageRequest 请求) =>
{
    try
    {
        if (请求.AccountId <= 0)
        {
            return Results.Ok(new SendMessageResponse(false, "账号ID无效"));
        }

        if (string.IsNullOrWhiteSpace(请求.Message))
        {
            return Results.Ok(new SendMessageResponse(false, "消息内容不能为空"));
        }

        // 消息长度限制：20字以内
        if (请求.Message.Length > 20)
        {
            return Results.Ok(new SendMessageResponse(false, "消息长度不能超过20字"));
        }

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 1. 查询玩家信息
        using var playerCommand = new MySqlCommand(
            "SELECT id, name, clan_id FROM players WHERE account_id = @account_id",
            connection
        );
        playerCommand.Parameters.AddWithValue("@account_id", 请求.AccountId);

        using var playerReader = await playerCommand.ExecuteReaderAsync();
        if (!await playerReader.ReadAsync())
        {
            return Results.Ok(new SendMessageResponse(false, "玩家不存在"));
        }

        int 玩家ID = playerReader.GetInt32(0);
        string 玩家姓名 = playerReader.GetString(1);
        int? 家族ID = playerReader.IsDBNull(2) ? null : playerReader.GetInt32(2);
        playerReader.Close();

        // 2. 检查玩家是否有家族
        if (!家族ID.HasValue || 家族ID.Value <= 0)
        {
            return Results.Ok(new SendMessageResponse(false, "你还没有加入家族"));
        }

        // 3. 频率限制：5秒一次
        if (玩家最后发言时间.TryGetValue(玩家ID, out var 最后发言时间))
        {
            var 时间差 = (DateTime.Now - 最后发言时间).TotalSeconds;
            if (时间差 < 5)
            {
                int 剩余秒数 = 5 - (int)时间差;
                return Results.Ok(new SendMessageResponse(false, $"发送消息过于频繁，请{剩余秒数}秒后再试"));
            }
        }

        // 4. 防刷检测：1分钟内相同消息≥5次则禁言1小时
        string 消息哈希 = 计算SHA256(请求.Message);
        using var checkSpamCommand = new MySqlCommand(
            @"SELECT COUNT(*) FROM player_message_logs 
              WHERE player_id = @player_id 
              AND channel_type = 'clan' 
              AND message_hash = @message_hash 
              AND created_at > DATE_SUB(NOW(), INTERVAL 1 MINUTE)",
            connection
        );
        checkSpamCommand.Parameters.AddWithValue("@player_id", 玩家ID);
        checkSpamCommand.Parameters.AddWithValue("@message_hash", 消息哈希);
        var spamCount = Convert.ToInt32(await checkSpamCommand.ExecuteScalarAsync());

        if (spamCount >= 5)
        {
            // 添加禁言记录（1小时）
            using var muteCommand = new MySqlCommand(
                "INSERT INTO player_mute_records (player_id, mute_until, reason) VALUES (@player_id, DATE_ADD(NOW(), INTERVAL 1 HOUR), @reason)",
                connection
            );
            muteCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            muteCommand.Parameters.AddWithValue("@reason", "刷屏行为：1分钟内发送相同消息超过5次");
            await muteCommand.ExecuteNonQueryAsync();

            // 发送禁言通知给玩家
            var muteEvent = new SystemMessageEvent
            {
                Message = "你已被禁言1小时！",
                MessageTime = DateTime.Now
            };
            await WebSocketConnectionManager.SendToPlayerAsync(玩家ID, muteEvent, 玩家连接映射);

            return Results.Ok(new SendMessageResponse(false, "你已被禁言1小时"));
        }

        // 5. 检查是否在禁言期内
        using var muteCheckCommand = new MySqlCommand(
            "SELECT mute_until FROM player_mute_records WHERE player_id = @player_id AND mute_until > NOW() ORDER BY mute_until DESC LIMIT 1",
            connection
        );
        muteCheckCommand.Parameters.AddWithValue("@player_id", 玩家ID);
        var muteResult = await muteCheckCommand.ExecuteScalarAsync();
        if (muteResult != null && !DBNull.Value.Equals(muteResult))
        {
            var muteUntil = Convert.ToDateTime(muteResult);
            var 剩余时间 = muteUntil - DateTime.Now;
            int 剩余分钟 = (int)剩余时间.TotalMinutes;
            return Results.Ok(new SendMessageResponse(false, $"你已被禁言，剩余{剩余分钟}分钟"));
        }

        // 6. 记录发言日志
        using var logCommand = new MySqlCommand(
            "INSERT INTO player_message_logs (player_id, channel_type, message_hash) VALUES (@player_id, 'clan', @message_hash)",
            connection
        );
        logCommand.Parameters.AddWithValue("@player_id", 玩家ID);
        logCommand.Parameters.AddWithValue("@message_hash", 消息哈希);
        await logCommand.ExecuteNonQueryAsync();

        // 7. 更新最后发言时间
        玩家最后发言时间.AddOrUpdate(玩家ID, DateTime.Now, (key, oldValue) => DateTime.Now);

        // 8. 将消息加入队列（优先级1：家族消息）
        消息队列.Enqueue(new 待处理消息
        {
            优先级 = 1,
            频道类型 = "clan",
            玩家ID = 玩家ID,
            玩家姓名 = 玩家姓名,
            消息内容 = 请求.Message,
            家族ID = 家族ID
        });

        return Results.Ok(new SendMessageResponse(true, "消息发送成功"));
    }
    catch (Exception ex)
    {
        return Results.Ok(new SendMessageResponse(false, "服务器错误: " + ex.Message));
    }
});

// 获取世界消息历史接口：GET /api/getWorldMessages?limit=10
app.MapGet("/api/getWorldMessages", async (HttpContext context) =>
{
    try
    {
        int limit = 10;
        if (context.Request.Query.TryGetValue("limit", out var limitStr) && int.TryParse(limitStr, out var parsedLimit))
        {
            limit = parsedLimit;
        }
        if (limit <= 0 || limit > 20) limit = 10;

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        using var command = new MySqlCommand(
            "SELECT player_id, player_name, message, created_at FROM world_messages ORDER BY created_at DESC LIMIT @limit",
            connection
        );
        command.Parameters.AddWithValue("@limit", limit);

        var 消息列表 = new List<ChatMessageData>();
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            消息列表.Add(new ChatMessageData
            {
                PlayerId = reader.GetInt32(0),
                PlayerName = reader.GetString(1),
                Message = reader.GetString(2),
                MessageTime = reader.GetDateTime(3)
            });
        }

        return Results.Ok(new GetChatMessagesResponse(true, "获取成功", 消息列表));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetChatMessagesResponse(false, "服务器错误: " + ex.Message, new List<ChatMessageData>()));
    }
});

// 获取国家消息历史接口：GET /api/getCountryMessages?limit=10
app.MapGet("/api/getCountryMessages", async (HttpContext context) =>
{
    try
    {
        int limit = 10;
        if (context.Request.Query.TryGetValue("limit", out var limitStr) && int.TryParse(limitStr, out var parsedLimit))
        {
            limit = parsedLimit;
        }
        if (limit <= 0 || limit > 20) limit = 10;

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        using var command = new MySqlCommand(
            "SELECT player_id, player_name, message, created_at FROM country_messages ORDER BY created_at DESC LIMIT @limit",
            connection
        );
        command.Parameters.AddWithValue("@limit", limit);

        var 消息列表 = new List<ChatMessageData>();
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            消息列表.Add(new ChatMessageData
            {
                PlayerId = reader.GetInt32(0),
                PlayerName = reader.GetString(1),
                Message = reader.GetString(2),
                MessageTime = reader.GetDateTime(3)
            });
        }

        return Results.Ok(new GetChatMessagesResponse(true, "获取成功", 消息列表));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetChatMessagesResponse(false, "服务器错误: " + ex.Message, new List<ChatMessageData>()));
    }
});

// 获取家族消息历史接口：GET /api/getClanMessages?limit=10
app.MapGet("/api/getClanMessages", async (HttpContext context) =>
{
    try
    {
        int limit = 10;
        if (context.Request.Query.TryGetValue("limit", out var limitStr) && int.TryParse(limitStr, out var parsedLimit))
        {
            limit = parsedLimit;
        }
        if (limit <= 0 || limit > 20) limit = 10;

        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        using var command = new MySqlCommand(
            "SELECT player_id, player_name, message, created_at FROM clan_messages ORDER BY created_at DESC LIMIT @limit",
            connection
        );
        command.Parameters.AddWithValue("@limit", limit);

        var 消息列表 = new List<ChatMessageData>();
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            消息列表.Add(new ChatMessageData
            {
                PlayerId = reader.GetInt32(0),
                PlayerName = reader.GetString(1),
                Message = reader.GetString(2),
                MessageTime = reader.GetDateTime(3)
            });
        }

        return Results.Ok(new GetChatMessagesResponse(true, "获取成功", 消息列表));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetChatMessagesResponse(false, "服务器错误: " + ex.Message, new List<ChatMessageData>()));
    }
});

// =================== 计算 SHA256 哈希的辅助方法 ===================

// 计算字符串的 SHA256 哈希（返回小写十六进制字符串）
static string 计算SHA256(string 输入)
{
    using var sha256 = SHA256.Create();
    byte[] bytes = Encoding.UTF8.GetBytes(输入);
    byte[] hashBytes = sha256.ComputeHash(bytes);
    StringBuilder sb = new StringBuilder();
    foreach (var b in hashBytes)
    {
        sb.Append(b.ToString("x2")); // 转成小写十六进制
    }
    return sb.ToString();
}

// =================== 家族日志记录辅助函数 ===================

/// <summary>
/// 记录家族日志并限制每个家族最多300条日志
/// </summary>
static async Task 记录家族日志(
    MySqlConnection connection,
    int clanId,
    string operationType,
    int? operatorId,
    string? operatorName,
    int? targetPlayerId,
    string? targetPlayerName,
    string? details,
    string description)
{
    try
    {
        // 1. 插入新日志
        using var insertCommand = new MySqlCommand(
            @"INSERT INTO clan_logs (clan_id, operation_type, operator_id, operator_name, 
                                    target_player_id, target_player_name, details, description)
              VALUES (@clan_id, @operation_type, @operator_id, @operator_name, 
                      @target_player_id, @target_player_name, @details, @description)",
            connection
        );
        insertCommand.Parameters.AddWithValue("@clan_id", clanId);
        insertCommand.Parameters.AddWithValue("@operation_type", operationType);
        insertCommand.Parameters.AddWithValue("@operator_id", operatorId.HasValue ? (object)operatorId.Value : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@operator_name", string.IsNullOrEmpty(operatorName) ? (object)DBNull.Value : operatorName);
        insertCommand.Parameters.AddWithValue("@target_player_id", targetPlayerId.HasValue ? (object)targetPlayerId.Value : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@target_player_name", string.IsNullOrEmpty(targetPlayerName) ? (object)DBNull.Value : targetPlayerName);
        insertCommand.Parameters.AddWithValue("@details", string.IsNullOrEmpty(details) ? (object)DBNull.Value : details);
        insertCommand.Parameters.AddWithValue("@description", description);
        
        await insertCommand.ExecuteNonQueryAsync();

        // 2. 删除超过300条的最旧记录（每个家族独立限制）
        using var deleteCommand = new MySqlCommand(
            @"DELETE FROM clan_logs 
              WHERE clan_id = @clan_id
              AND id NOT IN (
                  SELECT id FROM (
                      SELECT id FROM clan_logs 
                      WHERE clan_id = @clan_id 
                      ORDER BY created_at DESC 
                      LIMIT 300
                  ) AS temp
              )",
            connection
        );
        deleteCommand.Parameters.AddWithValue("@clan_id", clanId);
        await deleteCommand.ExecuteNonQueryAsync();
    }
    catch (Exception ex)
    {
        日志记录器.错误($"[家族日志] 记录日志失败: {ex.Message}");
        // 不抛出异常，避免影响主业务逻辑
    }
}

// =================== 战场倒计时管理 ===================

/// <summary>
/// 启动战场倒计时
/// </summary>
void 启动战场倒计时(int 国家ID, int 家族1ID, int 家族2ID, DateTime 战场开始时间)
{
    // 如果已有倒计时任务，先取消
    if (战场倒计时任务.TryGetValue(国家ID, out var 旧任务))
    {
        旧任务.Cancel();
        战场倒计时任务.TryRemove(国家ID, out _);
    }

    var 取消令牌源 = new CancellationTokenSource();
    战场倒计时任务.TryAdd(国家ID, 取消令牌源);

    _ = Task.Run(async () =>
    {
        try
        {
            var 剩余时间 = (战场开始时间 - DateTime.Now).TotalSeconds;
            
            // 立即发送第一次通知（确保所有客户端都能收到倒计时开始的通知）
            int 初始秒数 = (int)Math.Ceiling(剩余时间);
            if (初始秒数 > 0)
            {
                日志记录器.信息($"[战场倒计时] 国家 {国家ID} 战场倒计时开始，剩余 {初始秒数} 秒，立即通知所有相关玩家");
                await 通知战场倒计时(国家ID, 家族1ID, 家族2ID, 初始秒数);
            }
            
            // 等待倒计时结束
            while (剩余时间 > 0 && !取消令牌源.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, 取消令牌源.Token); // 每秒检查一次
                剩余时间 = (战场开始时间 - DateTime.Now).TotalSeconds;

                // 每5秒或最后10秒时，通过WebSocket通知所有相关玩家
                int 整数秒数 = (int)Math.Ceiling(剩余时间);
                if (整数秒数 % 5 == 0 || 整数秒数 <= 10)
                {
                    // 通知两个家族的所有在线玩家
                    await 通知战场倒计时(国家ID, 家族1ID, 家族2ID, 整数秒数);
                }
            }

            // 倒计时结束，生成战场
            if (!取消令牌源.Token.IsCancellationRequested && 剩余时间 <= 0)
            {
                日志记录器.信息($"[战场倒计时] 国家 {国家ID} 的战场倒计时结束，开始生成战场");
                await 生成战场(国家ID, 家族1ID, 家族2ID);
            }
        }
        catch (OperationCanceledException)
        {
            日志记录器.信息($"[战场倒计时] 国家 {国家ID} 的倒计时已取消");
        }
        catch (Exception ex)
        {
            日志记录器.错误($"[战场倒计时] 国家 {国家ID} 的倒计时任务出错: {ex.Message}");
        }
        finally
        {
            战场倒计时任务.TryRemove(国家ID, out _);
        }
    });
}

/// <summary>
/// 通知战场倒计时（通过WebSocket）
/// </summary>
async Task 通知战场倒计时(int 国家ID, int 家族1ID, int 家族2ID, int 剩余秒数)
{
    // 查询两个家族的所有玩家ID
    using var connection = new MySqlConnection(数据库连接字符串);
    await connection.OpenAsync();

    var 玩家ID列表 = new List<int>();
    
    // 查询家族1的玩家
    using (var cmd1 = new MySqlCommand("SELECT id FROM players WHERE clan_id = @clan_id", connection))
    {
        cmd1.Parameters.AddWithValue("@clan_id", 家族1ID);
        using var reader1 = await cmd1.ExecuteReaderAsync();
        while (await reader1.ReadAsync())
        {
            玩家ID列表.Add(reader1.GetInt32(0));
        }
    }

    // 查询家族2的玩家
    using (var cmd2 = new MySqlCommand("SELECT id FROM players WHERE clan_id = @clan_id", connection))
    {
        cmd2.Parameters.AddWithValue("@clan_id", 家族2ID);
        using var reader2 = await cmd2.ExecuteReaderAsync();
        while (await reader2.ReadAsync())
        {
            玩家ID列表.Add(reader2.GetInt32(0));
        }
    }

    // 发送倒计时消息给所有在线玩家
    var 倒计时事件 = new
    {
        eventType = "BattlefieldCountdown",
        countryId = 国家ID,
        remainingSeconds = 剩余秒数
    };

    string 消息内容 = JsonSerializer.Serialize(倒计时事件);
    byte[] 消息字节 = Encoding.UTF8.GetBytes(消息内容);

    foreach (var 玩家ID in 玩家ID列表)
    {
        if (玩家连接映射.TryGetValue(玩家ID, out var socket) && socket != null && socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(消息字节), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch
            {
                // 忽略发送失败
            }
        }
    }
}

/// <summary>
/// 生成战场（倒计时结束后调用）
/// </summary>
async Task 生成战场(int 国家ID, int 家族1ID, int 家族2ID)
{
    using var connection = new MySqlConnection(数据库连接字符串);
    await connection.OpenAsync();

    using var transaction = await connection.BeginTransactionAsync();
    try
    {
        // 更新战场状态为战斗中
        using (var cmd = new MySqlCommand(
            @"UPDATE battlefields 
              SET battlefield_status = 'fighting', start_time = NOW() 
              WHERE country_id = @country_id 
                 AND battlefield_status = 'preparing'",
            connection,
            transaction))
        {
            cmd.Parameters.AddWithValue("@country_id", 国家ID);
            await cmd.ExecuteNonQueryAsync();
        }

        // TODO: 这里以后实现具体的战场生成逻辑
        // 例如：生成Boss、初始化积分等

        await transaction.CommitAsync();
        日志记录器.信息($"[战场] 国家 {国家ID} 的战场已生成");
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        日志记录器.错误($"[战场] 生成战场失败: {ex.Message}");
    }
}

// =================== 消息处理函数 ===================

// 处理消息（存储到数据库并广播）
static async Task 处理消息(待处理消息 消息, string 数据库连接字符串, ConcurrentDictionary<int, WebSocket> 玩家连接映射)
{
    using var connection = new MySqlConnection(数据库连接字符串);
    await connection.OpenAsync();

    try
    {
        if (消息.频道类型 == "system")
        {
            // 系统消息：只发送给目标玩家，不存储
            if (消息.目标玩家ID.HasValue)
            {
                var systemEvent = new SystemMessageEvent
                {
                    Message = 消息.消息内容,
                    MessageTime = DateTime.Now
                };
                await WebSocketConnectionManager.SendToPlayerAsync(消息.目标玩家ID.Value, systemEvent, 玩家连接映射);
            }
        }
        else if (消息.频道类型 == "world")
        {
            // 世界消息：存储到数据库（保留最新20条），然后广播给所有玩家
            using var insertCommand = new MySqlCommand(
                "INSERT INTO world_messages (player_id, player_name, message) VALUES (@player_id, @player_name, @message)",
                connection
            );
            insertCommand.Parameters.AddWithValue("@player_id", 消息.玩家ID);
            insertCommand.Parameters.AddWithValue("@player_name", 消息.玩家姓名);
            insertCommand.Parameters.AddWithValue("@message", 消息.消息内容);
            await insertCommand.ExecuteNonQueryAsync();

            // 删除超过20条的消息
            using var deleteCommand = new MySqlCommand(
                @"DELETE FROM world_messages 
                  WHERE id NOT IN (
                      SELECT id FROM (
                          SELECT id FROM world_messages ORDER BY created_at DESC LIMIT 20
                      ) AS temp
                  )",
                connection
            );
            await deleteCommand.ExecuteNonQueryAsync();

            // 广播给所有玩家（排除发送者自己，因为发送者已经在本地显示了）
            var chatEvent = new ChatMessageEvent
            {
                Channel = "world",
                PlayerId = 消息.玩家ID,
                PlayerName = 消息.玩家姓名,
                Message = 消息.消息内容,
                MessageTime = DateTime.Now
            };
            // 向所有在线玩家广播，但排除发送者自己
            await WebSocketConnectionManager.BroadcastExceptAsync(chatEvent, 消息.玩家ID, 玩家连接映射);
        }
        else if (消息.频道类型 == "country")
        {
            // 国家消息：存储到数据库（保留最新20条），然后广播给所有玩家
            if (消息.国家ID.HasValue)
            {
                using var insertCommand = new MySqlCommand(
                    "INSERT INTO country_messages (country_id, player_id, player_name, message) VALUES (@country_id, @player_id, @player_name, @message)",
                    connection
                );
                insertCommand.Parameters.AddWithValue("@country_id", 消息.国家ID.Value);
                insertCommand.Parameters.AddWithValue("@player_id", 消息.玩家ID);
                insertCommand.Parameters.AddWithValue("@player_name", 消息.玩家姓名);
                insertCommand.Parameters.AddWithValue("@message", 消息.消息内容);
                await insertCommand.ExecuteNonQueryAsync();

                // 删除超过20条的消息
                using var deleteCommand = new MySqlCommand(
                    @"DELETE FROM country_messages 
                      WHERE id NOT IN (
                          SELECT id FROM (
                              SELECT id FROM country_messages ORDER BY created_at DESC LIMIT 20
                          ) AS temp
                      )",
                    connection
                );
                await deleteCommand.ExecuteNonQueryAsync();

                // 只广播给该国家的所有在线玩家
                var chatEvent = new ChatMessageEvent
                {
                    Channel = "country",
                    PlayerId = 消息.玩家ID,
                    PlayerName = 消息.玩家姓名,
                    Message = 消息.消息内容,
                    MessageTime = DateTime.Now
                };
                // 查询该国家的所有在线玩家ID，然后只向这些玩家发送消息
                using var countryPlayersCommand = new MySqlCommand(
                    "SELECT id FROM players WHERE country_id = @country_id",
                    connection
                );
                countryPlayersCommand.Parameters.AddWithValue("@country_id", 消息.国家ID.Value);
                var 国家玩家ID列表 = new List<int>();
                using var countryPlayersReader = await countryPlayersCommand.ExecuteReaderAsync();
                while (await countryPlayersReader.ReadAsync())
                {
                    国家玩家ID列表.Add(countryPlayersReader.GetInt32(0));
                }
                countryPlayersReader.Close();
                
                // 只向该国家的在线玩家发送消息（排除发送者自己）
                foreach (var 玩家ID in 国家玩家ID列表)
                {
                    // 排除发送者自己，因为发送者已经在本地显示了
                    if (玩家ID == 消息.玩家ID) continue;
                    
                    if (玩家连接映射.TryGetValue(玩家ID, out var socket) && socket != null && socket.State == WebSocketState.Open)
                    {
                        await WebSocketConnectionManager.SendToPlayerAsync(玩家ID, chatEvent, 玩家连接映射);
                    }
                }
            }
        }
        else if (消息.频道类型 == "clan")
        {
            // 家族消息：存储到数据库（保留最新20条），然后广播给所有玩家
            if (消息.家族ID.HasValue)
            {
                using var insertCommand = new MySqlCommand(
                    "INSERT INTO clan_messages (clan_id, player_id, player_name, message) VALUES (@clan_id, @player_id, @player_name, @message)",
                    connection
                );
                insertCommand.Parameters.AddWithValue("@clan_id", 消息.家族ID.Value);
                insertCommand.Parameters.AddWithValue("@player_id", 消息.玩家ID);
                insertCommand.Parameters.AddWithValue("@player_name", 消息.玩家姓名);
                insertCommand.Parameters.AddWithValue("@message", 消息.消息内容);
                await insertCommand.ExecuteNonQueryAsync();

                // 删除超过20条的消息
                using var deleteCommand = new MySqlCommand(
                    @"DELETE FROM clan_messages 
                      WHERE id NOT IN (
                          SELECT id FROM (
                              SELECT id FROM clan_messages ORDER BY created_at DESC LIMIT 20
                          ) AS temp
                      )",
                    connection
                );
                await deleteCommand.ExecuteNonQueryAsync();

                // 只广播给该家族的所有在线玩家
                var chatEvent = new ChatMessageEvent
                {
                    Channel = "clan",
                    PlayerId = 消息.玩家ID,
                    PlayerName = 消息.玩家姓名,
                    Message = 消息.消息内容,
                    MessageTime = DateTime.Now
                };
                // 查询该家族的所有在线玩家ID，然后只向这些玩家发送消息
                using var clanPlayersCommand = new MySqlCommand(
                    "SELECT id FROM players WHERE clan_id = @clan_id",
                    connection
                );
                clanPlayersCommand.Parameters.AddWithValue("@clan_id", 消息.家族ID.Value);
                var 家族玩家ID列表 = new List<int>();
                using var clanPlayersReader = await clanPlayersCommand.ExecuteReaderAsync();
                while (await clanPlayersReader.ReadAsync())
                {
                    家族玩家ID列表.Add(clanPlayersReader.GetInt32(0));
                }
                clanPlayersReader.Close();
                
                // 只向该家族的在线玩家发送消息（排除发送者自己）
                foreach (var 玩家ID in 家族玩家ID列表)
                {
                    // 排除发送者自己，因为发送者已经在本地显示了
                    if (玩家ID == 消息.玩家ID) continue;
                    
                    if (玩家连接映射.TryGetValue(玩家ID, out var socket) && socket != null && socket.State == WebSocketState.Open)
                    {
                        await WebSocketConnectionManager.SendToPlayerAsync(玩家ID, chatEvent, 玩家连接映射);
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        日志记录器.错误($"[处理消息] 错误: {ex.Message}");
        throw;
    }
}

app.Run();

// =================== 类型定义 ===================

public record LoginRequest(string Username, string Password);

public record LoginResponse(bool Success, string Message, string Token, int AccountId);

public record RegisterRequest(string Username, string Password);

public record RegisterResponse(bool Success, string Message);

public record GetPlayerRequest(int AccountId);

public record GetPlayerResponse(bool Success, string Message, PlayerData? Data);

public record CreatePlayerRequest(int AccountId, string Name, string Gender);

public record CreatePlayerResponse(bool Success, string Message);

public record CountryListResponse(bool Success, string Message, List<CountrySummary> Data);

public record JoinCountryRequest(int AccountId, int CountryId);

public record JoinCountryResponse(bool Success, string Message);

public record GetCountryInfoRequest(int CountryId);

public record GetCountryInfoResponse(bool Success, string Message, int MemberCount, int Rank, int? WarClan1Id, string? WarClan1Name, int? WarClan2Id, string? WarClan2Name, DateTime? BattleStartTime);

public record DeclareWarRequest(int AccountId, int CountryId);

public record DeclareWarResponse(bool Success, string Message, bool BothClansReady);

public record ChangeCountryRequest(int AccountId, int CountryId);

public record ChangeCountryResponse(bool Success, string Message);

public record GetCountryMembersRequest(int CountryId);

public record GetCountryMembersResponse(bool Success, string Message, List<PlayerSummary> Data);

public record GetAllPlayersResponse(bool Success, string Message, List<PlayerSummary> Data);

public record CreateClanRequest(int AccountId, string ClanName);

public record CreateClanResponse(bool Success, string Message, int ClanId);

public record DisbandClanRequest(int AccountId);

public record DisbandClanResponse(bool Success, string Message);

public record LogoutRequest(int AccountId);

public record LogoutResponse(bool Success, string Message);

public record HeartbeatRequest(int AccountId);

public record HeartbeatResponse(bool Success, string Message, int ClanId);

public record BattleResultRequest(int PlayerId, int MonsterType, int Experience, int CopperMoney, int CurrentHp, bool Victory);

public record BattleResultResponse(bool Success, string Message, int NewExperience, int NewCopperMoney, int NewCurrentHp, bool LevelUp, int NewLevel);

public record GetClanInfoRequest(int ClanId, int PlayerId);

public record GetClanInfoResponse(bool Success, string Message, ClanInfoData? Data);

public record DonateClanRequest(int AccountId);

public record DonateClanResponse(bool Success, string Message, bool AlreadyDonated);

public record CheckDonateStatusRequest(int AccountId);

public record CheckDonateStatusResponse(bool Success, string Message, bool AlreadyDonated);

public record JoinClanRequest(int AccountId, int ClanId);

public record JoinClanResponse(bool Success, string Message);

public record LeaveClanRequest(int AccountId);

public record LeaveClanResponse(bool Success, string Message);

public record CheckLeaveClanCooldownRequest(int AccountId);

public record CheckLeaveClanCooldownResponse(bool Success, string Message, bool InCooldown, int RemainingMinutes);

public record GetClansByCountryRequest(int CountryId);

public record GetClansListResponse(bool Success, string Message, List<ClanSummary> Data);

public record GetClanMembersRequest(int ClanId);

public record GetClanMembersResponse(bool Success, string Message, List<PlayerSummary> Data);

public record KickClanMemberRequest(int AccountId, int TargetPlayerId);

public record KickClanMemberResponse(bool Success, string Message);

// 聊天系统请求/响应类
public record SendWorldMessageRequest(int AccountId, string Message);

public record SendCountryMessageRequest(int AccountId, string Message);

public record SendClanMessageRequest(int AccountId, string Message);

public record SendMessageResponse(bool Success, string Message);

public record GetChatMessagesResponse(bool Success, string Message, List<ChatMessageData> Data);

public class ChatMessageData
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime MessageTime { get; set; }
}

public record GetClanRolesRequest(int ClanId);

public record GetClanRolesResponse(bool Success, string Message, ClanRolesData? Data);

public record AppointClanRoleRequest(int AccountId, int ClanId, int PlayerId, string Role);

public record AppointClanRoleResponse(bool Success, string Message);

public class ClanRolesData
{
    public int ClanId { get; set; }
    public int ClanLevel { get; set; }
    public List<ClanRoleSlot> Roles { get; set; } = new();
}

public class ClanRoleSlot
{
    public string Role { get; set; } = "";  // "副族长" 或 "精英"
    public int SlotIndex { get; set; }  // 职位槽位索引（从0开始）
    public int PlayerId { get; set; }  // 玩家ID，0表示未任命
    public string PlayerName { get; set; } = "";  // 玩家姓名，"无"表示未任命
    public bool IsOccupied { get; set; }  // 是否已任命
}

public class ClanRoleInfo
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
}

public class ClanSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int Prosperity { get; set; }
    public int Funds { get; set; }
    public int LeaderId { get; set; }
    public string LeaderName { get; set; } = "";
    public int MemberCount { get; set; }
    public int CountryId { get; set; } = -1;  // -1 表示没有国家
    public string CountryName { get; set; } = "";
    public string CountryCode { get; set; } = "";
}

public class ClanInfoData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int LeaderId { get; set; }
    public string LeaderName { get; set; } = "";
    public int MemberCount { get; set; }
    public int Prosperity { get; set; }
    public int Funds { get; set; }
    public int CountryRank { get; set; }  // 国家排名（基于家族繁荣值，由高到低降序）
    public int WorldRank { get; set; }     // 世界排名（基于家族繁荣值，由高到低降序）
    public string PlayerRole { get; set; } = "";  // 当前玩家在家族中的职位（族长、副族长、精英、成员）
}

// 玩家数据（用于API返回）
public class PlayerData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Gender { get; set; } = "";
    public int Level { get; set; }
    public int Experience { get; set; } = 0;  // 当前经验值
    public string TitleName { get; set; } = "";
    public string Office { get; set; } = "";
    public int CopperMoney { get; set; }
    public int Gold { get; set; }
    public int CountryId { get; set; } = -1;  // -1 表示没有国家/家族
    public int ClanId { get; set; } = -1;     // -1 表示没有国家/家族
    public int ClanContribution { get; set; } = 0;  // 家族贡献值
    public PlayerAttributesData Attributes { get; set; } = new();
    public CountryData? Country { get; set; }
    public ClanData? Clan { get; set; }
}

public class PlayerAttributesData
{
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public float CritRate { get; set; }
}

public class CountryData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
}

public class ClanData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

// =================== 怪物模板相关数据类 ===================

/// <summary>
/// 怪物模板数据
/// </summary>
public class MonsterTemplateData
{
    public int Id { get; set; }
    public int MonsterType { get; set; }
    public string Name { get; set; } = "";
    public int BaseLevel { get; set; }
    public int BaseHp { get; set; }
    public int BaseAttack { get; set; }
    public int BaseDefense { get; set; }
    public int BaseCopperMoney { get; set; }
    public int BaseExperience { get; set; }
    public decimal LevelGrowthRate { get; set; }
    public bool IsBoss { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>
/// 获取怪物模板响应
/// </summary>
public class GetMonsterTemplatesResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<MonsterTemplateData> Templates { get; set; } = new();

    public GetMonsterTemplatesResponse(bool success, string message, List<MonsterTemplateData> templates)
    {
        Success = success;
        Message = message;
        Templates = templates;
    }
}

// =================== 战斗结果相关数据类 ===================

public class CountrySummary
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Declaration { get; set; } = "";
    public string Announcement { get; set; } = "";
    public int CopperMoney { get; set; }
    public int Food { get; set; }
    public int Gold { get; set; }
}

public class PlayerSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Gender { get; set; } = "";
    public int Level { get; set; }
    public string TitleName { get; set; } = "";
    public string Office { get; set; } = "";
    public int CopperMoney { get; set; }
    public int Gold { get; set; }
    public int CountryId { get; set; } = -1;  // -1 表示没有国家
    public int ClanContribution { get; set; } = 0;  // 家族贡献值
    public string ClanRole { get; set; } = "成员";  // 家族职位（族长、副族长、精英、成员）
    public PlayerAttributesData Attributes { get; set; } = new();
    public string CountryName { get; set; } = "";
    public string CountryCode { get; set; } = "";
}

// =================== SignalR Hub 和事件消息 ===================

/// <summary>
/// SignalR Hub - 用于实时通信
/// </summary>
public class GameHub : Hub
{
    // 连接建立时调用
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        日志记录器.信息($"[SignalR] 客户端已连接: {Context.ConnectionId}");
    }

    // 连接断开时调用
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
        日志记录器.信息($"[SignalR] 客户端已断开: {Context.ConnectionId}");
    }

    // 加入家族组（当玩家加入家族时调用）
    public async Task JoinClanGroup(int clanId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"clan_{clanId}");
        日志记录器.信息($"[SignalR] 客户端 {Context.ConnectionId} 加入家族组: clan_{clanId}");
    }

    // 离开家族组（当玩家离开家族时调用）
    public async Task LeaveClanGroup(int clanId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"clan_{clanId}");
        日志记录器.信息($"[SignalR] 客户端 {Context.ConnectionId} 离开家族组: clan_{clanId}");
    }
}

/// <summary>
/// 简单的 WebSocket 连接管理器
/// 用于维护所有在线 WebSocket 连接，并向其广播事件（JSON 格式）
/// </summary>
public static class WebSocketConnectionManager
{
    // 使用 ConcurrentDictionary 存储所有在线的 WebSocket 连接
    // Key: WebSocket 实例, Value: 占位（未使用）
    private static readonly ConcurrentDictionary<WebSocket, byte> 连接集合 = new();
    
    // 连接的最后心跳时间记录
    // Key: WebSocket 实例, Value: 最后心跳时间
    private static readonly ConcurrentDictionary<WebSocket, DateTime> 连接心跳时间 = new();
    
    // WebSocket心跳超时时间（秒）- 如果超过这个时间没有心跳，断开连接
    private const int WebSocket心跳超时秒数 = 30; // 30秒无心跳则断开（客户端20秒发送一次，30秒足够）

    // JSON 序列化选项（与全局 JSON 配置保持一致：camelCase + 支持中文）
    private static readonly JsonSerializerOptions Json选项 = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 新连接加入
    /// </summary>
    public static void AddConnection(WebSocket socket)
    {
        if (socket == null) return;
        连接集合.TryAdd(socket, 0);
        连接心跳时间.AddOrUpdate(socket, DateTime.Now, (key, oldValue) => DateTime.Now);
    }
    
    /// <summary>
    /// 更新连接的心跳时间
    /// </summary>
    public static void UpdateHeartbeat(WebSocket socket)
    {
        if (socket == null) return;
        连接心跳时间.AddOrUpdate(socket, DateTime.Now, (key, oldValue) => DateTime.Now);
    }

    /// <summary>
    /// 连接断开时移除
    /// </summary>
    public static void RemoveConnection(WebSocket socket)
    {
        if (socket == null) return;
        连接集合.TryRemove(socket, out _);
        连接心跳时间.TryRemove(socket, out _);
    }
    
    /// <summary>
    /// 检查并清理超时的WebSocket连接
    /// </summary>
    public static async Task CheckAndCleanTimeoutConnections(ConcurrentDictionary<int, WebSocket> 玩家连接映射, ConcurrentDictionary<int, DateTime> 在线账号集合, string 数据库连接字符串)
    {
        var 当前时间 = DateTime.Now;
        var 需要断开的连接 = new List<WebSocket>();
        
        foreach (var kvp in 连接心跳时间)
        {
            var socket = kvp.Key;
            var 最后心跳时间 = kvp.Value;
            
            // 检查连接是否超时
            var 超时秒数 = (当前时间 - 最后心跳时间).TotalSeconds;
            if (超时秒数 > WebSocket心跳超时秒数)
            {
                需要断开的连接.Add(socket);
            }
            // 检查连接状态是否已关闭
            else if (socket.State != WebSocketState.Open)
            {
                需要断开的连接.Add(socket);
            }
        }
        
        // 断开超时连接
        foreach (var socket in 需要断开的连接)
        {
            try
            {
                日志记录器.信息($"[WebSocket] 检测到超时连接，正在断开...");
                
                // 从玩家连接映射中移除
                var 要移除的玩家ID列表 = new List<int>();
                foreach (var kvp in 玩家连接映射)
                {
                    if (kvp.Value == socket)
                    {
                        要移除的玩家ID列表.Add(kvp.Key);
                    }
                }
                foreach (var pid in 要移除的玩家ID列表)
                {
                    玩家连接映射.TryRemove(pid, out _);
                    
                    // 根据玩家ID查询账号ID，并从在线账号集合中移除
                    try
                    {
                        using var connection = new MySqlConnection(数据库连接字符串);
                        connection.Open();
                        using var command = new MySqlCommand(
                            "SELECT account_id FROM players WHERE id = @player_id LIMIT 1",
                            connection
                        );
                        command.Parameters.AddWithValue("@player_id", pid);
                        var accountIdResult = command.ExecuteScalar();
                        if (accountIdResult != null && !DBNull.Value.Equals(accountIdResult))
                        {
                            int 账号ID = Convert.ToInt32(accountIdResult);
                            在线账号集合.TryRemove(账号ID, out _);
                            日志记录器.信息($"[WebSocket] 超时断开：账号 {账号ID} (玩家 {pid}) 已从在线集合中移除");
                        }
                    }
                    catch (Exception ex)
                    {
                        日志记录器.错误($"[WebSocket] 查询账号ID失败: {ex.Message}");
                    }
                }
                
                // 关闭连接
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "心跳超时", CancellationToken.None);
                }
                
                // 从管理器中移除
                RemoveConnection(socket);
            }
            catch (Exception ex)
            {
                日志记录器.错误($"[WebSocket] 断开超时连接失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 向所有在线 WebSocket 客户端广播事件（如 ClanMemberKickedEvent 等）
    /// </summary>
    public static async Task BroadcastAsync(object eventData)
    {
        if (eventData == null) return;
        if (连接集合.IsEmpty) return;

        string json;
        try
        {
            json = JsonSerializer.Serialize(eventData, Json选项);
        }
        catch (Exception ex)
        {
            日志记录器.错误($"[WebSocket] 序列化事件失败: {ex.Message}");
            return;
        }

        var buffer = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(buffer);

        var 需要移除的连接 = new List<WebSocket>();

        foreach (var kvp in 连接集合.Keys)
        {
            var socket = kvp;
            if (socket.State != WebSocketState.Open)
            {
                需要移除的连接.Add(socket);
                continue;
            }

            try
            {
                await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                日志记录器.错误($"[WebSocket] 发送消息失败，移除连接: {ex.Message}");
                需要移除的连接.Add(socket);
            }
        }

        // 清理失效连接
        foreach (var socket in 需要移除的连接)
        {
            RemoveConnection(socket);
        }
    }

    /// <summary>
    /// 向指定玩家ID推送消息（定向推送）
    /// </summary>
    public static async Task SendToPlayerAsync(int playerId, object eventData, ConcurrentDictionary<int, WebSocket> 玩家连接映射)
    {
        if (eventData == null) return;
        if (!玩家连接映射.TryGetValue(playerId, out var socket)) return;
        if (socket == null || socket.State != WebSocketState.Open) return;

        string json;
        try
        {
            json = JsonSerializer.Serialize(eventData, Json选项);
        }
        catch (Exception ex)
        {
            日志记录器.错误($"[WebSocket] 序列化事件失败: {ex.Message}");
            return;
        }

        try
        {
            var buffer = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(buffer);
            await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            日志记录器.错误($"[WebSocket] 向玩家{playerId}发送消息失败: {ex.Message}");
            玩家连接映射.TryRemove(playerId, out _);
        }
    }

    /// <summary>
    /// 向所有在线 WebSocket 客户端广播事件，但排除指定玩家ID（用于聊天消息，避免发送者收到重复消息）
    /// </summary>
    public static async Task BroadcastExceptAsync(object eventData, int excludePlayerId, ConcurrentDictionary<int, WebSocket> 玩家连接映射)
    {
        if (eventData == null) return;
        if (连接集合.IsEmpty) return;

        string json;
        try
        {
            json = JsonSerializer.Serialize(eventData, Json选项);
        }
        catch (Exception ex)
        {
            日志记录器.错误($"[WebSocket] 序列化事件失败: {ex.Message}");
            return;
        }

        var buffer = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(buffer);

        var 需要移除的连接 = new List<WebSocket>();

        foreach (var kvp in 连接集合.Keys)
        {
            var socket = kvp;
            if (socket.State != WebSocketState.Open)
            {
                需要移除的连接.Add(socket);
                continue;
            }

            // 检查这个连接是否属于要排除的玩家
            bool 是否排除 = false;
            foreach (var kvp2 in 玩家连接映射)
            {
                if (kvp2.Value == socket && kvp2.Key == excludePlayerId)
                {
                    是否排除 = true;
                    break;
                }
            }

            if (是否排除) continue;

            try
            {
                await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                日志记录器.错误($"[WebSocket] 发送消息失败，移除连接: {ex.Message}");
                需要移除的连接.Add(socket);
            }
        }

        // 清理失效连接
        foreach (var socket in 需要移除的连接)
        {
            RemoveConnection(socket);
        }
    }
}

/// <summary>
/// 待处理消息（用于消息队列）
/// </summary>
public class 待处理消息
{
    public int 优先级 { get; set; } // 系统(0) > 家族(1) > 国家(2) > 世界(3)
    public string 频道类型 { get; set; } = ""; // "world", "country", "clan", "system"
    public int 玩家ID { get; set; }
    public string 玩家姓名 { get; set; } = "";
    public string 消息内容 { get; set; } = "";
    public int? 国家ID { get; set; }
    public int? 家族ID { get; set; }
    public int? 目标玩家ID { get; set; } // 系统消息的目标玩家ID
}

/// <summary>
/// 游戏事件消息基类
/// </summary>
public class GameEventMessage
{
    public string EventType { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 成员被踢出家族事件
/// </summary>
public class ClanMemberKickedEvent : GameEventMessage
{
    public int ClanId { get; set; }
    public int KickedPlayerId { get; set; }
    public string KickedPlayerName { get; set; } = "";
    public int OperatorId { get; set; }
    public string OperatorName { get; set; } = "";

    public ClanMemberKickedEvent()
    {
        EventType = "ClanMemberKicked";
    }
}

/// <summary>
/// 家族职位任命事件
/// </summary>
public class ClanRoleAppointedEvent : GameEventMessage
{
    public int ClanId { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string Role { get; set; } = "";
    public int OperatorId { get; set; }
    public string OperatorName { get; set; } = "";
    public int? ReplacedPlayerId { get; set; }  // 如果顶替了其他玩家，记录被顶替的玩家ID
    public string? ReplacedPlayerName { get; set; }

    public ClanRoleAppointedEvent()
    {
        EventType = "ClanRoleAppointed";
    }
}

/// <summary>
/// 家族解散事件
/// </summary>
public class ClanDisbandedEvent : GameEventMessage
{
    public int ClanId { get; set; }
    public string ClanName { get; set; } = "";
    public int OperatorId { get; set; }
    public string OperatorName { get; set; } = "";

    public ClanDisbandedEvent()
    {
        EventType = "ClanDisbanded";
    }
}

/// <summary>
/// 成员加入家族事件
/// </summary>
public class ClanMemberJoinedEvent : GameEventMessage
{
    public int ClanId { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = "";

    public ClanMemberJoinedEvent()
    {
        EventType = "ClanMemberJoined";
    }
}

/// <summary>
/// 成员离开家族事件
/// </summary>
public class ClanMemberLeftEvent : GameEventMessage
{
    public int ClanId { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = "";

    public ClanMemberLeftEvent()
    {
        EventType = "ClanMemberLeft";
    }
}

/// <summary>
/// 家族捐献事件
/// </summary>
public class ClanDonatedEvent : GameEventMessage
{
    public int ClanId { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public int DonationAmount { get; set; }
    public int FundsAdded { get; set; }
    public int ProsperityAdded { get; set; }

    public ClanDonatedEvent()
    {
        EventType = "ClanDonated";
    }
}

/// <summary>
/// 聊天消息事件
/// </summary>
public class ChatMessageEvent : GameEventMessage
{
    public string Channel { get; set; } = ""; // "world", "country", "clan"
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime MessageTime { get; set; } = DateTime.Now;

    public ChatMessageEvent()
    {
        EventType = "ChatMessage";
    }
}

/// <summary>
/// 系统消息事件
/// </summary>
public class SystemMessageEvent : GameEventMessage
{
    public string Message { get; set; } = "";
    public DateTime MessageTime { get; set; } = DateTime.Now;

    public SystemMessageEvent()
    {
        EventType = "SystemMessage";
    }
}

// =================== 家族日志相关请求和响应类 ===================

/// <summary>
/// 获取家族日志请求
/// </summary>
public class GetClanLogsRequest
{
    public int ClanId { get; set; }
}

/// <summary>
/// 家族日志数据
/// </summary>
public class ClanLogData
{
    public int Id { get; set; }
    public int ClanId { get; set; }
    public string OperationType { get; set; } = "";
    public int? OperatorId { get; set; }
    public string? OperatorName { get; set; }
    public int? TargetPlayerId { get; set; }
    public string? TargetPlayerName { get; set; }
    public string? Details { get; set; }
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 获取家族日志响应
/// </summary>
public class GetClanLogsResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<ClanLogData> Logs { get; set; } = new List<ClanLogData>();

    public GetClanLogsResponse(bool success, string message, List<ClanLogData> logs)
    {
        Success = success;
        Message = message;
        Logs = logs;
    }
}

// =================== 日志记录器 ===================
/// <summary>
/// 简单的日志记录器，支持同时输出到控制台和文件
/// </summary>
public static class 日志记录器
{
    private static readonly object 日志锁 = new object();
    private static readonly string 日志文件路径 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log.txt");
    private const long 最大日志文件大小 = 10 * 1024 * 1024; // 10MB，超过此大小会清空文件重新开始

    /// <summary>
    /// 记录信息日志
    /// </summary>
    public static void 信息(string 消息)
    {
        写入日志("INFO", 消息);
    }

    /// <summary>
    /// 记录警告日志
    /// </summary>
    public static void 警告(string 消息)
    {
        写入日志("WARN", 消息);
    }

    /// <summary>
    /// 记录错误日志
    /// </summary>
    public static void 错误(string 消息)
    {
        写入日志("ERROR", 消息);
    }

    /// <summary>
    /// 写入日志到文件（不输出到控制台）
    /// </summary>
    private static void 写入日志(string 级别, string 消息)
    {
        string 时间戳 = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string 日志内容 = $"[{时间戳}] [{级别}] {消息}";

        // 只写入文件，不输出到控制台（线程安全）
        lock (日志锁)
        {
            try
            {
                // 检查文件大小，如果超过限制则清空
                if (File.Exists(日志文件路径))
                {
                    var 文件信息 = new FileInfo(日志文件路径);
                    if (文件信息.Length > 最大日志文件大小)
                    {
                        File.WriteAllText(日志文件路径, $"[{时间戳}] [INFO] 日志文件已超过大小限制，清空重新开始\n", Encoding.UTF8);
                    }
                }

                // 追加写入日志文件
                File.AppendAllText(日志文件路径, 日志内容 + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 如果文件写入失败，静默处理（不输出任何信息，避免控制台刷屏）
                // 如果需要调试，可以查看文件系统权限或磁盘空间
            }
        }
    }
}

