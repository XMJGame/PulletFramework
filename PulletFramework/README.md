# PulletFramework.Window
一个轻量级的基于堆栈的界面系统。

## 模块安装

仓库中的功能按职责拆成独立 UPM 包：

| 模块 | 必需 | 用途 |
| --- | --- | --- |
| `PulletFramework` | 是 | UI、网络、对象池、事件及资源抽象 |
| `PulletFramework.YooAsset` | 否 | YooAsset 3.x 初始化、资源构建与 CDN 加载 |
| `PulletAssetPublishing` | 否 | 对象存储供应商、凭据、CORS 与资源发布 |
| `PulletMiniGame` | 否 | 微信、抖音等小游戏平台能力和发布配置 |
| `PulletFramework.HybridCLR` | 否 | HybridCLR 构建与程序集复制工具，已验证 8.14.1 |

```text
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.YooAsset
https://github.com/XMJGame/PulletFramework.git?path=/PulletAssetPublishing
https://github.com/XMJGame/PulletFramework.git?path=/PulletMiniGame
```

需要代码热更新时，先安装官方 HybridCLR，再安装可选工作流模块：

```text
https://github.com/focus-creative-games/hybridclr_unity.git#v8.14.1
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.HybridCLR
```

普通 App 或单机项目只安装基础框架即可。使用 YooAsset 时先安装 YooAsset 3.0.5，再安装适配包；
发布微信或抖音小游戏时再安装 `PulletMiniGame`。模块页面会自动汇入同一个 `Pullets/Workspace`，
不会各自增加一组顶级菜单。

## 编辑器工作台

通过 `Pullets/Workspace` 打开统一工作台。页面由已安装模块自动注册：

- `Player 构建`：通用 Player 版本、目标平台、本机签名参数和 Unity 构建入口。
- `YooAsset 资源`：资源运行模式、CDN、资源版本、收集器和构建器。
- `资源发布`：选择对象存储供应商，配置凭据、下载域名和发布目录。
- `小游戏发布`：安装 `PulletMiniGame` 后出现，管理微信、抖音、平台宏和导出参数。
- `HybridCLR`：安装并启用 HybridCLR 后出现，管理 AOT 与热更新程序集流程。

旧编辑器设置、旧 YooAsset 构建窗口及兼容入口均已移除，所有配置统一从 Workspace 对应模块进入。

扩展模块只需让自己的 Editor 程序集引用 `PulletFramework.Editor.Workspace.Contracts`，并实现
`IPulletWorkspaceModule`。工作台使用 `TypeCache` 自动发现页面，框架主体不需要反向引用业务或平台程序集。

`PulletFramework.YooAsset.Editor` 已作为独立包和程序集提供资源页面；没有安装相应模块时，不会注册该页面。
通用 Player 构建统一由 `PulletPlayerBuildService` 执行。默认输出到
`Builds/Player/{BuildTarget}`，构建前会检查平台支持、活动平台和启用场景，不会自动触发平台切换或构建。

CI 或升级检查可以执行以下方法验证模块 ID、构造和初始化：

```text
-executeMethod PulletFramework.Editor.PulletWorkspaceWindow.ValidateModules
```

```csharp
public sealed class MyWorkspaceModule : IPulletWorkspaceModule
{
    public string Id => "my-module";
    public string DisplayName => "我的模块";
    public string Description => "模块说明";
    public int Order => 500;

    public void OnEnable() { }
    public void OnDisable() { }
    public void OnGUI() { }
}
```

## Window 导航

窗口层级同时表达导航职责：`RootLayer` 是 Tab 根页面，`NormalLayer` 是详情栈，`NavigationLayer` 是常驻导航，`PopupLayer` 是弹窗。

```csharp
// 底部 Tab 常驻窗口
PulletWindow.ShowPersistentAsync<MainTabWindow>();

// Tab 根页面切换不会写入返回栈
PulletWindow.SwitchTabAsync<HomeWindow>("home");
PulletWindow.SwitchTabAsync<RoleWindow>("role");
PulletWindow.SwitchTabAsync<BagWindow>("bag");

// 详情页和弹窗
PulletWindow.PushAsync<ItemDetailWindow>(itemId);
PulletWindow.ShowPopupAsync<SettingsWindow>();
PulletWindow.Pop();
PulletWindow.PopToRoot();
```

底部导航窗口建议声明为：

```csharp
[UIWindow(EWindowLayer.NavigationLayer, false)]
public sealed class MainTabWindow : UIWindow
{
    public override string assetPath => "UI/MainTabWindow";
}
```

Tab 页面本身使用 `RootLayer`。即使没有声明，`SwitchTabAsync` 也会将目标窗口放入该层：

```csharp
[UIWindow(EWindowLayer.RootLayer)]
public sealed class HomeWindow : UIWindow
{
    public override string assetPath => "UI/HomeWindow";
}
```

需要隐藏底部导航的全屏详情页可以声明为：

```csharp
[UIWindow(EWindowLayer.NormalLayer, true, true)]
public sealed class BattleWindow : UIWindow
{
    public override string assetPath => "UI/BattleWindow";
}
```

## 安全区

背景保持全屏，在每个窗口的交互根节点上挂载 `UISafeAreaFitter`。目标节点应在父节点中全屏拉伸，组件可以分别选择 Left、Right、Top、Bottom：

- 顶部栏：Left、Right、Top
- 底部 Tab：Left、Right、Bottom
- 全屏交互区：All
- 背景和全屏遮罩：不挂载

平台 SDK 返回自定义安全区时，通过 `IUISafeAreaProvider` 注入：

```csharp
UISafeArea.SetProvider(new PlatformSafeAreaProvider());
```

Provider 返回的 Rect 必须使用物理屏幕像素和左下角原点。抖音或微信若返回左上角坐标，需要先转换后再交给框架。

## 缓存和过渡

窗口默认关闭后缓存。一次性页面可以声明为关闭即销毁，常驻窗口可声明为永久缓存：

```csharp
[UIWindow(
    EWindowLayer.PopupLayer,
    fullScreen: false,
    cachePolicy: EWindowCachePolicy.DestroyOnClose)]
public sealed class RewardPopup : UIWindow
{
    public override string assetPath => "UI/RewardPopup";
}

// 释放所有已关闭的普通缓存；Permanent 默认保留。
PulletWindow.TrimCache();
```

在窗口预制体根节点挂载 `UIFadeWindowTransition` 即可启用默认淡入淡出。过渡期间所有可见窗口会统一关闭交互，`OpenWindowOperation` 会在进场动画完成后才成功。

自定义动画组件实现 `IUIWindowTransition` 即可，不需要修改窗口管理器。

需要使用 Animator 时，在窗口预制体根节点挂载 `UIAnimatorWindowTransition` 和 `Animator`：

- Animator Controller 创建 `Open` 和 `Close` 两个非循环状态。
- 组件中的 Enter State、Exit State 填写完整状态路径，默认是 `Base Layer.Open` 和 `Base Layer.Close`。
- 动画使用非零 `Time.timeScale` 时可关闭 Ignore Time Scale；暂停界面建议保持开启，并将 Animator 的 Update Mode 设置为 Unscaled Time。
- `Close` 播放完成后，框架才调用 `OnClose`、移出窗口栈并按缓存策略释放资源。

淡入淡出和 Animator 组件只挂一个；同一窗口存在多个 `IUIWindowTransition` 时框架使用找到的第一个。

## 导航拦截

窗口可以拦截返回或 Tab 切换：

```csharp
protected override bool CanClose(EWindowCloseReason reason)
{
    if (!hasUnsavedChanges)
        return true;

    ShowSaveConfirm();
    return false;
}
```

需要知道关闭是否成功时使用 `TryCloseWindow`、`Pop` 或 `TryPopToRoot` 的返回值。

## 强类型参数

新窗口继承 `UIWindow<TArguments>`：

```csharp
public readonly struct ItemDetailArgs
{
    public readonly int ItemId;

    public ItemDetailArgs(int itemId)
    {
        ItemId = itemId;
    }
}

public sealed class ItemDetailWindow : UIWindow<ItemDetailArgs>
{
    public override string assetPath => "UI/ItemDetailWindow";

    protected override void OnOpen()
    {
        ShowItem(Arguments.ItemId);
    }
}

PulletWindow.PushAsync<ItemDetailWindow, ItemDetailArgs>(new ItemDetailArgs(1001));
```

取消由当前调用发起的打开操作会停止加载并清理窗口；等待一个已经打开或由其他调用正在加载的窗口时，取消只结束本次等待，不会误关共享窗口。

## 窗口结果

调用方可以在窗口完成退出动画后接收结果：

```csharp
PulletWindow.OpenWindowForResultAsync<PickerWindow, int>(result =>
{
    if (result.HasValue)
        SelectItem(result.Value);
});

// PickerWindow 内部
CloseWithResult(selectedItemId);
```

用户直接返回时，回调仍会执行，但 `HasValue` 为 false。

## 弹窗队列

需要逐个显示的奖励、公告、升级提示使用队列接口：

```csharp
long requestId = PulletWindow.EnqueuePopup<RewardPopup>(reward);
PulletWindow.CancelQueuedPopup(requestId); // 只能取消尚未显示的请求
```

`ShowPopupAsync` 仍用于必须立即出现的独立弹窗。队列会等待当前 `PopupLayer` 窗口关闭后再继续。

## Loading 和 Toast

项目实现一次 `IUIFeedbackPresenter` 并注册，框架不限制具体预制体样式：

```csharp
PulletUIFeedback.SetPresenter(feedbackPresenter);

using (PulletUIFeedback.ShowLoading("正在连接"))
{
    await ConnectAsync();
}

PulletUIFeedback.ShowToast("保存成功", EUIToastType.Success);
```

多个系统同时请求 Loading 时会进行引用计数，只有最后一个令牌释放后才隐藏。业务异常时可以调用 `ClearLoading()` 兜底。

## HTTP 和 HTTPS

HTTP 与 HTTPS 使用同一个 `PulletHttp` 入口。默认传输层是 `UnityWebRequest`，默认最多同时执行 4 个请求：

```csharp
PulletHttp.Get("https://api.example.com/profile", response =>
{
    if (response.IsSuccess)
        Debug.Log(response.Text);
    else
        Debug.LogError($"{response.StatusCode}: {response.Error}");
});

HttpRequestHandle handle = PulletHttp.PostJson(
    "https://api.example.com/save",
    json,
    OnSaved);

// 页面关闭时可以取消仍在排队或执行中的请求。
handle.Cancel();
```

自定义请求支持 Header、超时、优先级和重试：

```csharp
var request = HttpRequest.Get("https://api.example.com/config");
request.TimeoutSeconds = 10;
request.MaxRetries = 2;
request.Priority = 100;
request.Headers["Authorization"] = token;
PulletHttp.Send(request, OnConfigLoaded);
```

GET 默认重试两次；POST 默认不重试，避免支付、领奖、保存等接口被重复提交。请求体默认复制，确认调用期间不会修改原数组时可设置 `CopyBody = false` 降低大包内存复制。可通过 `SetMaxConcurrentRequests` 调整并发数，通过 `SetTransport` 注入微信或抖音 HTTP SDK 实现。

## WebSocket

```csharp
var options = new PulletWebSocketOptions
{
    Url = "wss://game.example.com/socket",
    AutoReconnect = true,
    ReconnectDelaySeconds = 1f,
    MaxReconnectDelaySeconds = 15f,
    HeartbeatIntervalSeconds = 15f,
    HeartbeatTimeoutSeconds = 45f,
    HeartbeatPayload = System.Text.Encoding.UTF8.GetBytes("ping"),
    HeartbeatIsText = true,
};
options.Headers["Authorization"] = token;

PulletWebSocketClient socket = PulletNetwork.CreateWebSocketClient(options);
socket.Connected += OnConnected;
socket.MessageReceived += message =>
{
    if (message.IsText)
        Debug.Log(message.Text);
};
socket.Disconnected += OnDisconnected;
socket.Error += Debug.LogError;
socket.Connect();

socket.SendText("hello");
socket.SendBinary(bytes);

// 不再使用时释放，自动重连也会停止。
PulletNetwork.DestroyWebSocketClient(socket);
```

默认 `DotNetWebSocketTransport` 适用于 Mono 和 IL2CPP 原生平台。WebGL、微信及抖音小游戏应实现 `IWebSocketTransport`，通过 `SetWebSocketTransportFactory` 注入平台 SDK；连接状态、发送队列、心跳、指数退避、重连抖动、消息大小限制和主线程事件分发仍由框架统一处理。

# PulletFramework.Setting

业务与框架模块统一使用 `PulletPlayerPrefs` 保存轻量设置。其 API 与 Unity `PlayerPrefs`
保持接近，默认后端就是 Unity；可选宿主模块可以通过 `InstallBackend` 切换存储实现。
`PulletMiniGame` 会在平台初始化成功后自动安装微信或抖音后端，业务代码无需平台判断。

```csharp
PulletPlayerPrefs.SetInt("guide.completed", 1);
PulletPlayerPrefs.SetFloat("camera.sensitivity", 0.8f);
PulletPlayerPrefs.SetString("language", "zh-CN");
PulletPlayerPrefs.Save();
```

# PulletFramework.Logging

框架及可选 Pullet 模块统一通过 `PLogger` 输出日志。可在 `Pullets/Workspace -> 框架设置`
选择运行时日志等级：`Off`、`Error`、`Warning`、`Info` 或 `Debug`。等级采用包含关系，例如
`Warning` 会输出 Warning 和 Error；窗口生命周期、网络逐包等高频诊断只在 `Debug` 下输出。
编辑器的构建、上传及配置检查日志不受该设置影响，避免隐藏必要的工具反馈。

配置保存在 `Assets/Settings/Pullets/Resources/PulletSettings.asset`，也可以在运行时临时覆盖：

```csharp
PLogger.Level = EPulletLogLevel.Warning;
```

# PulletFramework.Machine
一个轻量级的状态机。

# PulletFramework.Event
一个轻量级的事件系统。

# PulletFramework.Form

`FormSingleton` 负责 TextAsset 加载、加载状态、资源句柄释放和销毁批次隔离。默认的
`ReadFormTool` 继续解析旧版 TSV/TXT；JSON、二进制或代码生成表属于业务格式，由具体表重写
`Parse(TextAsset)`：

```csharp
[Serializable]
public sealed class ItemRows
{
    public ItemRow[] items;
}

public sealed class ItemForm : FormSingleton<ItemForm, ItemRow>
{
    public override string formPath => "ItemForm.json";

    protected override Dictionary<int, ItemRow> Parse(TextAsset asset)
    {
        ItemRows rows = JsonUtility.FromJson<ItemRows>(asset.text);
        var result = new Dictionary<int, ItemRow>();
        foreach (ItemRow row in rows.items)
            result.Add(row.id, row);
        return result;
    }
}
```

框架不固定 JSON 根节点、主键字段或 JSON 库，业务可以选择 `JsonUtility`、Newtonsoft JSON、
protobuf 或配置表代码生成工具。

# PulletFramework.Pooling
一个功能强大的游戏对象池系统。

# PulletFramework.Sound

`PulletSound` 默认使用 Unity `AudioSource` 播放音乐、语音和短音效。小游戏模块可以通过
`PulletSound.SetMusicBackend(IPulletMusicBackend)` 只替换长音乐后端，业务侧的暂停、恢复、
静音和音量 API 保持不变。

传入 YooAsset 地址或 `AudioClip` 时继续走 Unity；传入带 `.mp3`、`.wav` 等扩展名的 HTTPS
地址时，已注册的平台后端优先接管。没有平台后端的编辑器、PC 和 App 会使用
`UnityWebRequest` 下载后播放，并在 `PulletSound.UnloadAll()` 时释放音频。

```csharp
PulletSound.PlayMusic("https://cdn.example.com/audio/home.mp3");
PulletSound.PlaySound("ButtonClick");
```

推荐把循环 BGM 和较长旁白作为独立 CDN 音频，把高频、低延迟的短音效放入资源包。

对象池、窗口、表格和音频统一依赖 `PulletFramework.Resource` 抽象，不直接依赖 YooAsset。
项目启动时安装一个资源适配器即可；切换资源系统时业务模块不需要改动。

## 资源适配

核心程序集 `PulletFramework.Runtime` 不引用 YooAsset。仓库提供的 `PulletFramework.YooAsset`
是可选适配程序集，使用 YooAsset 3.x API。推荐由项目唯一入口等待标准启动流程：

```csharp
using PulletFramework.YooAssetAdapter;

var resourceOperation = PulletYooAssets.PrepareDefaultPackageAsync();
yield return resourceOperation;
if (!resourceOperation.Succeeded)
    throw new System.Exception(resourceOperation.Error);

PulletFrameworks.Initialize();
```

`PulletYooAssetSettings` 建议保存在
`Assets/Settings/Pullets/YooAsset/Resources/PulletYooAssetSettings.asset`，由模块的数据入口统一加载。
启动流程统一处理文件系统初始化、
请求版本、加载清单、下载资源和安装 `PulletResources` 适配器；小游戏平台可通过
`PulletYooAssets.WebFileSystemFactory` 注入自己的持久缓存与 AssetBundle 加载策略。

这里的 `defaultHostServer` 是业务 AssetBundle CDN，与微信/抖音构建设置中的 Unity WebGL
首包 CDN 是两套独立配置。URL 支持 `{platform}`、`{appVersion}`、`{package}` 占位符。

只需要自定义 Host 模式时，也可复用框架提供的远端地址服务：

```csharp
var remoteServices = new YooAssetRemoteService(primaryUrl, fallbackUrl);
var options = new HostPlayModeOptions
{
    BuiltinFileSystemParameters =
        FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
    CacheFileSystemParameters =
        FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteServices)
};
```

`PulletResources` 只服务框架自身的 UI、表格、音频和对象池。接入其他资源系统时，实现
`IResourceAdapter`、`IResourcePackage` 及最小单资源句柄，然后调用：

```csharp
PulletResources.Install(new CustomResourceAdapter());
```

卸载或切换适配器前，应按“停止生成对象、关闭相关窗口、停止并卸载音频、
`PulletPooling.DestroySpawner(packageName)`、卸载资源包”的顺序收口。对象池会先取消在途实例化，
销毁缓存和活动实例，最后释放资源句柄。
游戏业务不需要使用 `PulletResources`：选择 YooAsset 时直接使用其 `ResourcePackage`、`AssetHandle`
和 `SceneHandle`；选择 Unity AssetBundle 或其他方案时直接使用对应官方 API。框架不重复封装完整的
资源系统能力。

```c#
using UnityEngine;
using PulletFramework.Pooling;

IEnumerator Start()
{
    // 初始化游戏对象池系统
    PulletPooling.Initalize();

    // 创建孵化器
    var spawner = PulletPooling.CreateSpawner("DefaultPackage");

    // 创建Cube预制体的对象池
    var operation = spawner.CreateGameObjectPoolAsync("Cube.prefab");
    yield return operation;

    // 孵化Cube游戏对象
    SpawnHandle handle = spawner.SpawnAsync("Cube.prefab");
    yield return handle;
    Debug.Log(handle.GameObj.name);

    // 回收游戏对象
    handle.Restore();

    // 丢弃游戏对象
    handle.Discard();
}
```
