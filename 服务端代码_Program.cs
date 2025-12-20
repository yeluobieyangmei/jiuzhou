using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);

// 添加控制器支持，配置 JSON 选项（支持 camelCase 和 UTF-8 编码）
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping; // 支持中文字符
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[清理超时账号任务] 错误: {ex.Message}");
        }
    }
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// =================== 登录接口：POST /api/login ===================

app.MapPost("/api/login", async ([FromBody] LoginRequest 请求) =>
{
    try
    {
        // 连接 MySQL 数据库
        using var connection = new MySqlConnection(数据库连接字符串);
        await connection.OpenAsync();

        // 查询账号信息（包括失败次数和锁定时间）
        using var command = new MySqlCommand(
            "SELECT id, password_hash, failed_login_count, locked_until FROM accounts WHERE username = @username",
            connection
        );
        command.Parameters.AddWithValue("@username", 请求.Username);

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
            string 输入密码哈希 = 计算SHA256(请求.Password);

            if (数据库中的密码哈希 == 输入密码哈希)
            {
                // 密码正确，检查账号是否已在线
                if (在线账号集合.ContainsKey(账号ID))
                {
                    reader.Close();
                    return Results.Ok(new LoginResponse(false, "当前账号已在线，禁止重复登录！", "", -1));
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
                p.id, p.name, p.gender, p.level, p.title_name, p.office,
                p.copper_money, p.gold, p.country_id, p.clan_id,
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
            // SQL 查询顺序：p.id(0), p.name(1), p.gender(2), p.level(3), p.title_name(4), p.office(5),
            // p.copper_money(6), p.gold(7), p.country_id(8), p.clan_id(9),
            // pa.max_hp(10), pa.current_hp(11), pa.attack(12), pa.defense(13), pa.crit_rate(14),
            // c.id(15), c.name(16), c.code(17), cl.id(18), cl.name(19)
            var 玩家数据 = new PlayerData
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Gender = reader.GetString(2),
                Level = reader.GetInt32(3),
                TitleName = reader.GetString(4),
                Office = reader.GetString(5),
                CopperMoney = reader.GetInt32(6),
                Gold = reader.GetInt32(7),
                CountryId = reader.IsDBNull(8) ? -1 : reader.GetInt32(8),  // Unity JsonUtility 不支持可空类型，用 -1 表示 null
                ClanId = reader.IsDBNull(9) ? -1 : reader.GetInt32(9),    // Unity JsonUtility 不支持可空类型，用 -1 表示 null
                Attributes = new PlayerAttributesData
                {
                    MaxHp = reader.GetInt32(10),
                    CurrentHp = reader.GetInt32(11),
                    Attack = reader.GetInt32(12),
                    Defense = reader.GetInt32(13),
                    CritRate = reader.GetFloat(14)
                },
                Country = reader.IsDBNull(15) ? null : new CountryData
                {
                    Id = reader.GetInt32(15),
                    Name = reader.GetString(16),
                    Code = reader.GetString(17)
                },
                Clan = reader.IsDBNull(18) ? null : new ClanData
                {
                    Id = reader.GetInt32(18),
                    Name = reader.GetString(19)
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
                @"INSERT INTO players (account_id, name, gender, level, title_name, office, copper_money, gold)
                  VALUES (@account_id, @name, @gender, 1, '无', '国民', 50000000, 2000000)",
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
            return Results.Ok(new GetCountryInfoResponse(false, "国家ID无效", 0, 0));
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
                return Results.Ok(new GetCountryInfoResponse(false, "指定的国家不存在", 0, 0));
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

        return Results.Ok(new GetCountryInfoResponse(true, "获取国家信息成功", 成员总数, 排名));
    }
    catch (Exception ex)
    {
        return Results.Ok(new GetCountryInfoResponse(false, "服务器错误: " + ex.Message, 0, 0));
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
                p.copper_money, p.gold,
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
                Attributes = new PlayerAttributesData
                {
                    MaxHp = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    CurrentHp = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                    Attack = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                    Defense = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                    CritRate = reader.IsDBNull(12) ? 0f : reader.GetFloat(12)
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
                p.copper_money, p.gold, p.country_id,
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
                Attributes = new PlayerAttributesData
                {
                    MaxHp = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                    CurrentHp = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                    Attack = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                    Defense = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                    CritRate = reader.IsDBNull(13) ? 0f : reader.GetFloat(13)
                },
                CountryName = reader.IsDBNull(14) ? "" : reader.GetString(14),
                CountryCode = reader.IsDBNull(15) ? "" : reader.GetString(15)
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
                    玩家职位 = roleResult.ToString() ?? "成员";
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

            // 4.2 将该家族所有成员的 clan_id 设置为 NULL，并记录族长解散家族的时间（用于冷却时间）
            using var updateMembersCommand = new MySqlCommand(
                @"UPDATE players 
                  SET clan_id = NULL,
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

            // 4.4 删除家族记录（外键会自动处理相关数据）
            using var deleteClanCommand = new MySqlCommand(
                "DELETE FROM clans WHERE id = @clan_id",
                connection,
                transaction
            );
            deleteClanCommand.Parameters.AddWithValue("@clan_id", 家族ID.Value);
            await deleteClanCommand.ExecuteNonQueryAsync();

            // 提交事务
            await transaction.CommitAsync();

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
            // 4.1 扣除玩家铜钱并更新最后捐献日期
            using var updatePlayerCommand = new MySqlCommand(
                @"UPDATE players 
                  SET copper_money = copper_money - @cost_copper, 
                      last_donate_date = CURDATE() 
                  WHERE id = @player_id",
                connection,
                transaction
            );
            updatePlayerCommand.Parameters.AddWithValue("@cost_copper", 捐献消耗铜钱);
            updatePlayerCommand.Parameters.AddWithValue("@player_id", 玩家ID);
            await updatePlayerCommand.ExecuteNonQueryAsync();

            // 4.2 增加家族资金（+100）和繁荣值（+10）
            using var updateClanCommand = new MySqlCommand(
                @"UPDATE clans 
                  SET funds = funds + 100, 
                      prosperity = prosperity + 10 
                  WHERE id = @clan_id",
                connection,
                transaction
            );
            updateClanCommand.Parameters.AddWithValue("@clan_id", 家族ID.Value);
            await updateClanCommand.ExecuteNonQueryAsync();

            // 提交事务
            await transaction.CommitAsync();

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

public record GetCountryInfoResponse(bool Success, string Message, int MemberCount, int Rank);

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

public record HeartbeatResponse(bool Success, string Message);

public record GetClanInfoRequest(int ClanId, int PlayerId);

public record GetClanInfoResponse(bool Success, string Message, ClanInfoData? Data);

public record DonateClanRequest(int AccountId);

public record DonateClanResponse(bool Success, string Message, bool AlreadyDonated);

public record CheckDonateStatusRequest(int AccountId);

public record CheckDonateStatusResponse(bool Success, string Message, bool AlreadyDonated);

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
    public string TitleName { get; set; } = "";
    public string Office { get; set; } = "";
    public int CopperMoney { get; set; }
    public int Gold { get; set; }
    public int CountryId { get; set; } = -1;  // -1 表示没有国家/家族
    public int ClanId { get; set; } = -1;     // -1 表示没有国家/家族
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
    public PlayerAttributesData Attributes { get; set; } = new();
    public string CountryName { get; set; } = "";
    public string CountryCode { get; set; } = "";
}

