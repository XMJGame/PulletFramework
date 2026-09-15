# PulletMiniGame

`PulletMiniGame` 是依赖 `PulletFramework` 的可选小游戏模块。基础框架不引用本模块，
普通 PC、移动端或主机项目可以完全不安装它。

## 模块边界

- `Runtime`：平台能力接口、服务入口和独立生命周期驱动。
- `Editor`：小游戏项目脚手架、平台注册表、宏、配置和统一发布窗口。
- `Runtime/Platform/WeChat`：不引用官方 SDK 的微信平台适配边界。
- `Runtime/Platform/Douyin`：不引用官方 SDK 的抖音平台适配边界。
- `Editor/Platform/WeChat`：微信参数面板、校验和后续 SDK 导出实现。
- `Editor/Platform/Douyin`：抖音参数面板、校验和后续 SDK 导出实现。

微信和抖音适配程序集只声明桥接边界，不直接绑定某个版本的官方 SDK。本包已经提供
`WeChat SDK Bridge` 和 `Douyin SDK Bridge` 可选样例，分别绑定各自官方 SDK。

## UPM 安装

先安装基础框架与 Editor-only 资源发布依赖，再安装小游戏模块：

```text
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.AssetPublishing
https://github.com/XMJGame/PulletFramework.git?path=/PulletMiniGame
```

`PulletMiniGame` 不强制依赖 YooAsset。需要业务资源热更新与 CDN 时，再单独安装：

```text
https://github.com/tuyoogame/YooAsset.git?path=/Assets/YooAsset#3.0.5
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.YooAsset
```

`AssetPublishing` 只在 Unity Editor 中参与首包 CDN 上传和地址拼接，不会增加 Player 运行时代码。使用 YooAsset 时，建议采用根 README 中的完整顺序：核心、AssetPublishing、YooAsset、MiniGame。

安装微信官方 SDK 后，在 Package Manager 的 Samples 中导入 `WeChat SDK Bridge`。
模块没有把平台 SDK 写进自身依赖，因此普通游戏和只接单个平台的项目不会被额外 SDK 污染。

## 启动

```csharp
using PulletFramework;
using PulletMiniGame;
using PulletMiniGame.Platform;

PulletFrameworks.Initialize();
PlatformResult result = await MiniGameBootstrap.InitializeAsync(cancellationToken);
if (!result.Succeeded)
    Debug.LogError(result.Error);
```

业务只查询需要的能力：

```csharp
if (PulletPlatform.TryGet(out IRewardedAdService ads))
{
    PlatformResult loaded = await ads.LoadAsync("revive", cancellationToken);
    if (loaded.Succeeded)
    {
        RewardedAdResult adResult = await ads.ShowAsync("revive", cancellationToken);
        if (adResult.ShouldGrantReward)
            RevivePlayer();
    }
}
```

退出时先调用 `PulletMiniGames.Shutdown()`，再销毁基础框架。

框架代码统一通过 `PulletPlayerPrefs` 读写轻量设置，默认后端是 Unity `PlayerPrefs`。
平台初始化成功后，模块会自动切换后端：微信使用 `WXBase.Storage`，抖音使用
`TT.PlayerPrefs`。音频、画质、语言和游戏业务不需要编写平台判断；无论声音系统在平台
初始化之前还是之后启动，音频偏好都会重新从当前平台存储读取。

SDK 样例还会在 WebGL 小游戏包中注册长音乐后端：抖音使用 `TTAudioManager`，微信使用
`WX.CreateInnerAudioContext`。只有 HTTPS 音频地址会被接管，YooAsset 内的音乐与短音效仍
使用 Unity `AudioSource`。因此业务可以统一调用：

```csharp
PulletSound.PlayMusic("https://cdn.example.com/audio/home.mp3");
```

编辑器和普通 App 会自动回退到 Unity 的远端音频加载，不需要编写平台宏。平台后端当前用于
背景音乐，不替换需要低延迟并发播放的短音效。

## 异步调用约定

业务侧的一次性平台请求统一返回 `Task`，方法使用 `Async` 后缀，并接收可选的
`CancellationToken`。平台拒绝、登录失败、广告未准备好等可预期结果通过
`PlatformResult` 返回；调用方主动取消则遵循 .NET 约定抛出 `OperationCanceledException`。

建议让请求绑定界面或游戏流程的生命周期：

```csharp
try
{
    PlatformResult<PlatformLoginCredential> login =
        await player.LoginAsync(cancellationToken);
    if (login.Succeeded)
        ConnectGameServer(login.Value.Code);
    else
        ShowLoginError(login.Error);
}
catch (OperationCanceledException)
{
    // 界面关闭或流程退出，无需提示平台错误。
}
```

不要使用 `async void` 编写普通业务方法；它只适用于 Unity 生命周期和按钮事件入口。
微信、抖音 SDK 适配层内部仍使用原生回调，再转换为 Task。共享的初始化和广告加载请求中，
取消某个调用者只结束该调用者的等待，不会中断其他调用者仍在等待的平台请求。

所有初始化入口最终都由 `PulletMiniGames` 管理同一适配器会话。初始化失败后可以重试；切换
适配器或调用 `Shutdown` 会使旧会话失效，即使旧 SDK 回调随后成功，也不能覆盖当前平台的
`PulletPlayerPrefs` 后端。关闭时模块只卸载自己安装的后端，不会移除业务后来替换的后端。

`MiniGameBootstrap` 在编辑器默认选择模拟服务；正式包选择当前宏启用的 SDK 样例注册工厂。
样例通过 `BeforeSceneLoad` 注册，业务应从首场景 `Awake` 或之后调用初始化。
没有注册或同时注册多个正式平台会明确报错。关闭 Domain Reload 后仍会清理和重新注册。

需要自定义适配器时也可以显式注入：

```csharp
var platformAdapter = new WeChatPlatformAdapter(new WeChatSdkBridge());
PlatformResult result = await PulletMiniGames.InitializeAsync(
    platformAdapter, cancellationToken);
```

`IPlayerService.Login` 返回 `PlatformLoginCredential`，其中 `Code` 是临时登录凭证。
业务服务器应使用它换取账号会话；`PlatformUser` 仅用于服务器确认后的用户资料模型。

微信桥接会等待 `WX.InitSDK` 回调后再注册生命周期和读取启动参数。
通过 `PulletPlatform.TryGet` 查询服务前必须等待初始化成功。
自动启动会加载当前平台运行配置，将 `revive` 等业务广告位转换为广告单元 ID；先调用 `Load`，再调用 `Show`。
直接实例化未包装的 SDK bridge 时，参数仍是平台广告单元 ID。
只有视频正常完整结束才返回 `ShouldGrantReward == true`。分享成功回调仅表示请求已发起，
不能用于确认用户已分享或发放分享奖励。

## 编辑器工具

平台账号、插件权限、合法域名、构建输出和常见错误请参阅
[《小游戏平台接入与故障排查》](Documentation/PlatformTroubleshooting.zh-CN.md)。

- `Pullets/Workspace -> 小游戏发布 -> 初始化标准目录`：创建标准游戏、平台和配置目录。
- 同一页面维护微信/抖音配置、应用平台设置并执行 SDK 导出，不再保留旧的独立构建菜单。
- Package Manager 的 `Platform Diagnostics` Sample：导入可复用真机验收面板，测试登录、分享、
  激励视频、生命周期和抖音侧边栏。建议只放在独立开发场景，不进入正式发布场景。

发布窗口维护 WebGL 平台宏：

```text
PULLET_MINIGAME
PULLET_PLATFORM_WECHAT / PULLET_PLATFORM_DOUYIN
PULLET_ENV_DEVELOPMENT / PULLET_ENV_TEST / PULLET_ENV_RELEASE
```

团结引擎的 `MINIGAME_SUBPLATFORM_WEIXIN` 和 `MINIGAME_SUBPLATFORM_DOUYIN` 也会启用对应
运行时程序集。模块只管理自己的 `PULLET_` 宏，不会删除项目已有宏。

平台 SDK 或转换插件通过 `IPlatformBuildAdapter` 注册导出能力。未安装 SDK 时仍可配置、
编写和模拟业务，但发布按钮保持禁用。

“首包资源”控制 Unity WebGL Data 文件，不是 YooAsset Bundle：

- `Package`：Data 随微信/抖音平台分包发布，通常用于体积未超过平台限制的项目。
- `CDN`：转换后的 Data 文件先上传对象存储，导出包只保留下载地址；必须先配置“资源发布”，重新导出后再上传首包文件。
- “随 Player 发布”则属于 YooAsset 配置，决定是否把默认 Package 的 Bundle 放入 `StreamingAssets`。它与上述 Data 首包模式可以独立组合。

微信验证工程使用官方 UPM 包，并固定到已验证提交：

```text
https://github.com/wechat-miniprogram/minigame-tuanjie-transform-sdk.git#d288776c50578926c732496882bd6ab6684c778c
```

微信适配器会写入官方 `MiniGameConfig`，再调用 `WXConvertCore.DoExport(true)`。抖音 SDK
通过 BGDT 安装；检测到 `ByteGame/StarkSDKTools/Build Tool` 后，统一窗口会启用
“打开抖音构建工具”。新项目应使用 TTSDK 6.6.3 以上版本和 Unity 新包体格式。

新增平台时实现 `IMiniGamePlatformDefinition` 即可加入发布窗口。平台定义通过 Unity
`TypeCache` 自动发现，按照 `Order` 排序，不需要修改窗口代码。平台 ID 会自动转换为
`PULLET_PLATFORM_<ID>` 宏，例如 `kuaishou` 对应 `PULLET_PLATFORM_KUAISHOU`。

新增平台的运行时 SDK 绑定在自己的程序集内调用
`MiniGameBootstrap.Register(platformId, factory)`。该程序集由平台宏约束，核心无需增加平台判断。

## 当前验证与后续

YooAsset 业务资源 CDN、平台缓存桥接与验证场景说明见
[Documentation/YooAssetCdn.zh-CN.md](Documentation/YooAssetCdn.zh-CN.md)。

验证工程已安装微信 SDK、BGDT 3.0.271 和 TTSDK 6.9.3。该版本 TTSDK 已包含构建工具。
BGDT 的安装入口与用法见[官方文档](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/BGDT-handbook/apply)。

已验证 Unity 2022.3.62f3 编辑器编译、初始化并发/取消/重试/迟到回调，以及关闭 Domain Reload
后的连续两次 Play Mode 启停。`Platform Diagnostics` Sample 可在平台真机包中逐项验收；
真实登录、广告、分享和侧边栏返回仍需使用对应开发者账号与真实配置测试。

TTSDK 可选样例与自动注册、平台运行参数配置、侧边栏复访及真机验收面板已经接入。
下一步完成微信/抖音真机验收与微信开放数据域榜单样例。当前公共模块已发布到仓库 `main`，可通过本页 Git URL 安装；
验证工程暂时仍使用 Assets 中的模块副本，便于同步调试和平台回归。

## 运行配置

在发布窗口的“运行参数”中编辑默认分享标题、图片 URL、query 和激励广告位映射。
配置路径为 `Assets/Settings/Platforms/WeChat/Resources/PulletMiniGame/wechat.asset`
以及 `Assets/Settings/Platforms/Douyin/Resources/PulletMiniGame/douyin.asset`。
小型配置通过 Resources 启动加载，无需等待 YooAsset 初始化；业务资源仍走 YooAsset。

初始提供 `revive`、`bonus_reward` 两个空广告位，必须填写各平台真实 ID 才能在真机请求广告。
编辑器模拟允许已声明的广告位 ID 为空；不存在的业务广告位始终失败。
默认分享参数可通过 `await IShareService.ShareAsync(default, cancellationToken)` 使用，
也可传入请求逐项覆盖。
这些配置仅保存公开运行参数，不能放 AppSecret 或服务端密钥。

## 抖音侧边栏复访

抖音桥接实现 `ISidebarRevisitService`，通过 `TT.CheckScene` 检查宿主支持，并通过
`TT.NavigateToScene` 跳转侧边栏。只有生命周期参数同时满足 `launch_from=homepage`
和 `location=sidebar_card` 才视为有效回流；不会只根据 `scene=021036` 发放奖励。

`SidebarRevisitTask` 提供按周期去重的本地辅助逻辑。每日周期可以使用
`SidebarRevisitTask.ChinaDailyPeriodKey(DateTimeOffset.UtcNow)`，领取前调用 `CanClaim`，
奖励成功入账后再调用 `TryMarkClaimed`。正式联网项目应由服务器校验和记录奖励状态，
本地助手适合离线游戏及界面即时状态，不能承担防作弊。

平台不限制奖励频次，当前建议按每日一次设计。活动 ID 可在抖音运行参数中选填；
未配置时按平台兜底侧边栏样式跳转。上传审核要求工程实际调用 `TT.NavigateToScene`，
只判断启动场景而没有跳转入口不算完成接入。

## 排行榜

排行榜拆成三个独立能力，业务应按当前平台实际提供的服务组合使用：

| 平台 | 上报成绩 | 游戏内查询数据 | 平台/受限域展示 | 时间范围 | 关系范围 |
| --- | --- | --- | --- | --- | --- |
| [抖音](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/open-ability/ImRankIntroduce) | `TT.SetImRankData` | `TT.GetImRankData` | `TT.GetImRankList` | 日、周、月、总榜 | 好友、全站 |
| 微信 | `WX.SetUserCloudStorage` | 主域不允许读取好友数据 | 开放数据域读取并绘制 | 由开放数据域或业务规则决定 | 好友、群 |
| [快手](https://ks-game-docs.kuaishou.com/minigame/api/api.html) | 未发现公开原生榜单接口 | 未发现 | 未发现 | 自建后端决定 | 自建后端决定 |
| 编辑器 | 模拟 | 模拟 | 模拟 | 全部 | 好友、全站 |

抖音桥接完整实现 `ILeaderboardScoreService`、`ILeaderboardQueryService` 和
`ILeaderboardViewService`。`BoardId` 会映射到抖音 `zoneId`；数值榜和段位榜分别使用
`LeaderboardScore` 的两个构造函数。调用榜单 API 前需要完成平台登录。
抖音文档将 `getImRankData` 标记为开放数据域能力；TTSDK 6.9.3 已提供对应 C# 包装，仍需在
抖音开发者工具和真机分别验收自绘榜。平台托管的 `GetImRankList` 可作为稳定兜底。

微信桥接目前只暴露 `ILeaderboardScoreService`，因为主域代码不能直接读取好友云数据。
微信好友榜/群榜需要单独制作开放数据域脚本和 `sharedCanvas` 展示层，不能冒充普通数据查询接口。
全服榜、跨平台榜、赛季榜和可信奖励结算都应走业务服务器。
自建后端中的 `Friends` 只能表示游戏自己的好友、公会或关注关系，无法读取微信、抖音的私有关系链。

导入 `WeChat SDK Bridge` 样例后，可以把 `WeChatOpenDataLeaderboardView` 挂到排行榜
`RawImage` 上。发布窗口启用“微信好友排行榜”后，适配器会打开官方 SDK 的
`UseFriendRelation`，保留其 Layout 开放数据域模板，并移除模板自带的随机测试成绩。
组件会按照 `RawImage` 的实际屏幕区域调用 `WXBase.ShowOpenData`：

```csharp
leaderboardView.ShowFriends("user_rank");
// 群排行只能在通过群分享卡片进入并取得 shareTicket 后调用。
leaderboardView.ShowGroup(shareTicket, "user_rank");
```

上报使用的 `LeaderboardScore.BoardId`、组件传入的 key，以及微信公众平台配置的排行榜 key
必须完全一致。开放数据域只负责社交关系榜展示，不会把好友数据返回给主域 C# 或业务服务器。

`LeaderboardGateway` 用来在平台榜与自建榜之间路由：

```csharp
var backend = new HttpLeaderboardBackend(new HttpLeaderboardBackendOptions
{
    BaseUrl = "https://game-api.example.com",
    AccessTokenProvider = () => session.AccessToken
});
var leaderboard = new LeaderboardGateway(backend);

await leaderboard.SubmitScoreAsync(
    new LeaderboardScore("endless_score", score), cancellationToken);
PlatformResult<LeaderboardPage> page = await leaderboard.GetRanksAsync(
    new LeaderboardQuery("endless_score", ELeaderboardPeriod.Week),
    cancellationToken: cancellationToken);
```

`Auto` 只有在平台同时支持“上报 + 查询”时才使用平台数据榜，否则整套读写走后端，
避免写入平台后又从服务器读到另一份数据。要使用微信开放数据域榜或抖音托管榜，可以显式传入
`ELeaderboardProvider.Platform` 上报，并通过对应平台展示能力打开。

`HttpLeaderboardBackend` 复用 `PulletHttp`，默认使用以下 JSON 接口：

- `POST /v1/leaderboards/score`：请求包含 `boardId`、`valueType`、`value`、`priority`、`extra`；
  返回 `{ "success": true }`。
- `POST /v1/leaderboards/query`：请求包含榜单、周期、范围和值类型及分页；返回
  `{ "success": true, "data": { "entries": [], "self": null, "pageNumber": 1, "totalCount": 0 } }`。
- 配置了 `AccessTokenProvider` 时自动发送 `Authorization: Bearer <token>`。

服务器必须从会话识别玩家，并校验成绩是否合法；客户端提交的分数、昵称、排名和奖励资格都不能
直接信任。跨平台账号合并、排行榜重置、反作弊和奖励发放也属于服务器职责。
