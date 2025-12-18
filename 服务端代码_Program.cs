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
                // 密码正确，登录成功
                // 重置失败次数和锁定时间
                reader.Close(); // 先关闭 reader，才能执行更新
                using var updateCommand = new MySqlCommand(
                    "UPDATE accounts SET failed_login_count = 0, locked_until = NULL WHERE id = @account_id",
                    connection
                );
                updateCommand.Parameters.AddWithValue("@account_id", 账号ID);
                await updateCommand.ExecuteNonQueryAsync();

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

