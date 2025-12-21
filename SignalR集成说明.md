# SignalR 集成说明

## 概述

本项目已实现 WebSocket + SignalR 实时通信方案，用于实现强联网模式，确保所有客户端都能及时收到服务器推送的事件通知。

## 服务器端实现状态

✅ **已完成**
- SignalR 服务配置
- GameHub 创建
- 事件类型定义（6种事件）
- 操作接口中的事件广播（踢出、任命、解散、加入、离开、捐献）

## 客户端实现状态

⚠️ **需要安装 SignalR Unity 客户端库**

客户端代码框架已创建（`SignalR连接管理.cs`），但需要安装 SignalR Unity 客户端库后才能正常工作。

## 安装 SignalR Unity 客户端库

### 方法1：使用 NuGet for Unity（推荐）

1. 安装 NuGet for Unity 包管理器
2. 在 Unity 中打开 NuGet 包管理器
3. 搜索并安装 `Microsoft.AspNetCore.SignalR.Client`

### 方法2：手动下载 DLL

1. 从 NuGet 下载 `Microsoft.AspNetCore.SignalR.Client` 包
2. 解压并找到以下 DLL 文件：
   - `Microsoft.AspNetCore.SignalR.Client.dll`
   - `Microsoft.AspNetCore.SignalR.Protocols.Json.dll`
   - `System.Threading.Channels.dll`
   - 其他依赖项
3. 将 DLL 文件复制到 Unity 项目的 `Assets/Plugins/` 目录

### 方法3：使用 Unity Package Manager（如果可用）

某些 SignalR Unity 客户端库可能通过 UPM 提供，可以尝试搜索相关包。

## 客户端代码集成步骤

### 1. 启用 SignalR 连接管理器

在 Unity 场景中创建一个空的 GameObject，挂载 `SignalR连接管理` 脚本。

### 2. 修改 SignalR连接管理.cs

找到所有 `// TODO:` 注释的地方，根据实际使用的 SignalR 客户端库实现连接逻辑。

示例代码（使用 Microsoft.AspNetCore.SignalR.Client）：

```csharp
using Microsoft.AspNetCore.SignalR.Client;

// 在类中添加字段
private HubConnection? hubConnection;

// 在 建立连接() 方法中：
hubConnection = new HubConnectionBuilder()
    .WithUrl(hubUrl)
    .Build();

// 注册事件处理方法
hubConnection.On<GameEventMessage>("OnGameEvent", (eventMessage) =>
{
    UnityMainThreadDispatcher.Instance().Enqueue(() =>
    {
        处理游戏事件(eventMessage);
    });
});

// 开始连接
await hubConnection.StartAsync();
是否已连接 = true;
```

### 3. 在玩家数据管理中集成

已在 `玩家数据管理.cs` 中添加了 SignalR 连接启动逻辑：
- 登录成功后自动建立连接
- 如果玩家有家族，自动加入家族组

### 4. 在家族操作中集成

需要在以下操作成功后调用 SignalR 方法：
- **加入家族**：调用 `SignalR连接管理.实例.加入家族组(家族ID)`
- **离开家族**：调用 `SignalR连接管理.实例.离开家族组(家族ID)`
- **解散家族**：调用 `SignalR连接管理.实例.离开家族组(家族ID)`

## 事件类型

服务器会推送以下6种事件：

1. **ClanMemberKicked** - 成员被踢出家族
2. **ClanRoleAppointed** - 家族职位任命
3. **ClanDisbanded** - 家族解散
4. **ClanMemberJoined** - 成员加入家族
5. **ClanMemberLeft** - 成员离开家族
6. **ClanDonated** - 家族捐献

## 测试步骤

1. 确保服务器端已启动并运行
2. 安装 SignalR Unity 客户端库
3. 在 Unity 中创建 SignalR连接管理 GameObject
4. 运行游戏并登录
5. 测试各种家族操作，验证事件推送是否正常工作

## 注意事项

1. **主线程调用**：SignalR 事件回调可能在后台线程执行，需要使用 Unity 主线程调度器来更新 UI
2. **连接管理**：确保连接断开时正确处理，实现自动重连机制
3. **错误处理**：添加完善的错误处理和日志记录
4. **性能优化**：大量事件时考虑批量处理或节流

## 下一步优化建议

1. 实现自动重连机制
2. 添加连接状态指示器
3. 实现消息队列（离线时缓存消息）
4. 添加事件去重机制
5. 性能监控和优化


