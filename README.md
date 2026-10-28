# Pullet Framework

面向 Unity 的模块化客户端框架，整理项目中重复出现的窗口、事件、状态机、资源生命周期、对象池和音频能力，并提供资源更新、小游戏适配与编辑器工具。

资源管理基于 YooAsset，代码热更新使用 HybridCLR，网络客户端对接 PulletNet。框架负责将这些能力接入项目，统一常用模块和编辑器工具。

## 能做什么

| 模块 | 主要能力 | 使用入口 |
| --- | --- | --- |
| PulletFramework | 窗口导航与缓存、事件、状态机、配置表、对象池、音频、设置及框架资源接口 | [核心模块](PulletFramework/README.md) |
| PulletFramework.YooAsset | 默认包与多资源包初始化、更新、下载、清理及构建 | [资源管理](PulletFramework.YooAsset/README.md) |
| PulletFramework.AssetPublishing | 编辑器资源发布、供应商接口、腾讯云 COS 与 CORS 配置 | [资源发布](PulletFramework.AssetPublishing/README.md) |
| PulletFramework.MiniGame | 微信/抖音平台接口、生命周期、登录、分享、广告和发布工具 | [小游戏适配](PulletFramework.MiniGame/README.md) |
| PulletFramework.HybridCLR | 热更新程序集构建、清单与运行时加载 | [代码热更新](PulletFramework.HybridCLR/README.md) |
| PulletFramework.NetClient | Unity 连接生命周期、多连接、主线程消息交付、消息会话和局域网发现 | [网络客户端](PulletFramework.NetClient/README.md) |

核心包可以单独使用；其他包按需安装。NetClient 本身不依赖核心框架，可以独立接入。

## 开始使用

在 Unity Package Manager 中选择 **Add package from git URL**。只体验基础能力时，先安装核心包：

```text
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework
```

由应用的唯一入口初始化；停止所有使用者后再关闭：

```csharp
PulletFramework.PulletFrameworks.Initialize();

// 应用退出时，先完成窗口、音频和其他资源使用者的关闭。
PulletFramework.PulletFrameworks.Destroy();
```

使用 YooAsset 时，先安装 YooAsset 与发布模块，再安装适配模块；运行时先准备默认资源包，再初始化框架。安装顺序和完整示例见[核心模块](PulletFramework/README.md)与[YooAsset 模块](PulletFramework.YooAsset/README.md)。

可选包的 Git URL：

```text
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.AssetPublishing
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.YooAsset
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.MiniGame
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.HybridCLR
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.NetClient
```

这些地址跟随 main。需要可重复构建时，在 URL 末尾添加 `#<commit>` 固定版本，并按包文档安装第三方依赖。YooAsset 适配使用 3.0.5；小游戏 SDK 和 HybridCLR 需另行安装。不要同时安装同一模块的两份源码。

## 统一编辑器工作台

安装核心包后，通过 `Pullets/Workspace` 打开工作台。Player 构建、资源包、对象存储发布、小游戏发布和 HybridCLR 页面随所安装的模块注册，不各自增加一组顶级菜单。

扩展自己的页面，只需在 Editor 程序集中实现 `IPulletWorkspaceModule`。使用方式见[工作台扩展](PulletFramework/README.md#编辑器工作台)。

## 状态与验证

已验证 Windows、Android、iOS，以及微信、抖音小游戏；小游戏尚未提交上线。另有 HybridCLR 的 Windows IL2CPP 与 NetClient 的 Editor/Windows Mono 测试记录。各模块的验证范围见测试记录。NetClient 暂不支持 WebGL、微信和抖音小游戏。

- [模块状态与路线图](Documentation/Roadmap.zh-CN.md)
- [验证范围与记录](Documentation/Validation.zh-CN.md)
- [验证工程](https://gitee.com/xu_mingjun/pullet-mini-game-validation)

## 文档导航

- [架构说明](Documentation/Architecture.zh-CN.md)
- [文档索引](Documentation/README.md)
- [NetClient 配置指南](PulletFramework.NetClient/Configuration.md)
- [微信与抖音常见问题](PulletFramework.MiniGame/Documentation/PlatformTroubleshooting.zh-CN.md)
- [YooAsset CDN 流程](PulletFramework.MiniGame/Documentation/YooAssetCdn.zh-CN.md)
- [维护约定](CONTRIBUTING.md)

## 许可

仓库尚未提供开源许可证，使用或再分发请先联系作者确认授权；第三方组件遵循各自许可证。
