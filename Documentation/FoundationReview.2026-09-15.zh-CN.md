# Pullet 模块基础审查

审查日期：2026-09-15。本文保留首次审查时的问题与证据；F01-F07 后续均已完成实现加固和自动化回归，并于 `main@e670808` 发布。平台真机与性能项目仍以加固计划中的待验证清单为准，不要把本文理解为全部验收通过。

## 范围与证据

- 主要审查对象：`G:/XuMingJun/2026/Mini_Games/PulletMiniGameValidation/Assets` 中的 PulletFramework、PulletFramework.YooAsset、PulletFramework.AssetPublishing、PulletFramework.MiniGame。
- 对照源仓库：`G:/GitHub/PulletFramework`。HybridCLR 未安装在本轮验证工程中，仅查看其源码和包声明，没有重新安装或验证。
- 下文源码位置均相对于验证工程 `Assets/`，行号是首次审查快照，仅用于定位历史问题。
- 首次审查本身没有改运行时实现、重新构建小游戏或上传 CDN；后续修复与验证过程记录在加固计划中。
- 已执行的复现：将实际 `PulletAsyncOperation.cs` 与最小测试代码通过 PowerShell `Add-Type` 编译到内存，执行调度器清理场景。没有用替代实现模拟调度器。
- 其余问题来自源码控制流核对，尚未进行 Unity PlayMode 或真机复现；不能把静态结论写成真机验证结果。

## 优先修复的问题

### F01 / P1：操作调度器不安全地处理完成回调中的清理

处理状态：**已修复并发布**。EditMode 3 项通过；覆盖回调内清理、监听者异常隔离和清理期间创建新操作。

位置：`PulletFramework/Runtime/PulletAsyncOperation.cs:159`、`:170`。

`Update()` 在调用业务完成回调后继续执行 `Operations.RemoveAt(i)`。如果回调调用框架销毁或 `PulletOperationSystem.Clear()`，原集合已经清空，原索引失效。另一个问题是 `Clear()` 先清空集合，再依次 Abort；其中一个操作的完成回调抛异常，会中断后续 Abort，其余操作已不在调度集合，却仍为 Processing。

本轮实际输出：

```text
Clear in Completed: ArgumentOutOfRangeException
Clear callback exception: InvalidOperationException
Remaining operation after Clear and Update: IsDone=False, Status=Processing
```

建议：完成通知与集合修改隔离；使用代次或身份检查防止重入操作污染集合；清理时逐项保证终态，隔离每个监听者异常。不要仅在最外层吞掉异常。

验收：完成回调清理、清理回调创建新操作、某个回调抛异常、重复 Clear，所有被取消的操作都结束，后续调度继续正常。

### F02 / P1：YooAsset 完成通知早于包占用状态释放

处理状态：**已修复**。包操作先写入终态并按操作身份释放占用，再逐项隔离通知外部监听者；EditMode 已覆盖监听者异常及完成回调观察释放状态。

位置：`PulletFramework.YooAsset/Runtime/PulletYooAssetPackagePipeline.cs:564`、`PulletYooAssets.cs:167`、`PulletYooAssetPackageOperation.cs:164`。

Pipeline 先 `operation.Complete(...)`，再 `finished(false)` 移除 ActiveOperations。同包 Initialize 的 Completed 回调立即开始 Check/Prepare，或 Check 完成后立即开始 Update，会被误判为“已有操作正在执行”。进度或完成监听者抛异常时，finished 还可能完全执行不到，使这个包一直处于占用状态。

建议：先建立终态并释放操作归属，再通知外部；成功、失败、取消、异常共享一次性收尾逻辑；Finish 校验操作身份，不能只按包名移除。各监听者分别捕获异常并用 PLogger 报告。

验收：在 Completed 中串接同包操作成功；监听者抛异常不影响其他监听者、包状态和后续操作。

### F03 / P1：YooAsset Reset 会留下无法结束的等待句柄

处理状态：**已修复**。Reset 会先摘除活动操作和状态，再把上层句柄同步置为取消终态；操作代次及对象身份阻止迟到回调删除新会话。PlayMode 已覆盖活动句柄立即结束且只通知一次；各真实网络阶段和小游戏真机仍留待 S6 验收。

位置：`PulletFramework.YooAsset/Runtime/PulletYooAssets.cs:146`、`PulletYooAssetPackageOperation.cs:120`。

Reset 只调用 Cancel 设置请求标志，随后清空操作表并销毁协程驱动对象。如果初始化、版本请求或下载仍在等待，驱动停止后无法再执行 Pipeline 的取消收尾，操作仍可能 `IsDone == false`，外部 `yield return operation` 永远等待。

建议：停止驱动前确定每个操作的取消终态；明确底层操作与上层等待句柄的取消职责；加入代次隔离，防止重置前的迟到结果影响新会话。

验收：在初始化、版本请求、清单加载、下载、预热各阶段 Reset；句柄都结束且只通知一次；随后重新初始化成功。

### F04 / P1：资源上传平台目录与运行时目录不一致

处理状态：**实现已修复，平台验收待完成**。统一平台目录映射已在 EditMode 覆盖 6 组构建目标与运行时目标；旧线上目录不会自动迁移，实际 Player/CDN 验收仍属于 S3/S6。

位置：`PulletFramework.YooAsset/Editor/PulletYooAssetCosPublisher.cs:114`、`Runtime/PulletYooAssetSettings.cs:198`。

上传使用 `EditorUserBuildSettings.activeBuildTarget.ToString()`，下载使用自定义平台映射：

| 目标 | 上传目录 | 下载目录 |
| --- | --- | --- |
| WebGL | WebGL | WebGL |
| Android | Android | Android |
| iOS | iOS | IPhone |
| Windows 64 位 | StandaloneWindows64 | PC |

默认自动拼接配置下，后两者不会请求刚上传的目录。当前抖音/微信都是 WebGL，因此既有验收没有覆盖这个问题。PC 统一为一个目录还会混淆不同桌面平台的 AB。

建议：建立唯一的资源平台标识映射，让发布端和运行时共用规则；保留当前 WebGL 路径兼容性。自动测试每个支持的平台生成的最终 URL 完全一致。

### F05 / P2：窗口入场过程中关闭，打开操作可能一直等待

处理状态：**已修复并通过自动化回归**。每次打开使用独立身份，关闭、替换、销毁及过期取消均不能结束后续打开操作。

位置：`PulletFramework/Runtime/Window/PulletWindow.cs:803`、`UIWindow.cs:674`、`OpenWindowOperation.cs:45`、`UIFadeWindowTransition.cs:73`。

加载完成、入场动画未完成时，窗口已经不是 IsLoading。关闭走 InternalClose，淡入淡出组件会取消入场回调，再播放退场。原 OpenWindowOperation 只检查 LoadError 与 IsOpenCompleted，关闭未给它设置取消结果，因此它会继续等待；以后同一缓存窗口重新打开，还可能使旧打开操作错误地成功。

建议：给每次打开分配独立操作身份；关闭/替换/销毁时主动结束对应打开操作，不能只依赖共享 UIWindow 的布尔值。

验收：带入场动画时立即关闭、返回或切根窗口；旧句柄取消，新打开句柄不受影响。

### F06 / P2：音频的两类播放入口没有统一请求顺序

处理状态：**已修复并通过自动化回归**。地址加载和直接 Clip 播放共享请求代次，通道暂停、全局暂停与应用暂停分别管理。

位置：`PulletFramework/Runtime/Sound/PulletSound.cs:145`、`:167`、`:234`、`:241`。

先 `PlayMusic("A")` 异步加载，再直接 `PlayMusic(clipB)`，后者没有递增 `_musicRequest`；A 加载完成时仍通过旧请求检查，覆盖较新的 B。Voice 两种重载存在同样问题。

另一个需要统一的语义是暂停：PauseAll/PauseMusic 只暂停已有 AudioSource，不记录通道暂停状态，加载中的请求完成后仍会调用 Play。若暂停代表应用退后台或全局暂停，这会违反调用方预期。

建议：公共播放请求统一排序，真正开始播放用内部方法；明确手动暂停、应用暂停与 Stop 的语义，使异步加载后的播放遵守同一状态。

验收：延迟加载 A 后播放 B，最终仍为 B；加载中暂停不自动出声；恢复、停止、静音持久化分别正确。

### F07 / P2：小游戏初始化失败后重试绕过存储后端安装

处理状态：**已修复并通过自动化回归**。初始化、重试及成功后的平台存储和声音偏好绑定已统一入口，并隔离取消与迟到结果。

位置：`PulletFramework.MiniGame/Runtime/MiniGameBootstrap.cs:59`、`PulletMiniGames.cs:27`。

第一次初始化已经 Install adapter，但初始化失败或调用者取消时，成功分支没有安装平台 PlayerPrefs。之后通过 MiniGameBootstrap 重试，因为 IsInstalled 为 true，只调用 PulletPlatform.InitializeAsync，不再经过 PulletMiniGames 的成功处理。即使重试成功，平台存储后端安装和声音偏好重新读取仍可能被跳过。

建议：初始化与成功后的通用绑定只有一个入口；重试复用该入口。异步取消与迟到成功的归属应明确，不把“adapter 已安装”当成“集成已完成”。

验收：SDK 首次失败、第二次成功；首次调用取消后重试；都能使用平台存储，且重复调用不重复绑定。

## 需要明确的契约与改进项

这些项目不等同于已经复现的启动故障，应按使用契约决定修复方式。

### 版本与回退

`PulletYooAssetPackagePipeline.cs:254` 的较旧版本保护只用于 Web 模式默认包的 Prepare，并且比较的是远端与内置版本，不是远端与当前已使用版本。当前用到 3、内置为 1、CDN 指向 2 时，这个保护并不阻止选用 2。其他包、显式 Update 和 Host 模式也不走相同保护。

需要统一“默认禁止倒退 / 允许显式回滚 / 无法比较如何处理”的策略，再让包更新流程使用。不要把版本不相等称作版本更新，也不要禁止业务主动回滚。仅有一个数字比较工具不代表所有入口都已经受保护。

### Form

`PulletForm.cs:75` 的 IsReadFinish 只表示 readCount 归零，无法统一汇总成功与失败；失败详情目前在各表的 LoadError。`AddForm<T>()` 已登记类型后再次调用会跳过，不构成失败重试。

建议提供轻量的批量加载结果及重试语义；保留业务覆写 Parse 的方式，不为此把 JSON 等所有格式塞进核心框架。

### Pooling 与资源所有权

Spawner 销毁后没有终态保护，旧 Spawner 引用仍可调用 SpawnInternal 创建新池，而它已经不在 PulletPooling 管理列表里。建议销毁后的入口明确拒绝，避免游离资源。

继续保持调用方先释放 UI/音频/对象池再卸载资源包的边界，不让 YooAsset 模块反向掌握游戏业务。补测重复归还、外部 Destroy、加载中销毁池、失败重试和包卸载。池对象的状态复位约定需要文档化；不建议直接再造通用实体系统。

### AssetPublishing

通用发布服务已经存在，但 YooAsset 发布器仍直接调用 TencentCOS，没有经过统一供应商服务。运行时不应依赖云厂商；资源清单与发布顺序属于 YooAsset，上传协议与凭据属于 AssetPublishing。

另外，发布开始保存了一份配置用于生成最终地址，但逐文件 PutObjectAsync 使用无配置参数重载，会重新读取当前配置。应固定一次发布任务的配置快照、包身份和目标目录，避免任务期间修改配置导致资源分散。保留“资源先传、清单随后、版本指针最后”的顺序，并补上并发发布防护和失败重试测试。

当前凭据资产命中了验证工程的 gitignore；本轮没有读取、改动或上传凭据。不要用提交凭据的方式解决不同机器配置问题。

### 静态生命周期与独立安装

Demo 的 GameLaunch 主动执行销毁，但基础框架驱动器自身缺少等价的退出收尾。需要测试关闭 Domain Reload 的反复 Play、部分初始化失败后销毁、重复初始化和销毁；不能只靠 Demo 入口约定兜底。

继续保留独立模块：核心资源抽象不扩充成完整 YooAsset API；小游戏 SDK 能力留在平台适配模块；HybridCLR 本轮不安装、不扩展；着色器收集维持当前范围。

## 两个仓库的一致性

本轮按相对路径比较 `.cs/.asmdef/.json/.uxml/.uss`，统一 CRLF/LF 后，验证工程相对于独立仓库存在以下不同或缺失文件：

| 模块 | 不同或仅验证工程存在 |
| --- | ---: |
| PulletFramework | 10 |
| PulletFramework.YooAsset | 9 |
| PulletFramework.AssetPublishing | 4 |
| PulletFramework.MiniGame | 12 |
| 合计 | 35 |

这不是 35 个 bug，也不是包含所有资源及源仓库独有文件的全量差异计数。但着色器收集、首包 CDN 和通用发布服务等已有实现，不能假定已经全部进入独立仓库。

后续处理结果：公共实现已逐项审核并同步，三组全新 UPM 组合编译通过；发布源已推送至 `main@e670808`，验证工程已推送至 `main@5d94423`。验证工程保留已导入的 SDK Sample 与项目配置，不用整目录覆盖代替模块安装。

建议先审核差异、确定唯一维护源，再让验证工程使用本地 UPM 依赖或可检查的单向同步流程。不要直接整目录覆盖，不要丢失任一处现有修改。

## 加固顺序与验收门槛

1. **异步与生命周期**：先修 F01-F03，建立完成一次、取消可结束、异常不阻断清理、旧回调不能操作新会话的基础约束；随后覆盖 F05-F07。
2. **发布与更新一致性**：修 F04，统一版本选择与发布配置快照；测试多包、远端不可达、内置回退、远端旧版本和中断发布。
3. **可复用与回归**：审查同步两个仓库，增加独立安装验证，再重新走抖音、微信和至少一个普通 Player 的完整流程。

目前在上述 Pullet 模块中未发现正式 NUnit EditMode/PlayMode 测试程序集；示例 SingletonTest 不等价于框架回归测试。手工跑通记录有价值，但应补充以下可重复测试：

- EditMode：操作重入/异常/清理、版本选择、各平台 URL、发布顺序、配置快照、初始化失败重试。
- PlayMode：窗口动画期间关闭、池和资源销毁、音频请求交错、重复启动退出及 Domain Reload 设置组合。
- 开发者工具及真机：冷启动、缓存启动、断网、前后台切换、平台存储、CDN 更新与内置回退。

结论：现有模块划分值得保留，生命周期和失败路径已建立自动化回归。下一轮重点是真机缓存、前后台、声音、存储、安全区和 Player Profiler 验收，不继续增加大而全的 API 或无关功能。
