# Pullet Framework

> [!IMPORTANT]
> 这是作者用于个人学习、架构实验和 Unity 小游戏适配验证的公开仓库，不是稳定发行的商业框架或开箱即用的项目模板。当前 API、目录、配置和模块边界仍可能发生不兼容调整，请不要仅因为仓库公开就默认它已经适合生产环境。

Pullet Framework 正在探索一套面向 Unity 游戏、App 和微信/抖音小游戏的模块化基础架构。基础框架只保留通用运行时能力，资源管理、小游戏平台和 HybridCLR 作为可选模块按需组合。这些实现主要用于沉淀思路、验证技术路线和复盘实际问题，也欢迎作为学习参考。

当前版本处于 `0.x` 开发阶段，只维护 `main` 分支，尚未承诺稳定 API 或长期兼容性。项目已在 Unity `2022.3.62f3` 中完成三种全新 UPM 组合编译、Windows x64 Player 构建与运行，以及微信、抖音开发者工具基础运行验证；小游戏真机缓存、完整平台能力和性能仍待验收。

## 使用前须知

- **适合**：阅读实现、讨论架构、验证 YooAsset/CDN/小游戏流程，以及在可自行维护的实验项目中试用。
- **暂不建议直接用于**：无法接受破坏性变更、缺少独立回归能力或需要正式技术支持承诺的生产项目。
- 自动化测试和已记录的运行结果只覆盖文档注明的版本与场景，不代表所有 Unity、YooAsset、微信 SDK、TTSDK 或设备组合均已通过。
- 在实际项目中使用时，请自行审查源码、第三方依赖许可、安全配置和平台规则，并固定到经过自己验证的提交。
- 本仓库不提供稳定性、兼容性或持续维护承诺；已知边界和待验证事项以[开发状态文档](Documentation/DevelopmentStatus.zh-CN.md)为准。

## 模块

| 目录 | Package | 用途 | 是否必需 |
| --- | --- | --- | --- |
| `PulletFramework` | `com.xmjgame.pullet-framework` | Window、事件、状态机、网络、音频、对象池、设置和资源抽象 | 是 |
| `PulletFramework.AssetPublishing` | `com.xmjgame.pullet-framework.asset-publishing` | 编辑器资源发布与对象存储供应商抽象 | 否 |
| `PulletFramework.YooAsset` | `com.xmjgame.pullet-framework.yooasset` | YooAsset 初始化、多 Package 更新、构建与 CDN 发布 | 否 |
| `PulletMiniGame` | `com.xmjgame.pullet-minigame` | 微信、抖音平台能力、SDK 桥接和统一发布窗口 | 否 |
| `PulletFramework.HybridCLR` | `com.xmjgame.pullet-framework.hybridclr` | HybridCLR 编辑器工作流 | 否 |

推荐组合：

- 普通 Unity 游戏：`PulletFramework`
- 使用 YooAsset：`PulletFramework` + `PulletFramework.AssetPublishing` + `PulletFramework.YooAsset`
- 微信/抖音小游戏：在上一组合基础上增加 `PulletMiniGame`
- 需要代码热更新的 App：按需增加 `PulletFramework.HybridCLR`

## 安装

在 Unity Package Manager 中选择 **Add package from git URL**，按依赖顺序添加需要的模块：

```text
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.AssetPublishing
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.YooAsset
https://github.com/XMJGame/PulletFramework.git?path=/PulletMiniGame
```

以上地址跟随 `main`，适合体验最新开发状态。需要可重复构建时，应在 URL 末尾固定已自行验证的 commit，例如：

```text
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework#<commit>
```

当前尚未提供稳定版标签，不建议把未固定的 `main` 依赖直接用于正式项目。

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
- [资源发布模块](PulletFramework.AssetPublishing/README.md)
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

下一阶段是平台真机与性能验收：分别在抖音和微信确认首次下载、失败重试、二次启动缓存命中、前后台恢复和资源占用，并使用 Player Profiler 采集 CPU/GC 数据。具体进度以[开发状态文档](Documentation/DevelopmentStatus.zh-CN.md)为准。

## 许可说明

本仓库当前主要作为公开的个人学习记录，尚未附加开源许可证。仓库公开可见不等于已经授予复制、修改、分发或商用许可；第三方组件仍分别受其自身许可证约束。若后续决定正式开放使用范围，应再添加明确的 `LICENSE` 文件。
