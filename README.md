# Pullet Framework

Pullet Framework 是一套面向 Unity 游戏、App 和微信/抖音小游戏的模块化基础架构。基础框架只提供通用运行时能力，资源管理、小游戏平台和 HybridCLR 均为可选模块，可按项目需要组合安装。

当前版本处于 `0.x` 开发阶段，统一使用 `main` 分支。已在 Unity `2022.3.62f3` 验证工程中完成程序集编译和微信、抖音基础运行验证。

## 模块

| 目录 | Package | 用途 | 是否必需 |
| --- | --- | --- | --- |
| `PulletFramework` | `com.xmjgame.pullet-framework` | Window、事件、状态机、网络、音频、对象池、设置和资源抽象 | 是 |
| `PulletAssetPublishing` | `com.xmjgame.pullet-asset-publishing` | 编辑器资源发布与对象存储供应商抽象 | 否 |
| `PulletFramework.YooAsset` | `com.xmjgame.pullet-framework.yooasset` | YooAsset 初始化、多 Package 更新、构建与 CDN 发布 | 否 |
| `PulletMiniGame` | `com.xmjgame.pullet-minigame` | 微信、抖音平台能力、SDK 桥接和统一发布窗口 | 否 |
| `PulletFramework.HybridCLR` | `com.xmjgame.pullet-framework.hybridclr` | HybridCLR 编辑器工作流 | 否 |

推荐组合：

- 普通 Unity 游戏：`PulletFramework`
- 使用 YooAsset：`PulletFramework` + `PulletAssetPublishing` + `PulletFramework.YooAsset`
- 微信/抖音小游戏：在上一组合基础上增加 `PulletMiniGame`
- 需要代码热更新的 App：按需增加 `PulletFramework.HybridCLR`

## 安装

在 Unity Package Manager 中选择 **Add package from git URL**，按依赖顺序添加需要的模块：

```text
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework
https://github.com/XMJGame/PulletFramework.git?path=/PulletAssetPublishing
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.YooAsset
https://github.com/XMJGame/PulletFramework.git?path=/PulletMiniGame
```

`PulletFramework.YooAsset` 使用 YooAsset `3.0.5`。若项目无法从依赖声明自动解析 YooAsset，请先安装：

```text
https://github.com/tuyoogame/YooAsset.git?path=/Assets/YooAsset#3.0.5
```

微信官方转换 SDK、抖音 TTSDK 和 HybridCLR 需要按各自平台要求单独安装。不要同时导入两份相同模块源码，否则会产生重复程序集或类型冲突。

## 工作台

安装模块后，通过 Unity 菜单 `Pullets/Workspace` 进入统一工作台：

- **框架设置**：运行时日志等级等基础配置。
- **Player 构建**：Player 版本、场景和 Unity 构建入口。
- **YooAsset 资源**：运行模式、Package、CDN、构建和发布。
- **资源发布**：对象存储供应商及凭据配置。
- **小游戏发布**：微信/抖音切换、平台宏、SDK 参数和导出。

`PulletSettings.logLevel` 只过滤游戏运行时日志。编辑器构建、上传和配置错误始终可见。

## 架构边界

- `PulletResources` 只服务框架内部的 UI、Form、Sound 和 Pooling，不替代业务资源系统。
- 使用 YooAsset 时，业务通过 `PulletYooAssets` 管理 Package 生命周期，再直接使用 YooAsset `ResourcePackage` API 加载业务资源。
- Player 版本与 YooAsset Package 版本独立。纯资源更新不要求修改 Player 版本。
- `PulletMiniGame` 只负责平台能力；平台 SDK 的具体绑定位于独立桥接程序集或 Sample 中。
- `PulletPlayerPrefs` 默认使用 Unity 后端，小游戏初始化成功后可切换到微信或抖音存储后端，全程不使用反射。

## 文档

- [当前开发状态](Documentation/DevelopmentStatus.zh-CN.md)
- [基础框架](PulletFramework/README.md)
- [YooAsset 模块](PulletFramework.YooAsset/README.md)
- [小游戏平台模块](PulletMiniGame/README.md)
- [资源发布模块](PulletAssetPublishing/README.md)
- [HybridCLR 模块](PulletFramework.HybridCLR/README.md)
- [微信与抖音故障记录](PulletMiniGame/Documentation/PlatformTroubleshooting.zh-CN.md)
- [YooAsset CDN 流程](PulletMiniGame/Documentation/YooAssetCdn.zh-CN.md)

配套验证工程：<https://gitee.com/xu_mingjun/pullet-mini-game-validation>

## 开发约定

- 公共模块以本仓库为源头，再同步到验证工程，不在两处独立演化同一文件。
- 当前只维护 `main` 分支；重要阶段使用小而清晰的提交形成回退点。
- 未明确需要时，不反复执行耗时的 PC 或 WebGL 完整构建。
- 提交前至少完成相关程序集编译检查，并确认 Pullet 模块没有绕过 `PLogger` 直接输出日志。
- 不要将 COS、CDN、平台密钥或签名密码提交到公共仓库。已经进入 Git 历史的凭据必须轮换。

## 当前重点

下一阶段是完成 YooAsset + COS 的端到端验证：发布测试 Package，分别在抖音和微信确认首次下载、失败重试、二次启动缓存命中及真机性能数据。具体进度以[开发状态文档](Documentation/DevelopmentStatus.zh-CN.md)为准。
