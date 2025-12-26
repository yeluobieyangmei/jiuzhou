using MySql.Data.MySqlClient;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// 战场管理类：处理王城战宣战和倒计时逻辑
/// 所有王城战相关逻辑都在此类中实现
/// </summary>
public static class 战场管理
{
    // 每个国家的倒计时任务字典（Key: 国家ID, Value: 取消令牌源）
    private static readonly ConcurrentDictionary<int, CancellationTokenSource> 国家倒计时任务 = new();

    /// <summary>
    /// 处理宣战请求（所有逻辑都在这里）
    /// </summary>
    public static async Task<宣战处理结果> 处理宣战请求(
        int accountId,
        int countryId,
        int clanId,
        string 数据库连接字符串,
        ConcurrentDictionary<int, WebSocket> 玩家连接映射,
        ILogger 日志记录器)
    {
        try
        {
            // 1. 参数验证
            if (accountId <= 0 || countryId <= 0 || clanId <= 0)
            {
                return new 宣战处理结果(false, "参数无效", false);
            }

            using var connection = new MySqlConnection(数据库连接字符串);
            await connection.OpenAsync();

            // 开始事务
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // 2. 验证玩家是否存在且属于该家族
                int 玩家ID = -1;
                int? 玩家家族ID = null;
                using (var playerCmd = new MySqlCommand(
                    "SELECT id, clan_id FROM players WHERE account_id = @account_id LIMIT 1",
                    connection,
                    transaction))
                {
                    playerCmd.Parameters.AddWithValue("@account_id", accountId);
                    using var playerReader = await playerCmd.ExecuteReaderAsync();
                    if (await playerReader.ReadAsync())
                    {
                        玩家ID = playerReader.GetInt32(0);
                        玩家家族ID = playerReader.IsDBNull(1) ? null : playerReader.GetInt32(1);
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        return new 宣战处理结果(false, "玩家不存在", false);
                    }
                }

                // 3. 检查玩家是否属于请求的家族
                if (!玩家家族ID.HasValue || 玩家家族ID.Value != clanId)
                {
                    await transaction.RollbackAsync();
                    return new 宣战处理结果(false, "你不属于该家族", false);
                }

                // 4. 检查玩家是否是族长或副族长
                using (var roleCmd = new MySqlCommand(
                    @"SELECT leader_id, deputy_leader_id FROM clans WHERE id = @clan_id",
                    connection,
                    transaction))
                {
                    roleCmd.Parameters.AddWithValue("@clan_id", clanId);
                    using var roleReader = await roleCmd.ExecuteReaderAsync();
                    if (await roleReader.ReadAsync())
                    {
                        int 族长ID = roleReader.IsDBNull(0) ? -1 : roleReader.GetInt32(0);
                        int 副族长ID = roleReader.IsDBNull(1) ? -1 : roleReader.GetInt32(1);
                        
                        if (玩家ID != 族长ID && 玩家ID != 副族长ID)
                        {
                            await transaction.RollbackAsync();
                            return new 宣战处理结果(false, "只有族长或副族长可以宣战", false);
                        }
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        return new 宣战处理结果(false, "家族不存在", false);
                    }
                }

                // 5. 查询当前国家的宣战状态
                int? 宣战家族1ID = null;
                string? 宣战家族1名称 = null;
                int? 宣战家族2ID = null;
                string? 宣战家族2名称 = null;
                using (var countryCmd = new MySqlCommand(
                    @"SELECT c.war_clan1_id, c1.name, c.war_clan2_id, c2.name
                      FROM countries c
                      LEFT JOIN clans c1 ON c.war_clan1_id = c1.id
                      LEFT JOIN clans c2 ON c.war_clan2_id = c2.id
                      WHERE c.id = @country_id",
                    connection,
                    transaction))
                {
                    countryCmd.Parameters.AddWithValue("@country_id", countryId);
                    using var countryReader = await countryCmd.ExecuteReaderAsync();
                    if (await countryReader.ReadAsync())
                    {
                        宣战家族1ID = countryReader.IsDBNull(0) ? null : countryReader.GetInt32(0);
                        宣战家族1名称 = countryReader.IsDBNull(1) ? null : countryReader.GetString(1);
                        宣战家族2ID = countryReader.IsDBNull(2) ? null : countryReader.GetInt32(2);
                        宣战家族2名称 = countryReader.IsDBNull(3) ? null : countryReader.GetString(3);
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        return new 宣战处理结果(false, "国家不存在", false);
                    }
                }

                // 6. 检查是否已经有两个家族宣战
                if (宣战家族1ID.HasValue && 宣战家族2ID.HasValue)
                {
                    await transaction.RollbackAsync();
                    string 提示信息 = $"当前已有家族宣战：{宣战家族1名称} VS {宣战家族2名称}";
                    return new 宣战处理结果(false, 提示信息, false);
                }

                // 7. 检查是否已经宣战
                if (宣战家族1ID == clanId || 宣战家族2ID == clanId)
                {
                    await transaction.RollbackAsync();
                    return new 宣战处理结果(false, "你的家族已经宣战", false);
                }

                // 8. 获取家族名称
                string 家族名称 = "";
                using (var clanNameCmd = new MySqlCommand(
                    "SELECT name FROM clans WHERE id = @clan_id",
                    connection,
                    transaction))
                {
                    clanNameCmd.Parameters.AddWithValue("@clan_id", clanId);
                    var nameObj = await clanNameCmd.ExecuteScalarAsync();
                    if (nameObj != null)
                    {
                        家族名称 = nameObj.ToString() ?? "";
                    }
                }

                // 9. 记录宣战家族信息
                bool 两个家族都就绪 = false;
                if (!宣战家族1ID.HasValue)
                {
                    // 设置宣战家族1
                    using (var updateCmd = new MySqlCommand(
                        "UPDATE countries SET war_clan1_id = @clan_id WHERE id = @country_id",
                        connection,
                        transaction))
                    {
                        updateCmd.Parameters.AddWithValue("@clan_id", clanId);
                        updateCmd.Parameters.AddWithValue("@country_id", countryId);
                        await updateCmd.ExecuteNonQueryAsync();
                    }
                    日志记录器.LogInformation($"[战场管理] 第一个家族宣战 - 国家ID: {countryId}, 家族: {家族名称}({clanId})");
                }
                else
                {
                    // 设置宣战家族2
                    using (var updateCmd = new MySqlCommand(
                        "UPDATE countries SET war_clan2_id = @clan_id WHERE id = @country_id",
                        connection,
                        transaction))
                    {
                        updateCmd.Parameters.AddWithValue("@clan_id", clanId);
                        updateCmd.Parameters.AddWithValue("@country_id", countryId);
                        await updateCmd.ExecuteNonQueryAsync();
                    }
                    两个家族都就绪 = true;
                }

                // 提交事务
                await transaction.CommitAsync();

                // 10. 如果两个家族都就绪，启动倒计时
                if (两个家族都就绪)
                {
                    int 最终家族1ID = 宣战家族1ID ?? clanId;
                    int 最终家族2ID = clanId;
                    string 最终家族1名称 = 宣战家族1名称 ?? "";
                    string 最终家族2名称 = 家族名称;

                    日志记录器.LogInformation($"[战场管理] 两个家族都已宣战，准备启动倒计时 - 国家ID: {countryId}, 家族1: {最终家族1名称}({最终家族1ID}), 家族2: {最终家族2名称}({最终家族2ID})");

                    // 启动倒计时（在后台任务中运行，不阻塞当前请求）
                    _ = Task.Run(() => 启动倒计时(countryId, 最终家族1ID, 最终家族1名称, 最终家族2ID, 最终家族2名称, 数据库连接字符串, 玩家连接映射, 日志记录器));
                }

                return new 宣战处理结果(true, "宣战成功", 两个家族都就绪);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            日志记录器.LogError($"[战场管理] 处理宣战请求出错: {ex.Message}");
            return new 宣战处理结果(false, "服务器错误: " + ex.Message, false);
        }
    }

    /// <summary>
    /// 启动战场倒计时（30秒倒计时，每秒广播一次）
    /// </summary>
    private static void 启动倒计时(
        int 国家ID,
        int 家族1ID,
        string 家族1名称,
        int 家族2ID,
        string 家族2名称,
        string 数据库连接字符串,
        ConcurrentDictionary<int, WebSocket> 玩家连接映射,
        ILogger 日志记录器)
    {
        // 如果已有倒计时任务，先取消（确保每个国家只有一个倒计时任务）
        if (国家倒计时任务.TryGetValue(国家ID, out var 旧任务))
        {
            旧任务.Cancel();
            国家倒计时任务.TryRemove(国家ID, out _);
        }

        var 取消令牌源 = new CancellationTokenSource();
        国家倒计时任务.TryAdd(国家ID, 取消令牌源);

        日志记录器.LogInformation($"[战场管理] 准备启动倒计时任务 - 国家ID: {国家ID}");

        // 启动后台倒计时任务
        _ = Task.Run(async () =>
        {
            try
            {
                日志记录器.LogInformation($"[战场管理] 启动倒计时任务 - 国家ID: {国家ID}, 家族1: {家族1名称}({家族1ID}), 家族2: {家族2名称}({家族2ID})");
                
                DateTime 开始时间 = DateTime.Now;
                DateTime 结束时间 = 开始时间.AddSeconds(30);
                
                日志记录器.LogInformation($"[战场管理] 倒计时任务已进入循环，开始时间: {开始时间}, 结束时间: {结束时间}");
                
                // 等待1秒，给客户端时间注册WebSocket连接
                await Task.Delay(1000, 取消令牌源.Token);
                
                日志记录器.LogInformation($"[战场管理] 延迟1秒完成，开始广播循环");
                
                // 每秒广播一次倒计时
                while (!取消令牌源.Token.IsCancellationRequested)
                {
                    int 剩余秒数 = Math.Max(0, (int)Math.Ceiling((结束时间 - DateTime.Now).TotalSeconds));
                    
                    if (剩余秒数 <= 0)
                    {
                        // 倒计时结束，发送结束消息
                        日志记录器.LogInformation($"[战场管理] 国家 {国家ID} 的战场倒计时结束，发送结束消息");
                        await 广播倒计时结束(国家ID, 家族1ID, 家族1名称, 家族2ID, 家族2名称, 数据库连接字符串, 玩家连接映射, 日志记录器);
                        break;
                    }

                    日志记录器.LogInformation($"[战场管理] ========== 倒计时广播 ========== 国家ID: {国家ID}, 剩余秒数: {剩余秒数}秒, 家族1: {家族1名称}({家族1ID}) VS 家族2: {家族2名称}({家族2ID})");

                    // 广播倒计时给两个家族的所有在线成员
                    await 广播倒计时(国家ID, 家族1ID, 家族1名称, 家族2ID, 家族2名称, 剩余秒数, 数据库连接字符串, 玩家连接映射, 日志记录器);

                    日志记录器.LogInformation($"[战场管理] 广播完成，等待1秒后继续");

                    // 等待1秒
                    await Task.Delay(1000, 取消令牌源.Token);
                }

                // 倒计时结束后的处理
                日志记录器.LogInformation($"[战场管理] 国家 {国家ID} 的战场倒计时已结束，两个家族：{家族1名称} VS {家族2名称}");
            }
            catch (OperationCanceledException)
            {
                日志记录器.LogInformation($"[战场管理] 国家 {国家ID} 的倒计时已取消");
            }
            catch (Exception ex)
            {
                日志记录器.LogError($"[战场管理] 国家 {国家ID} 的倒计时任务出错: {ex.Message}");
                日志记录器.LogError($"[战场管理] 异常堆栈: {ex.StackTrace}");
            }
            finally
            {
                国家倒计时任务.TryRemove(国家ID, out _);
            }
        });
    }

    /// <summary>
    /// 广播战场倒计时给两个宣战家族的所有在线成员
    /// </summary>
    private static async Task 广播倒计时(
        int 国家ID,
        int 家族1ID,
        string 家族1名称,
        int 家族2ID,
        string 家族2名称,
        int 剩余秒数,
        string 数据库连接字符串,
        ConcurrentDictionary<int, WebSocket> 玩家连接映射,
        ILogger 日志记录器)
    {
        try
        {
            日志记录器.LogInformation($"[战场管理] 开始广播 - 国家ID: {国家ID}, 剩余秒数: {剩余秒数}");
            
            // 查询两个家族的所有玩家ID
            var 玩家ID列表 = new List<int>();
            using var connection = new MySqlConnection(数据库连接字符串);
            await connection.OpenAsync();

            // 查询家族1的玩家
            using (var cmd1 = new MySqlCommand("SELECT id FROM players WHERE clan_id = @clan_id", connection))
            {
                cmd1.Parameters.AddWithValue("@clan_id", 家族1ID);
                using var reader1 = await cmd1.ExecuteReaderAsync();
                while (await reader1.ReadAsync())
                {
                    玩家ID列表.Add(reader1.GetInt32(0));
                }
                await reader1.CloseAsync();
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
                await reader2.CloseAsync();
            }
            
            日志记录器.LogInformation($"[战场管理] 查询到玩家总数: {玩家ID列表.Count} (家族1: {家族1ID}, 家族2: {家族2ID})");
            日志记录器.LogInformation($"[战场管理] 玩家ID列表: {string.Join(", ", 玩家ID列表)}");
            日志记录器.LogInformation($"[战场管理] 当前连接映射中的玩家ID数量: {玩家连接映射.Count}");

            // 构建广播事件
            var 倒计时事件 = new
            {
                eventType = "BattlefieldCountdown",
                countryId = 国家ID,
                clan1Id = 家族1ID,
                clan1Name = 家族1名称,
                clan2Id = 家族2ID,
                clan2Name = 家族2名称,
                remainingSeconds = 剩余秒数
            };

            string 消息内容 = JsonSerializer.Serialize(倒计时事件);
            byte[] 消息字节 = Encoding.UTF8.GetBytes(消息内容);

            int 成功发送数 = 0;
            int 失败发送数 = 0;
            int 未找到连接数 = 0;
            int 连接已关闭数 = 0;

            // 向所有在线玩家发送
            foreach (var 玩家ID in 玩家ID列表)
            {
                if (玩家连接映射.TryGetValue(玩家ID, out var socket))
                {
                    if (socket == null)
                    {
                        未找到连接数++;
                        日志记录器.LogWarning($"[战场管理] 玩家 {玩家ID} 的连接为null");
                    }
                    else if (socket.State != WebSocketState.Open)
                    {
                        连接已关闭数++;
                        日志记录器.LogWarning($"[战场管理] 玩家 {玩家ID} 的连接状态为: {socket.State}");
                    }
                    else
                    {
                        try
                        {
                            await socket.SendAsync(new ArraySegment<byte>(消息字节), WebSocketMessageType.Text, true, CancellationToken.None);
                            成功发送数++;
                        }
                        catch (Exception ex)
                        {
                            失败发送数++;
                            日志记录器.LogError($"[战场管理] 发送给玩家 {玩家ID} 失败: {ex.Message}");
                        }
                    }
                }
                else
                {
                    未找到连接数++;
                }
            }

            // 记录每次广播的详细日志
            日志记录器.LogInformation($"[战场管理] ========== 广播统计 ========== 国家ID: {国家ID}, 剩余秒数: {剩余秒数}秒");
            日志记录器.LogInformation($"[战场管理] 查询到玩家总数: {玩家ID列表.Count}, 在线连接数: {玩家连接映射.Count}");
            日志记录器.LogInformation($"[战场管理] 发送结果: 成功 {成功发送数} 个, 失败 {失败发送数} 个, 未找到连接 {未找到连接数} 个, 连接已关闭 {连接已关闭数} 个");
            
            if (成功发送数 > 0)
            {
                日志记录器.LogInformation($"[战场管理] ✓ 成功向 {成功发送数} 个玩家发送了倒计时消息");
            }
            
            if (成功发送数 == 0 && 玩家ID列表.Count > 0)
            {
                日志记录器.LogWarning($"[战场管理] ⚠ 警告：查询到 {玩家ID列表.Count} 个玩家，但没有成功发送任何消息。玩家ID列表: {string.Join(", ", 玩家ID列表)}");
            }
        }
        catch (Exception ex)
        {
            日志记录器.LogError($"[战场管理] 国家 {国家ID} 广播失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 广播倒计时结束消息给两个宣战家族的所有在线成员
    /// </summary>
    private static async Task 广播倒计时结束(
        int 国家ID,
        int 家族1ID,
        string 家族1名称,
        int 家族2ID,
        string 家族2名称,
        string 数据库连接字符串,
        ConcurrentDictionary<int, WebSocket> 玩家连接映射,
        ILogger 日志记录器)
    {
        try
        {
            日志记录器.LogInformation($"[战场管理] 开始广播倒计时结束消息 - 国家ID: {国家ID}");
            
            // 查询两个家族的所有玩家ID
            var 玩家ID列表 = new List<int>();
            using var connection = new MySqlConnection(数据库连接字符串);
            await connection.OpenAsync();

            // 查询家族1的玩家
            using (var cmd1 = new MySqlCommand("SELECT id FROM players WHERE clan_id = @clan_id", connection))
            {
                cmd1.Parameters.AddWithValue("@clan_id", 家族1ID);
                using var reader1 = await cmd1.ExecuteReaderAsync();
                while (await reader1.ReadAsync())
                {
                    玩家ID列表.Add(reader1.GetInt32(0));
                }
                await reader1.CloseAsync();
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
                await reader2.CloseAsync();
            }
            
            日志记录器.LogInformation($"[战场管理] 查询到玩家总数: {玩家ID列表.Count} (家族1: {家族1ID}, 家族2: {家族2ID})");

            // 构建倒计时结束事件
            var 结束事件 = new
            {
                eventType = "BattlefieldCountdownEnd",
                countryId = 国家ID,
                clan1Id = 家族1ID,
                clan1Name = 家族1名称,
                clan2Id = 家族2ID,
                clan2Name = 家族2名称
            };

            string 消息内容 = JsonSerializer.Serialize(结束事件);
            byte[] 消息字节 = Encoding.UTF8.GetBytes(消息内容);

            int 成功发送数 = 0;
            int 失败发送数 = 0;
            int 未找到连接数 = 0;
            int 连接已关闭数 = 0;

            // 向所有在线玩家发送
            foreach (var 玩家ID in 玩家ID列表)
            {
                if (玩家连接映射.TryGetValue(玩家ID, out var socket))
                {
                    if (socket == null)
                    {
                        未找到连接数++;
                    }
                    else if (socket.State != WebSocketState.Open)
                    {
                        连接已关闭数++;
                    }
                    else
                    {
                        try
                        {
                            await socket.SendAsync(new ArraySegment<byte>(消息字节), WebSocketMessageType.Text, true, CancellationToken.None);
                            成功发送数++;
                        }
                        catch (Exception ex)
                        {
                            失败发送数++;
                            日志记录器.LogError($"[战场管理] 发送倒计时结束消息给玩家 {玩家ID} 失败: {ex.Message}");
                        }
                    }
                }
                else
                {
                    未找到连接数++;
                }
            }

            日志记录器.LogInformation($"[战场管理] 倒计时结束消息发送完成 - 国家ID: {国家ID}, 成功发送 {成功发送数} 个, 失败 {失败发送数} 个, 未找到连接 {未找到连接数} 个, 连接已关闭 {连接已关闭数} 个");
        }
        catch (Exception ex)
        {
            日志记录器.LogError($"[战场管理] 国家 {国家ID} 广播倒计时结束消息失败: {ex.Message}");
        }
    }
}

/// <summary>
/// 宣战处理结果
/// </summary>
public class 宣战处理结果
{
    public bool 成功 { get; set; }
    public string 消息 { get; set; }
    public bool 两个家族都就绪 { get; set; }

    public 宣战处理结果(bool 成功, string 消息, bool 两个家族都就绪)
    {
        this.成功 = 成功;
        this.消息 = 消息;
        this.两个家族都就绪 = 两个家族都就绪;
    }
}
