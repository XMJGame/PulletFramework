# PulletFramework.NetClient

`PulletFramework.NetClient` 是面向 PulletNet 服务端的 Unity 客户端包。它内置
`PulletNet.ClientSDK.dll` 和 `PulletNet.Protocol.dll`，在其上提供 Unity 生命周期管理、
主线程事件派发、组件化 Inspector 配置和局域网服务器发现。

本包不依赖 `PulletFramework` 主模块：既可以单独安装，也可以与其他 Pullet Framework
模块组合使用。服务端及跨平台 C# 网络内核仍使用 `PulletNet` 名称。

## 平台范围

- Windows、macOS、Linux、Android 和 iOS 是 ClientSDK 的 TCP、UDP、WebSocket 目标平台；
  这些平台的 Unity Player 实际连接测试尚未完成。
- 当前不支持 WebGL、微信小游戏和抖音小游戏。这些平台需要另行实现基于 JavaScript 或平台 SDK
  的 WebSocket 传输，不能直接使用本包现有的 `System.Net.Sockets` 实现。
- 局域网发现依赖 UDP 广播，因此也不适用于 WebGL 和小游戏。

运行时程序集已排除 WebGL，避免把当前不支持的传输实现打入对应 Player。
Unity 2021.3 下，单独安装以及与 `PulletFramework` 主模块并装的 EditMode 测试均已通过。

## 安装

通过 Unity Package Manager 的 **Add package from git URL** 添加：

```text
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.NetClient
```

在本地开发 PulletNet 时，也可以通过 Package Manager 添加
`UnityPackages/PulletFramework.NetClient/package.json`。修改 `Protocol` 或 `ClientSDK` 源码后，
先重新构建包内 DLL，再同步到 PulletFramework 仓库及验证工程：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Build-PulletNetUnityPackage.ps1
```

## 使用

1. 在启动场景的 GameObject 上添加 `PulletNetworkManager`。
2. 直接在组件 Inspector 中填写地址、端口、连接模式与发现策略。
3. 订阅 `Connected`、`Disconnected`、`PayloadReceived`，或在 Inspector 中绑定公开的
   `OnConnected`、`OnConnectedFailed`、`OnDisconnected` UnityEvent。重连结果通过代码事件
   `ReconnectAttempt`、`Reconnected`、`ReconnectFailed` 交给业务层制定恢复策略。
4. 等待 `ConnectAsync` 或 `AutoConnectAsync` 成功，再通过 `SendAsync` 发送业务层自行编码的消息。

服务器地址由业务层决定：可以使用固定 IP、选择发现列表中的服务器，或使用 Manager 的自动发现流程。
`AutoConnectMode` 支持直接连接、优先发现、直连失败后发现。发现响应中的 `0.0.0.0`、回环地址会自动
替换为实际响应来源地址。收到 `PayloadReceived` 后若要跨帧或异步处理，
请先调用 `ReceivedPayload.ToOwned()` 并在用完后释放；Unity 适配层内部会持有并自动释放
跨线程所需的 `OwnedPayload`。

`enableConnectionLogs` 默认开启。Console 会明确输出当前 `AutoConnectMode`、固定地址连接、
从直连切换到发现、发现轮次、自动选中的服务器、连接/重连成功或失败等关键决策；不会打印每个
业务数据包。正式项目如果接入自己的日志系统，可以关闭该选项并监听相应事件。

断线后，ClientSDK 会按 `maxReconnectAttempts` 对当前服务器执行一轮重连，并通过
`ReconnectAttempt`、`Reconnected`、`ReconnectFailed` 报告结果。达到重连上限后是否继续连接、
是否重新发现或是否切换服务器属于应用业务策略，通用 Manager 不替业务作出决定。

主线程 Payload 队列默认上限为 1024，每帧最多派发 256 条，可直接在 `PulletNetworkManager`
中调整。`Unreliable`/`Sequenced` 满队列时丢弃较旧的非可靠数据；可靠通道满队列时不会
静默丢弃，而是累加 `ReliablePayloadOverflowCount` 并触发 `PayloadQueueFaulted` 与连接错误事件。
运行时可通过 `DroppedPayloadCount` 观察被替换或丢弃的非可靠 Payload 数量。
`PulletNetworkManager.IsConnected` 直接读取 ClientSDK 的 `ConnectionState`，Unity 层不再维护另一份
可能漂移的连接布尔值。

```csharp
using PulletFramework.NetClient;
using PulletNet.ClientSDK;

ConnectionResult result = await manager.ConnectAsync();
if (result.IsSuccess)
    await manager.SendAsync(payload, ChannelType.ReliableOrdered);

// 自动发现并连接；可通过 ServersDiscovered 获取完整列表供业务 UI 选择。
ConnectionResult autoResult = await manager.AutoConnectAsync();

// 供“附近服务器”界面使用：有限次数广播，可主动停止，并返回整个发现列表。
manager.DiscoverySucceeded += servers => ShowVehicleList(servers);
manager.DiscoveryFailed += error => ShowDiscoveryError(error);
manager.StartDiscovery();
// manager.StopDiscovery();
```

`PulletServerDiscovery` 同时提供静态 `StartDiscovery` / `StopDiscovery`，以及 `OnStarted`、
`OnAttempt`、`OnSuccess`、`OnFailure`、`OnStopped` 回调。`discoveryMaxAttempts` 只限制广播次数；
停止广播后仍会在 `discoveryTimeoutSeconds` 时间窗内接收响应，以免遗漏稍晚返回的服务器。
发现 JSON 统一使用官方 `com.unity.nuget.newtonsoft-json` 包解析。

包内的 **Connection Sample** 演示了连接、断开和发送 UTF-8 消息。它只是接入示例，
不定义业务消息格式。

## 职责边界

- `PulletNet.Protocol`：客户端与服务端共享的框架控制协议。
- `PulletNet.ClientSDK`：不依赖 Unity 的连接、传输与重连实现。
- `PulletFramework.NetClient`：Unity 侧的组件配置、发现、生命周期和事件适配。
- 业务项目：消息 ID、序列化、鉴权和玩法消息处理。

本包与 `PulletFramework` 中原有的网络类是两条不同的接入路径。使用 PulletNet 服务端的
新项目应直接接入本包，不必让同一条网络消息再经过两套网络 API。
