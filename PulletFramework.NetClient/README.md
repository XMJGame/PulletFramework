# PulletFramework.NetClient

`PulletFramework.NetClient` 是面向 PulletNet 服务端的 Unity 客户端包。它内置
`PulletNet.ClientSDK.dll` 和 `PulletNet.Protocol.dll`，在其上提供 Unity 生命周期管理、
主线程事件派发、`ScriptableObject` 配置和局域网服务器发现。

本包不依赖 `PulletFramework` 主模块：既可以单独安装，也可以与其他 Pullet Framework
模块组合使用。服务端及跨平台 C# 网络内核仍使用 `PulletNet` 名称。

## 平台范围

- Windows、macOS、Linux、Android 和 iOS 是 ClientSDK 的 TCP、UDP、WebSocket 目标平台；
  这些平台的 Unity Player 实际连接测试尚未完成。
- 当前不支持 WebGL、微信小游戏和抖音小游戏。这些平台需要另行实现基于 JavaScript 或平台 SDK
  的 WebSocket 传输，不能直接使用本包现有的 `System.Net.Sockets` 实现。
- 局域网发现依赖 UDP 广播，因此也不适用于 WebGL 和小游戏。

运行时程序集已排除 WebGL，避免把当前不支持的传输实现打入对应 Player。
Unity 2022.3 下，单独安装以及与 `PulletFramework` 主模块并装的 EditMode 测试均已通过。

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

1. 在 Unity 菜单 `Assets/Create/Pullet Framework/NetClient Settings` 创建配置资产。
2. 在启动场景的 GameObject 上添加 `PulletNetworkManager`，并指定配置资产。
3. 订阅 `Connected`、`Disconnected`、`MessageReceived`，或在 Inspector 中绑定 UnityEvent。
4. 等待 `ConnectAsync` 成功，再通过 `SendAsync` 发送业务层自行编码的消息。

```csharp
using PulletFramework.NetClient;
using PulletNet.ClientSDK;

ConnectResult result = await manager.ConnectAsync();
if (result.IsSuccess)
    await manager.SendAsync(payload, ChannelType.ReliableOrdered);
```

包内的 **Connection Sample** 演示了连接、断开和发送 UTF-8 消息。它只是接入示例，
不定义业务消息格式。

## 职责边界

- `PulletNet.Protocol`：客户端与服务端共享的框架控制协议。
- `PulletNet.ClientSDK`：不依赖 Unity 的连接、传输与重连实现。
- `PulletFramework.NetClient`：Unity 侧的配置、生命周期和事件适配。
- 业务项目：消息 ID、序列化、鉴权和玩法消息处理。

本包与 `PulletFramework` 中原有的网络类是两条不同的接入路径。使用 PulletNet 服务端的
新项目应直接接入本包，不必让同一条网络消息再经过两套网络 API。
