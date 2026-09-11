# YooAsset 与小游戏 CDN

## 两类 CDN

小游戏工程里存在两类用途不同的 CDN，不应共用一个配置字段：

1. 平台首包 CDN：承载 Unity WebGL 的 wasm、data 等启动文件，由微信/抖音转换 SDK 管理。
2. YooAsset 业务资源 CDN：承载 AssetBundle、版本文件和资源清单，由 `PulletYooAssetRuntime` 管理。

两者可以使用同一个域名，但应分目录，例如：

```text
https://cdn.example.com/minigame/bootstrap/wechat/...
https://cdn.example.com/game-assets/WebGL/v1.0/DefaultPackage/...
```

## 唯一入口

项目只保留一个 `GameLaunch`。入口负责排列启动阶段，不直接承担下载细节：

```csharp
private IEnumerator Start()
{
    PulletFrameworks.Initialize();
    yield return PulletYooAssetRuntime.Initialize();

    if (PulletYooAssetRuntime.Status != EPulletYooAssetStartupStatus.Succeeded)
    {
        ShowRetry(PulletYooAssetRuntime.Error);
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

验证工程执行 `Build Shooter YooAsset Package` 后，会在版本目录同级的 `PublishReports` 中生成
`DefaultPackage_<版本>_publish.json`。该文件包含待上传文件的大小、SHA-256 与顺序：

1. `Payload`：哈希 AssetBundle 或原生文件；
2. `Manifest`：版本对应的 `.bytes` 和 `.hash`；
3. `VersionPointer`：包版本文件（例如 `DefaultPackage.version` 或
   `oneklab_DefaultPackage.version`），必须最后上传。

报告会自动排除 `buildlogtep.json`、`.report` 和 `link.xml`。发布报告本身也不需要上传到 CDN。
已经存在的版本不会被构建命令覆盖；资源有变化时应先递增 `appVersion`。只需要重新生成上传清单时，执行
`Pullets/Mini Game/Validation/Generate Shooter Publish Report`，无需重新构建资源包。

## 腾讯云 COS 发布

在 `Pullets/Workspace` 的“资源发布”页选择“腾讯云 COS”，直接填写以下配置并保存：

- `AccessKey ID / SecretId` 与 `AccessKey Secret / SecretKey`：编辑器上传凭据；
- `Bucket` 与 `Region`：对象存储桶位置；
- `远端根目录`：项目在桶内的发布根路径；
- `公开下载域名`：YooAsset 客户端使用的 CDN 或 COS 下载域名；
- `自定义 Endpoint`：预留给代理或兼容对象存储服务，腾讯云 COS 默认可留空。

配置资产保存在
`Assets/Settings/Pullets/Publishing/PulletAssetPublishingSettings.asset`，不进入 `Resources`，
运行时不会加载。上传流程不再读取环境变量。迁移期间旧版 `PulletEditorSetting` 只在未安装
`PulletAssetPublishing` 模块时回退使用，完成验证后会随旧窗口一起删除。

“YooAsset 资源”页负责生成发布报告并发起上传；“资源发布”页只负责供应商、凭据和存储位置。
上传成功后会把运行时 CDN 模板回填到 `PulletYooAssetSettings.defaultHostServer`。
不要把长期 SecretId/SecretKey 提交到公共仓库；正式项目应使用最小权限子账号，只授予目标资源目录的上传、
覆盖与查询权限。命令行执行 `PublishCurrentVersionBatch` 时不要附加 `-quit`，任务会在结束时主动退出。

## 缓存策略

- 微信：使用 YooAsset Mini Game 样例的 `WechatFileSystem`，缓存根位于 `WX.env.USER_DATA_PATH`，
  AssetBundle 加载与卸载走 `WXAssetBundle`。
- 抖音：使用 `TiktokPlatform` 与 `TTAssetBundle`，由平台适配层注入 YooAsset Web 文件系统。
- 版本不变时，二次启动只校验清单和缓存，不重复下载已缓存 AssetBundle。
- 发布新资源后只下载差异文件；空间不足或版本切换后可清理未被当前清单引用的缓存。
- `maximumConcurrency` 默认 4。小游戏不宜盲目提高并发，否则会放大首帧卡顿与弱网失败率。

### 抖音各层职责

`TiktokFileSystem` 与 `TTAssetBundle` 不是两套互斥方案。YooAsset 官方 Mini Game 样例的调用关系是：

```text
PulletYooAssetRuntime
  -> YooAsset WebNetworkFileSystem
    -> TiktokPlatform (IWebPlatformStrategy)
      -> TTSDK.TTAssetBundle
```

- YooAsset：管理版本、清单、依赖、下载队列、资源地址和生命周期。
- `TiktokFileSystemCreater`：创建 YooAsset Web 文件系统参数并注入抖音平台策略。
- `TiktokPlatform`：把 AssetBundle 请求、提取和卸载转交给 TTSDK。
- `TTAssetBundle`：TTSDK 对抖音 WebGL AssetBundle/ABFS 的封装。ABFS 可用时注册 URL 并从平台文件系统加载；
  不可用时回退到下载字节后从内存加载；卸载时通过 `TTUnload` 同步注销 URL。

因此业务代码仍只使用 YooAsset/PulletResources，不能直接到处调用 `TTAssetBundle`。平台 SDK 调用只留在
抖音适配程序集内部。

平台缓存容量和回收策略可能随基础库变化，最终应在真机测试首次下载、二次冷启动、断网启动、下载中断恢复、
版本升级、空间不足与清理缓存。

## 验证工程

`MiniGameFeatureDemo` 是轻量 3D 竖版射击场景。首场景只有启动入口、相机和诊断面板；
`PlayerShip`、`EnemyShip` 通过 `DefaultPackage` 加载，用来同时验证：

- 编辑器模拟资源清单与寻址；
- 平台触摸输入和安全区；
- YooAsset 首次下载与二次缓存；
- 敌机与子弹对象复用；
- 平台登录、分享、广告、侧边栏和排行榜诊断入口。

编辑器菜单 `Pullets/Mini Game/Validation/Create Feature Demo` 可重新生成场景、低模飞机预制体、资源配置和
YooAsset 收集规则。它不会自动构建 PC 或 WebGL。
