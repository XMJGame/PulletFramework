# PulletFramework.YooAsset

`PulletFramework.YooAsset` 是基础框架的可选资源适配模块。它负责 YooAsset 初始化、
运行时资源适配、资源构建设置、发布报告和 COS 上传工具；不包含微信或抖音平台能力。

## 安装顺序

```text
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework
https://github.com/tuyoogame/YooAsset.git?path=/Assets/YooAsset#3.0.5
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.YooAsset
```

安装后，`Pullets/Workspace` 会自动出现“YooAsset 资源”页面。移除本模块后，基础框架仍可使用，
但项目必须安装其他 `IResourceAdapter`，或不调用依赖外部资源的窗口、对象池和音频功能。

配置资产建议保存在：

```text
Assets/Settings/Pullets/YooAsset/Resources/PulletYooAssetSettings.asset
```

运行时内部由 `PulletYooAssetSettingsData` 统一加载该资产。该类型不向业务程序集开放，业务只通过
`PulletYooAssets` 获取默认包名称、检查配置并执行资源包操作，不需要序列化或主动获取配置对象。
所有资源包操作统一通过 `PulletYooAssets` 完成，不保留旧的双轨启动接口。

业务 AssetBundle CDN 与微信、抖音导出工具使用的 Unity 首包 CDN 是两套配置。

## 多 Package 运行流程

小游戏通常只需要 `DefaultPackage`。内容较大的项目可以在 YooAsset 资源收集器顶部点击 `+`
增加 `UIPackage`、`AudioPackage`、`LevelPackage` 等独立包，再从 Workspace 的下拉框选择当前
构建和发布的包。每个平台、每个 Package 的最后构建版本会独立记录，不会相互覆盖。

默认包启动：

```csharp
if (!PulletYooAssets.IsConfigured)
    throw new InvalidOperationException("未配置 PulletYooAssetSettings。");

var operation = PulletYooAssets.PrepareDefaultPackageAsync();
yield return operation;
if (!operation.Succeeded)
    Debug.LogError(operation.Error);
```

进入关卡前按需更新并下载另一个包：

```csharp
var operation = PulletYooAssets.PreparePackageAsync("LevelPackage", true);
operation.ProgressChanged += current =>
    Debug.Log($"{current.CurrentDownloadBytes}/{current.TotalDownloadBytes}");
yield return operation;

if (operation.Succeeded)
{
    ResourcePackage package = PulletYooAssets.GetPackage("LevelPackage");
    AssetHandle handle = package.LoadAssetAsync<GameObject>("Level_01");
    yield return handle;
    // 使用完毕后调用 handle.Release();
}
```

也可以拆开控制流程：`InitializePackageAsync` 只初始化文件系统，`CheckPackageAsync` 只检查
远端版本，`UpdatePackageAsync` 加载指定或最新清单，`DownloadPackageAsync` 下载全包或指定
标签，`UnloadPackageAsync` 销毁并移除包。下载操作提供进度、文件数、字节数以及
`DownloadFileStarted`、`DownloadError`、`PauseDownload()`、`ResumeDownload()` 和 `Cancel()`。
同一个 Package 同一时间只允许一个操作，不同 Package 可以并行处理。

`UnloadUnusedAssetsAsync` 用于卸载引用计数为零的资源，`ClearUnusedCacheAsync` 用于删除当前
清单不再使用的缓存 Bundle，`TryUnloadUnusedAsset` 可针对单个地址尝试卸载。业务加载获得的
YooAsset Handle 必须遵循官方生命周期，在不再使用时调用 `Release()`；异步实例化产生的
GameObject 由业务或对象池负责销毁、回收。

切换清单或卸载 Package 前，业务必须释放该包创建的资源 Handle 和实例。框架对象池使用该包时，
应先调用 `PulletPooling.DestroySpawner(packageName)`；相关窗口和音频也应先关闭、停止并卸载。
默认包通常贯穿整个应用生命周期；场景包和活动包适合在退出对应玩法后释放并卸载。

建议按“更新与生命周期是否独立”决定是否拆包，而不是按文件夹机械拆分。小游戏优先维持一个
`DefaultPackage`；较大的项目可让基础 UI 和首屏资源留在默认包，把大型关卡、活动或可选语音
拆成按需 Package。流程顺序参考 YooAsset 官方 Space Shooter：初始化、请求版本、加载清单、
创建下载器、下载、清理旧缓存。

参考：

- [Space Shooter - Initialize Package](https://github.com/tuyoogame/YooAsset/blob/yoo3/Assets/YooAsset/Samples~/Space%20Shooter/GameScript/Runtime/PatchLogic/FsmNode/FsmInitializePackage.cs)
- [Space Shooter - Request Package Version](https://github.com/tuyoogame/YooAsset/blob/yoo3/Assets/YooAsset/Samples~/Space%20Shooter/GameScript/Runtime/PatchLogic/FsmNode/FsmRequestPackageVersion.cs)
- [Space Shooter - Update Package Manifest](https://github.com/tuyoogame/YooAsset/blob/yoo3/Assets/YooAsset/Samples~/Space%20Shooter/GameScript/Runtime/PatchLogic/FsmNode/FsmUpdatePackageManifest.cs)
- [Space Shooter - Create Downloader](https://github.com/tuyoogame/YooAsset/blob/yoo3/Assets/YooAsset/Samples~/Space%20Shooter/GameScript/Runtime/PatchLogic/FsmNode/FsmCreateDownloader.cs)

## 构建与发布

日常发布统一从 `Pullets/Workspace -> YooAsset 资源` 操作：

1. “资源收集器”维护资源归属和地址；
2. “资源构建器”维护 YooAsset 官方高级构建参数；
3. “构建当前版本”读取当前平台、Pullet 包名与资源版本，并沿用资源构建器保存的压缩、首包拷贝、加密等参数；
4. “上传当前版本到腾讯云 COS”按照资源、清单、版本指针的顺序发布。

COS 上传时，哈希 Bundle 与带版本号的清单使用一年 immutable 缓存；可变的 `.version` 指针使用
`no-cache, max-age=0, must-revalidate`。因此资源文件可以长期复用，同时客户端仍会检查最新 Package 版本。

`resourceChannel` 是客户端兼容通道（例如 `v1`），普通资源更新时保持不变。
`packageVersion` 是 YooAsset 资源清单版本，资源发生变化后应递增它，再构建和上传。
这不会改变 Player/App 版本，也不要求重新提交小游戏审核。已存在的资源包版本不会被覆盖，
以免 CDN 和平台缓存把新旧清单及 Bundle 混用。

版本生成支持“手动版本”和“自动日期”两种方式。开发验证阶段可使用自动日期
（例如 `2026-09-11-163500`）；正式运营建议手动维护 `1.0.0`、`1.0.1` 这样的语义版本。
配置会记录最后一次成功构建的实际版本，生成报告和上传操作都以该版本为准。

默认 CDN 结构为：

```text
game-assets/{platform}/{appVersion}/{resourceChannel}/{package}
```

`appVersion` 直接取 Player/App 版本，不在 YooAsset 配置中重复保存。例如：
`game-assets/WebGL/1.0.0/v1/DefaultPackage`。

## 运行时结构

```text
启动与资源更新
  -> PulletYooAssets                 Package 门面、状态、并发约束
  -> PulletYooAssetPackagePipeline  初始化、版本、清单、下载、清理流水线
  -> PulletYooAssetPackageInitializer 文件系统和运行模式创建
  -> YooAsset ResourcePackage

业务资源加载
  -> PulletYooAssets.GetPackage()
  -> YooAsset ResourcePackage 原生 API

PulletFramework 内部 UI、表格、音频和对象池
  -> PulletResources 最小单资源接口
  -> YooAssetResourceAdapter
```

基础框架旧版 `StreamingAssetsHelper` 已删除，不移动到本模块。它在 Android 上通过同步等待
`UnityWebRequest` 查询 APK 内文件，存在阻塞主线程的风险；旧 YooAsset 访问器读取时又使用
`File.ReadAllBytes`，无法可靠读取 APK 内部路径。YooAsset 3.0 的默认 `BuiltinFileSystem` 已在
没有第三方同步访问器时使用异步读取路径，并会用默认缓存目录和 `DefaultBundleUnpackPolicy`
处理 Android 上加密 Bundle、RawBundle 与 ArchiveBundle 的按需解压，因此本模块直接使用
官方文件系统。将来若确实需要
同步读取 StreamingAssets，应单独接入 BetterStreamingAssets 一类实现，并通过
`IBuiltinFileAccessor` 注入，不能恢复忙等实现。

## 能力边界

`PulletYooAssets` 覆盖常用的多 Package 生命周期：初始化、版本检查、清单更新、整包或标签下载、
暂停/恢复/取消、缓存清理、未使用资源卸载和包销毁，并通过 `GetPackage()` 返回已初始化的
YooAsset `ResourcePackage`。单资源、子资源、Bundle 内全部资源、RawFile、场景、组合下载器和
高级缓存操作均直接使用 YooAsset 官方 API，不再二次封装。

`PulletResources` 只为 PulletFramework 自身的 UI、表格、音频和对象池提供最小单资源加载接口。
业务不需要学习它，也不应把它当作 YooAsset 的替代 API。未来项目若改用 Unity AssetBundle 或
其他资源系统，只需为框架内部提供同等的最小适配器；业务继续使用所选资源系统自己的 API。

## 资源加载示例

```csharp
ResourcePackage package = PulletYooAssets.GetPackage("LevelPackage");

AssetHandle prefab = package.LoadAssetAsync<GameObject>("PlayerShip");
SubAssetsHandle atlas = package.LoadSubAssetsAsync<Sprite>("Atlas");
AssetHandle config = package.LoadAssetAsync<RawFileObject>("GameConfig");
SceneHandle scene = package.LoadSceneAsync("Level_01", LoadSceneMode.Additive);

yield return prefab;
if (prefab.Status == EOperationStatus.Succeeded)
    prefab.InstantiateSync();

// 每个 Handle 按 YooAsset 官方规则管理和释放。
prefab.Release();
atlas.Release();
config.Release();
yield return scene.UnloadSceneAsync();
```

YooAsset 3.0 运行时参考：

- [初始化](https://www.yooasset.com/docs/guide-runtime/ResourceInit)
- [文件系统](https://www.yooasset.com/docs/guide-runtime/FileSystem)
- [资源更新](https://www.yooasset.com/docs/guide-runtime/ResourceUpdate)
- [资源清理](https://www.yooasset.com/docs/guide-runtime/ResourceClear)
- [资源加载](https://www.yooasset.com/docs/guide-runtime/ResourceLoad)
- [资源卸载](https://www.yooasset.com/docs/guide-runtime/ResourceUnload)
- [内置文件解压](https://www.yooasset.com/docs/solution/BuiltinFileUnpack)
