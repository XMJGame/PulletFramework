# 架构与设计取舍

## 模块边界

PulletFramework 以 UPM 包提供安装边界，以 asmdef 提供程序集边界。安装包不等于一个不可拆分的运行时：例如 NetClient 合并了安装入口，但保留连接、消息、发现及组合适配的内部程序集。

| 模块 | 负责什么 | 不负责什么 |
| --- | --- | --- |
| Core | Window、事件、状态机、Form、Sound、Pooling、设置和框架资源接口 | 游戏业务流程、完整业务资源系统、具体小游戏 SDK |
| YooAsset | Package 初始化、清单更新、下载与释放，以及框架资源适配 | 重写 YooAsset 的业务资源 API |
| AssetPublishing | 编辑器发布会话、供应商配置与对象存储实现 | 在 Player 中加载发布凭据 |
| MiniGame | 平台接口、适配器生命周期、编辑器配置与发布 | 把所有平台差异伪装成一致能力 |
| HybridCLR | 构建与加载热更新清单、程序集和补充元数据 | 取代 HybridCLR 或默认启用小游戏代码热更新 |
| NetClient | Unity 连接门面、多连接与主线程派发、可选发现和消息组合 | 登录鉴权、房间、AOI、业务序列化或玩法 |

包的具体依赖以各自 package.json 与 asmdef 为准。YooAsset 适配依赖 Core、AssetPublishing 和 YooAsset；MiniGame 依赖 Core 与 AssetPublishing，具体 SDK 桥接按平台安装。NetClient 不依赖 Core、YooAsset 或 MiniGame。

## 为什么窗口逻辑不放进每个MonoBehaviour

[UIWindow](../PulletFramework/Runtime/Window/UIWindow.cs)是普通 C# 窗口基类，统一导航、显示、缓存与关闭。项目视图仍由 Unity 对象和组件组成；“少继承 MonoBehaviour”不是禁止使用组件，而是不让每个视图再实现一套重复生命周期。

每次打开有独立身份，异步结果必须属于对应打开操作。窗口已关闭或重新打开后，旧加载结果不能完成新操作。项目仍需明确按钮监听、业务任务与资源句柄的持有者，框架不会代替业务处理全部关闭关系。

## 为什么保留两种资源接口

`PulletResources`只满足框架内部 UI、Form、Sound、Pooling 的资源需求。适配器把底层资源系统接进来，核心包不必依赖 YooAsset。

业务资源使用 YooAsset 时：

1. 通过 `PulletYooAssets`准备并管理 Package。
2. 直接使用 `ResourcePackage`加载业务资源。
3. 由业务 owner 释放句柄，先停止使用者，再卸载资源包。

这样不必维护一套与 YooAsset 重复的业务加载 API。Player 版本和 Package 版本也独立管理，纯资源更新不要求修改应用版本。

## 可选平台与编辑器扩展

小游戏核心接口与 SDK 桥接分开：微信开放数据域、抖音托管排行榜等差异保留在各自接口和实现中。其他平台不承担不需要的 SDK 引用。

编辑器工作台通过[IPulletWorkspaceModule](../PulletFramework/Editor/Workspace/Contracts/IPulletWorkspaceModule.cs)与 TypeCache 发现页面。扩展模块引用工作台契约，核心无需逐个引用 YooAsset、MiniGame 或业务编辑器。

## 异步与所有权

取消是调用者意图，不等于底层任务已经退出。资源加载、实例创建或网络关闭可能继续完成，owner需要观察终态，并清理迟到结果。

具体实现各有约束：

- Window：打开代次区分旧操作与新窗口。
- Sound：异步播放请求按代次判断，应用后台暂停与通道暂停分开记录。
- Pooling：实例与资源句柄分开管理，Spawner 销毁后不再接纳新工作。
- MiniGame：共享初始化有明确会话，旧平台结果不能覆盖新平台的存储后端。
- NetClient：SDK负责连接生命周期，Unity层负责主线程交付与其预算，项目负责业务协议与业务任务。

相关测试见[测试记录](Validation.zh-CN.md)。

## 网络模块的维护来源

NetClient 使用 PulletNet 的 UnityPackages 作为开发源，本仓库存放安装副本，避免两处独立修改同一实现。版本号相同不一定表示文件完全相同，升级时要同时核对包内 NativeInput 和源码说明。

发现和消息模块保留独立程序集，但使用者只需安装一个 NetClient UPM 包。底层网络算法属于 PulletNet；此处的设计重点是 Unity 生命周期、线程切换、可选组合与使用入口。
