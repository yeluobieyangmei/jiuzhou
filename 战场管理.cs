using MySql.Data.MySqlClient;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// 战场管理类：处理王城战宣战和倒计时逻辑
/// </summary>
public static class 战场管理
{
    // 战场倒计时任务字典（Key: 国家ID, Value: 取消令牌源）
    private static readonly ConcurrentDictionary<int, CancellationTokenSource> 战场倒计时任务 = new();

    /// <summary>
    /// 启动战场倒计时（30秒倒计时，每秒广播一次）
    /// </summary>
    public static void 启动倒计时(
        int 国家ID, 
        int 家族1ID, 
        string 家族1名称, 
        int 家族2ID, 
        string 家族2名称,
        string 数据库连接字符串,
        ConcurrentDictionary<int, WebSocket> 玩家连接映射,
        ILogger 日志记录器)
    {
        // 如果已有倒计时任务，先取消
        if (战场倒计时任务.TryGetValue(国家ID, out var 旧任务))
        {
            旧任务.Cancel();
            战场倒计时任务.TryRemove(国家ID, out _);
        }

        var 取消令牌源 = new CancellationTokenSource();
        战场倒计时任务.TryAdd(国家ID, 取消令牌源);

        日志记录器.LogInformation($"[战场倒计时] 准备启动后台任务 - 国家ID: {国家ID}");

        // 启动后台倒计时任务
        var 任务 = Task.Run(async () =>
        {
            try
            {
                日志记录器.LogInformation($"[战场倒计时] 启动倒计时任务 - 国家ID: {国家ID}, 家族1: {家族1名称}({家族1ID}), 家族2: {家族2名称}({家族2ID})");
                
                DateTime 开始时间 = DateTime.Now;
                DateTime 结束时间 = 开始时间.AddSeconds(30);
                
                日志记录器.LogInformation($"[战场倒计时] 倒计时任务已进入循环，开始时间: {开始时间}, 结束时间: {结束时间}");
                
                // 等待1秒，给客户端时间注册WebSocket连接
                await Task.Delay(1000, 取消令牌源.Token);
                
                日志记录器.LogInformation($"[战场倒计时] 延迟1秒完成，开始广播循环");
                
                // 每秒广播一次倒计时
                while (!取消令牌源.Token.IsCancellationRequested)
                {
                    int 剩余秒数 = Math.Max(0, (int)Math.Ceiling((结束时间 - DateTime.Now).TotalSeconds));
                    
                    if (剩余秒数 <= 0)
                    {
                        // 倒计时结束
                        日志记录器.LogInformation($"[战场倒计时] 国家 {国家ID} 的战场倒计时结束");
                        break;
                    }

                    日志记录器.LogInformation($"[战场倒计时] 准备广播，剩余秒数: {剩余秒数}");

                    // 广播倒计时给两个家族的所有在线成员
                    await 广播倒计时(国家ID, 家族1ID, 家族1名称, 家族2ID, 家族2名称, 剩余秒数, 数据库连接字符串, 玩家连接映射, 日志记录器);

                    日志记录器.LogInformation($"[战场倒计时] 广播完成，等待1秒后继续");

                    // 等待1秒
                    await Task.Delay(1000, 取消令牌源.Token);
                }

                // 倒计时结束后的处理（这里可以添加生成战场的逻辑）
                日志记录器.LogInformation($"[战场倒计时] 国家 {国家ID} 的战场倒计时已结束，两个家族：{家族1名称} VS {家族2名称}");
            }
            catch (OperationCanceledException)
            {
                日志记录器.LogInformation($"[战场倒计时] 国家 {国家ID} 的倒计时已取消");
            }
            catch (Exception ex)
            {
                日志记录器.LogError($"[战场倒计时] 国家 {国家ID} 的倒计时任务出错: {ex.Message}");
                日志记录器.LogError($"[战场倒计时] 异常堆栈: {ex.StackTrace}");
            }
            finally
            {
                战场倒计时任务.TryRemove(国家ID, out _);
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
            日志记录器.LogInformation($"[战场倒计时广播] 开始广播 - 国家ID: {国家ID}, 剩余秒数: {剩余秒数}");
            
            // 查询两个家族的所有玩家ID
            using var connection = new MySqlConnection(数据库连接字符串);
            日志记录器.LogInformation($"[战场倒计时广播] 正在连接数据库...");
            await connection.OpenAsync();
            日志记录器.LogInformation($"[战场倒计时广播] 数据库连接成功");

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
                await reader1.CloseAsync(); // 显式关闭reader
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
                await reader2.CloseAsync(); // 显式关闭reader
            }
            
                   日志记录器.LogInformation($"[战场倒计时广播] 查询到玩家总数: {玩家ID列表.Count} (家族1: {家族1ID}, 家族2: {家族2ID})");
                   日志记录器.LogInformation($"[战场倒计时广播] 玩家ID列表: {string.Join(", ", 玩家ID列表)}");
                   日志记录器.LogInformation($"[战场倒计时广播] 当前连接映射中的玩家ID数量: {玩家连接映射.Count}");
                   日志记录器.LogInformation($"[战场倒计时广播] 连接映射中的玩家ID: {string.Join(", ", 玩家连接映射.Keys)}");

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
                               日志记录器.LogWarning($"[战场倒计时广播] 玩家 {玩家ID} 的连接为null");
                           }
                           else if (socket.State != WebSocketState.Open)
                           {
                               连接已关闭数++;
                               日志记录器.LogWarning($"[战场倒计时广播] 玩家 {玩家ID} 的连接状态为: {socket.State}");
                           }
                           else
                           {
                               try
                               {
                                   await socket.SendAsync(new ArraySegment<byte>(消息字节), WebSocketMessageType.Text, true, CancellationToken.None);
                                   成功发送数++;
                                   日志记录器.LogInformation($"[战场倒计时广播] 成功发送给玩家 {玩家ID}");
                               }
                               catch (Exception ex)
                               {
                                   失败发送数++;
                                   日志记录器.LogError($"[战场倒计时广播] 发送给玩家 {玩家ID} 失败: {ex.Message}");
                               }
                           }
                       }
                       else
                       {
                           未找到连接数++;
                           日志记录器.LogWarning($"[战场倒计时广播] 玩家 {玩家ID} 不在连接映射中");
                       }
                   }

            // 记录每次广播的日志（用于调试）
            日志记录器.LogInformation($"[战场倒计时广播] 国家 {国家ID} 剩余 {剩余秒数} 秒，查询到 {玩家ID列表.Count} 个玩家，在线连接数 {玩家连接映射.Count}，成功发送 {成功发送数} 个，失败 {失败发送数} 个，未找到连接 {未找到连接数} 个，连接已关闭 {连接已关闭数} 个");
            
            // 如果成功发送数为0，记录警告
            if (成功发送数 == 0 && 玩家ID列表.Count > 0)
            {
                日志记录器.LogWarning($"[战场倒计时广播] 警告：查询到 {玩家ID列表.Count} 个玩家，但没有成功发送任何消息。玩家ID列表: {string.Join(", ", 玩家ID列表)}，连接映射中的玩家ID: {string.Join(", ", 玩家连接映射.Keys)}");
            }
        }
        catch (Exception ex)
        {
            日志记录器.LogError($"[战场倒计时广播] 国家 {国家ID} 广播失败: {ex.Message}");
        }
    }
}

