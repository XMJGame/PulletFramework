# YooAsset 与小游戏 CDN

## 两类 CDN

小游戏工程里存在两类用途不同的 CDN，不应共用一个配置字段：

1. 平台首包 CDN：承载 Unity WebGL 的 wasm、data 等启动文件，由微信/抖音转换 SDK 管理。
2. YooAsset 业务资源 CDN：承载 AssetBundle、版本文件和资源清单，由 `PulletYooAssets` 管理。

两者可以使用同一个域名，但应分目录，例如：

```text
https://cdn.example.com/minigame/bootstrap/wechat/...
https://cdn.example.com/game-assets/WebGL/v1.0/DefaultPackage/...
```

## Unity Data 首包 CDN

`Pullets/Workspace -> 小游戏发布` 中的“Unity Data 首包”控制 Unity WebGL 的
`webgl.data` 如何下发：

- `平台分包（Package）`：转换 SDK 把压缩后的 data 放进 `data-package`，由小游戏平台下载和预解压；
- `远端 Data CDN`：data 从代码包剥离，启动 Loader 在 Unity 运行前从对象存储下载。

Data CDN 不会替代 YooAsset。Unity 启动后，`GameLaunch` 仍会独立执行 YooAsset 的版本检查、清单更新和
AssetBundle 下载。当前自动目录为：

```text
{对象存储远端根目录}/bootstrap/{platform}/{Player版本}/{data哈希文件}
```

例如抖音 `1.0.0` 会生成：

```text
https://cdn.example.com/project/bootstrap/douyin/1.0.0/<md5>.webgl.data.unityweb.bin.br
```

选择 CDN 后应先重新构建小游戏，再点击“上传首包 Data CDN 文件”。上传器会读取导出包的 `game.js`，
确认 `loadDataPackageFromSubpackage=false`，再按 `DATA_FILE_MD5` 定位同级 `webgl` 目录中的压缩文件；
配置、哈希或远端路径不一致时会拒绝上传。上传使用长期不可变缓存，并复用“资源发布”页选择的对象存储供应商。

抖音和微信转换 SDK 都支持 Package/CDN 两种首包方式。平台限制和启动策略可能变化，不自动根据本地目录体积
切换模式：优先裁剪首场景、Resources 和内置资源，平台上传仍超限时才使用 CDN。

## YooAsset 内置资源

`Pullets/Workspace -> 小游戏发布` 中的“默认包随小游戏发布”与“Unity Data 首包”是两个独立选项：

- Unity Data 首包决定 `webgl.data` 使用平台分包还是 Data CDN；
- 默认包随小游戏发布决定 YooAsset 的 `DefaultPackage` 是否完整复制到 `StreamingAssets`。

开启 YooAsset 内置资源后，构建默认 Package 会同时生成两份用途不同的产物：完整版本目录继续用于 CDN 发布，
另一份复制到 `Assets/StreamingAssets/<YooFolderName>/DefaultPackage` 并随 Player/小游戏发布。运行时注册两个文件系统，
先匹配内置文件，只有内置目录没有的文件或远端清单新增的资源才从 CDN 获取。非默认 Package 保持远端按需加载，
避免后续场景包无意中增大首包。

默认包的启动准备阶段支持离线降级：远端版本、清单或启动下载失败时，流水线会销毁尚未交给业务使用的远端
Package，改用只读内置文件系统加载随包版本。操作仍以成功结束，并通过 `UsedBuiltinFallback` 与
`FallbackReason` 告知业务当前使用旧内置版本；日志也会保留远端失败原因。手动检查、更新和运行中的下载不会
触发该降级，避免销毁已经被 UI、音频或对象池引用的资源。没有内置资源时，CDN 失败仍按正常错误处理并允许重试。

版本采用纯数字分段比较，支持 `1.0.0`、`v1.2` 和 `2026-09-14-174738`，其中
`1.10.0 > 1.2.0`。CDN 版本旧于内置版本或格式无法比较时，启动选择内置版本并输出警告，
防止客户端被旧版本指针降级。同一资源兼容通道必须保持同一种版本体系；从日期版本切换为语义版本时应创建
新的 `resourceChannel`。

关闭开关并重新构建默认 Package 时，会清理旧的内置目录，避免过期 AB 继续混入 Player。小游戏导出前还会检查
`BuiltinCatalog.bytes`；开关已启用但资源尚未构建时，发布按钮会给出明确错误。

验证内置资源：

1. 开启“默认包随小游戏发布”，递增 YooAsset Package 版本并执行“构建当前版本”；
2. 确认小游戏发布页显示内置目录与体积，再重新导出抖音或微信包；
3. 清除开发者工具缓存后首次启动，确认 `StreamingAssets` 分包成功且游戏进入首页；
4. 在 Network 中允许版本文件和清单访问 CDN，但内置 Bundle 的哈希 URL 不应产生 CDN 下载；
5. 临时把版本地址改成不可访问地址，确认日志显示“已降级使用随包内置版本”且仍可进入首页；
6. 发布只增加一个远端资源的新版本，确认旧内置文件仍直接使用、新文件从 CDN 下载。

抖音官方将 `StreamingAssets` 定义为首包资源的一部分，并要求自定义 AB 构建目录时将需要随包的 Bundle 移入该
目录。内置资源会增加小游戏包体，因此只适合登录、首页和基础公共资源，不应把大型关卡或低频内容全部内置。

官方参考：

- [抖音 Data CDN 功能](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/packaging-release/webgl-data-cdn-tutorial)
- [抖音小游戏资源部署与缓存](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/packaging-release/changelog-and-resource/sc_webgl_resource)
- [抖音 Unity WebGL 构建说明](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/game-engine/rd-to-SCgame/unity-game-access/packaging-release/sc_build)
- [微信 Unity 转换与首包配置](https://github.com/wechat-miniprogram/minigame-unity-webgl-transform/blob/main/Design/Transform.md)
- [微信 Unity Loader 资源下载](https://github.com/wechat-miniprogram/minigame-unity-webgl-transform/blob/main/Design/UsingLoader.md)

## 唯一入口

项目只保留一个 `GameLaunch`。入口负责排列启动阶段，不直接承担下载细节：

```csharp
private IEnumerator Start()
{
    PulletFrameworks.Initialize();
    var resourceOperation = PulletYooAssets.PrepareDefaultPackageAsync();
    yield return resourceOperation;

    if (!resourceOperation.Succeeded)
    {
        ShowRetry(resourceOperation.Error);
        yield break;
    }

    yield return EnterGame();
}
```

标准流程是：初始化文件系统、请求版本、加载清单、计算下载列表、下载资源、安装
`PulletResources` 适配器、进入游戏。编辑器模拟模式也必须加载清单，不能把“文件系统初始化成功”
误认为资源已经可用。

## CDN 目录

项目中两份设置职责不同：

- `YooAssetSettings.asset` 是 YooAsset 官方全局设置，必须位于 `Resources`，负责缓存目录名和发布文件前缀；
- `PulletYooAssetSettings.asset` 是 PulletFramework 运行配置，位于 `Assets/Settings/Pullets/YooAsset/Resources`，负责包名、
  运行模式、CDN 地址、版本和下载策略，由 `GameLaunch` 显式引用。

两者不冲突，也不应合并。修改官方 `PackageFilePrefix` 会改变版本文件与清单文件名；已经发布过资源后修改它，
必须递增 `appVersion` 并重新构建、上传一个完整新版本。修改 `YooFolderName` 会改变本地缓存目录，线上项目中
可能导致已有缓存失效并重新下载。

`PulletYooAssetSettings.defaultHostServer` 应指向 YooAsset 构建输出中直接包含版本文件和清单文件的目录。
推荐模板：

```text
https://cdn.example.com/game-assets/{platform}/{appVersion}/{package}
```

构建 `DefaultPackage` 后，把同一版本目录完整上传，不要只上传 AssetBundle。更新时先上传带哈希的资源文件，
再上传清单，最后更新版本文件，避免客户端取得新版本号后读不到对应清单。

在 `Pullets/Workspace -> YooAsset 资源` 执行“构建当前版本”后，会在版本目录同级的
`PublishReports` 中生成
`DefaultPackage_<版本>_publish.json`。该文件包含待上传文件的大小、SHA-256 与顺序：

1. `Payload`：哈希 AssetBundle 或原生文件；
2. `Manifest`：版本对应的 `.bytes` 和 `.hash`；
3. `VersionPointer`：包版本文件（例如 `DefaultPackage.version` 或
   `oneklab_DefaultPackage.version`），必须最后上传。

报告会自动排除 `buildlogtep.json`、`.report` 和 `link.xml`。发布报告本身也不需要上传到 CDN。
已经存在的 Package 版本不会被构建命令覆盖；资源有变化时应递增 YooAsset Package 版本，
但不必修改 Player/App 版本。只需要重新生成上传清单时，在 Workspace 中执行“生成发布报告”，
无需重新构建资源包。

## 腾讯云 COS 发布

在 `Pullets/Workspace` 的“资源发布”页选择“腾讯云 COS”，直接填写以下配置并保存：

- `AccessKey ID / SecretId` 与 `AccessKey Secret / SecretKey`：编辑器上传凭据；
- `Bucket` 与 `Region`：对象存储桶位置；
- `远端根目录`：项目在桶内的发布根路径；
- `公开下载域名`：YooAsset 客户端使用的 CDN 或 COS 下载域名；
- `自定义 Endpoint`：预留给代理或兼容对象存储服务，腾讯云 COS 默认可留空。

配置资产保存在
`Assets/Settings/Pullets/Publishing/PulletAssetPublishingSettings.asset`，不进入 `Resources`，
运行时不会加载，上传流程也不再读取环境变量。旧版 `PulletEditorSetting` 和旧上传窗口已移除，
发布参数只有这一处来源。

“YooAsset 资源”页负责生成发布报告并发起上传；“资源发布”页只负责供应商、凭据和存储位置。
上传成功后会把运行时 CDN 模板回填到 `PulletYooAssetSettings.defaultHostServer`。
不要把长期 SecretId/SecretKey 提交到公共仓库；正式项目应使用最小权限子账号，只授予目标资源目录的上传、
覆盖与查询权限。命令行执行 `PublishCurrentVersionBatch` 时不要附加 `-quit`，任务会在结束时主动退出。

## 缓存策略

- COS 发布器会为哈希 Bundle 和带版本号的清单写入
  `Cache-Control: public, max-age=31536000, immutable`。这些对象的 URL 随内容或 Package 版本变化，
  可以长期缓存。
- `{PackageFilePrefix}_{PackageName}.version` 是可变版本指针，发布器写入
  `Cache-Control: no-cache, max-age=0, must-revalidate`，避免 CDN 或宿主继续使用旧版本号。
- 2026-09-11 已对验证桶中的 5 个 Bundle、清单、哈希和版本指针完成公网校验：HTTP 状态、长度、SHA-256、
  CORS 与缓存响应头均符合发布报告。该结果只证明远端对象正确，不代表平台沙盒缓存已经通过真机验收。
- 微信：使用 YooAsset Mini Game 样例的 `WechatFileSystem`，缓存根位于 `WX.env.USER_DATA_PATH`，
  AssetBundle 加载与卸载走 `WXAssetBundle`。
- 抖音：使用 `TiktokPlatform` 与 `TTAssetBundle`，由平台适配层注入 YooAsset Web 文件系统。
- 版本不变时，二次启动只校验清单和缓存，不重复下载已缓存 AssetBundle。
- 发布新资源后只下载差异文件；空间不足或版本切换后可清理未被当前清单引用的缓存。
- `maximumConcurrency` 默认 4。小游戏不宜盲目提高并发，否则会放大首帧卡顿与弱网失败率。

### 抖音各层职责

`TiktokFileSystem` 与 `TTAssetBundle` 不是两套互斥方案。YooAsset 官方 Mini Game 样例的调用关系是：

```text
PulletYooAssets
  -> YooAsset WebNetworkFileSystem
    -> PulletWebNetworkFileSystem (YooAsset.Extension)
      -> TiktokPlatform (IPulletWebPlatformStrategy)
        -> TTSDK.TTAssetBundle
```

- YooAsset：管理版本、清单、依赖、下载队列、资源地址和生命周期。
- `YooAsset.Extension`：使用 YooAsset 官方友元程序集名称，桥接其内部 Web 策略与公开的平台接口。
- `TiktokFileSystemCreater`：创建 YooAsset Web 文件系统参数并注入抖音平台策略。
- `TiktokPlatform`：把 AssetBundle 请求、提取和卸载转交给 TTSDK。
- `TTAssetBundle`：TTSDK 对抖音 WebGL AssetBundle/ABFS 的封装。ABFS 可用时注册 URL 并从平台文件系统加载；
  不可用时回退到下载字节后从内存加载；卸载时通过 `TTUnload` 同步注销 URL。

因此业务代码仍只使用 YooAsset/PulletResources，不能直接到处调用 `TTAssetBundle`。平台 SDK 调用只留在
抖音适配程序集内部。

平台缓存容量和回收策略可能随基础库变化，最终应在真机测试首次下载、二次冷启动、断网启动、下载中断恢复、
版本升级、空间不足与清理缓存。

2026-09-14 已在抖音开发者工具 4.5.6 完成远端运行验证：清除全部缓存后，TT ABFS 启用，YooAsset
初始化成功并加载首页；同版本再次启动同样成功。开发者工具显示的 `ttl: 5s, capacity: 128MB` 是当前模拟器
实现信息，不应写成正式设备的固定平台上限。

## 验证工程

`MiniGameFeatureDemo` 是轻量 3D 竖版射击场景。首场景只有启动入口、相机和诊断面板；
`PlayerShip`、`EnemyShip` 通过 `DefaultPackage` 加载，用来同时验证：

- 编辑器模拟资源清单与寻址；
- 平台触摸输入和安全区；
- YooAsset 首次下载与二次缓存；
- 敌机与子弹对象复用；
- 平台登录、分享、广告、侧边栏和排行榜诊断入口。

工程内的 `MiniGameFeatureDemoSetup` 负责生成场景、低模飞机预制体、资源配置和 YooAsset 收集规则；
日常资源构建与小游戏发布统一通过 `Pullet Workspace` 完成。
