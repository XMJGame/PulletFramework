# 小游戏平台接入与故障排查

本文记录 `PulletMiniGame` 在 Unity 2022.3.62f3 上接入抖音和微信小游戏时已经验证过的流程与问题。
平台 SDK、开发者工具和后台能力会持续更新，复现问题时应同时记录引擎、SDK、开发者工具、基础库、
AppID 和发生时间。

## 通用排查顺序

1. 确认使用正式小游戏 AppID，并且开发者工具登录的是该 AppID 的管理员或开发者账号。
2. 确认平台后台已经开通构建结果声明的插件、API 和业务能力。
3. 确认导入的是小游戏输出目录，不是原始 WebGL 目录，也没有按普通小程序类型导入。
4. 先看开发者工具控制台，再查开发者工具底层日志。控制台的最终异常经常只是权限或分包错误的后续表现。
5. 检查输出文件是否存在、大小是否非零、配置中的文件名或 MD5 是否一致。
6. 清理开发者工具缓存并重启；仍有问题时使用真机预览区分模拟器缺陷与真实运行问题。
7. 最后才重新构建 Unity。不要用反复构建代替账号、权限和输出文件检查。

日志和截图中不得提交 AppSecret、签名密码、COS SecretId/SecretKey、登录 code 或用户令牌。

## 抖音小游戏

### 账号与 AppID 无权限

**现象**

- 导入项目时提示 AppID 格式错误或无权限。
- 获取域名白名单失败，提示当前账号无权访问应用。
- 已在开放平台配置域名，但开发者工具仍无法取得配置。

**本次根因**

开发者工具使用了与开放平台应用无关的抖音快捷登录账号，而不是注册并拥有该小游戏的开发者账号。
AppID 本身可以正确，登录账号仍可能没有导入、编译或读取白名单的权限。

**处理**

退出开发者工具，使用开放平台中拥有该小游戏权限的账号登录，再重新导入项目。遇到“无权限”时先核对
开发者工具账号和开放平台成员列表，不要首先修改 AppID。

### COS 文件下载失败

**现象**

`Download file failed`，URL 指向 COS 或其他资源域名。

**处理**

- 在开放平台分别配置 Request 合法域名和 Download File 合法域名。
- 后台只填写域名，不填写协议、路径或尾部斜杠。
- 游戏请求使用 HTTPS，且最终重定向到的域名也必须在白名单内。
- 配置后重新拉取项目配置或重启开发者工具。

域名已经填写但工具提示“获取域名白名单失败”时，优先按上一节检查登录账号权限。

### 默认场景或黑屏

**现象**

构建可以启动，但加载的是示例场景、默认场景或黑屏。

**排查**

- 检查 Unity Build Settings 的首场景和 `Mini Game Build` 的参与构建场景。
- 查看平台日志实际加载的场景名，不能只看编辑器当前打开的场景。
- 确认导入的是 TTSDK 最终输出目录，而不是中间 WebGL 目录或上一次构建目录。
- 修改 AppID、域名等纯平台配置时不要无条件重建 Unity；场景或代码变化才需要重新构建。

### TTSDK 与微信 SDK 程序集冲突

**现象**

- `PlayerPrefs` 同时存在于 `ttsdk` 和 `wx-runtime-editor`。
- `ttsdk.dll` 无法解析 `TTLitJson` 或 `TTWebGL`。

**根因与处理**

两个平台 SDK 同时参与 Editor 或目标平台编译，或者 TTSDK DLL 与其依赖的导入平台不一致。
平台桥接必须放在独立 asmdef 中，并由 `PULLET_PLATFORM_DOUYIN`、`PULLET_PLATFORM_WECHAT`
以及 SDK 自身宏隔离。切换平台时同步应用插件 Import Settings，不能只隐藏业务 C# 文件。

### 广告与侧边栏

- 抖音没有适用于所有应用的通用正式广告位测试 ID。没有真实广告位时，只能验证参数校验、失败路径和 UI 状态；
  真正的加载、关闭及完整播放奖励必须在已开通广告能力的应用中真机验收。
- 侧边栏复访必须实际调用 `TT.NavigateToScene`，并在 `TT.OnShow` 中核对
  `scene=021036`、`launch_from=homepage`、`location=sidebar_card`。
- 平台只提供回流信号，不规定奖励无限领取。当前框架建议业务按每日一次去重，联网游戏应由服务器最终校验。

## 微信小游戏

### 项目类型和导入目录

微信小游戏根目录应包含 `game.json`，`project.config.json` 的 `compileType` 应为 `game`。
若开发者工具提示根目录缺少 `app.json`，通常是把项目按普通小程序导入，或选错了输出目录。
应重新导入微信 SDK 生成的 `minigame` 目录并选择小游戏类型，不要手工补一个 `app.json`。

### 正式 AppID 与官方插件

测试 AppID 不能代替正式小游戏 AppID 使用需要授权的官方插件。当前 Unity WebGL 转换结果可能声明：

| 能力 | Provider | 当前验证版本 | 何时需要 |
| --- | --- | --- | --- |
| Unity 启动插件 | [`wxe5a48f1ed5f544b7`](https://mp.weixin.qq.com/wxopen/pluginbasicprofile?action=intro&appid=wxe5a48f1ed5f544b7&lang=zh_CN) | `1.3.13` | Unity/团结 WebGL 转微信小游戏必须 |
| 开放数据域 Layout | [`wx7a727ff7d940bb3f`](https://mp.weixin.qq.com/wxopen/pluginbasicprofile?action=intro&appid=wx7a727ff7d940bb3f&lang=zh_CN) | `1.0.16` | 启用 `UseFriendRelation`、好友榜或开放数据域时必须 |
| MiniGameChat | [`wx2ea687f4258401a9`](https://mp.weixin.qq.com/wxopen/pluginbasicprofile?action=intro&appid=wx2ea687f4258401a9&lang=zh_CN) | 由官方模板决定 | 仅启用社交聊天组件时需要，不是排行榜依赖 |

后台入口一般为“设置 -> 第三方设置 -> 插件管理”，也可以从构建工具报错提供的插件详情页添加。
插件必须添加到当前构建所用的正式小游戏 AppID 下。

可直接打开以下官方插件页：

- [Unity 启动插件](https://mp.weixin.qq.com/wxopen/pluginbasicprofile?action=intro&appid=wxe5a48f1ed5f544b7&lang=zh_CN)
- [开放数据域 Layout](https://mp.weixin.qq.com/wxopen/pluginbasicprofile?action=intro&appid=wx7a727ff7d940bb3f&lang=zh_CN)
- [MiniGameChat 社交组件](https://mp.weixin.qq.com/wxopen/pluginbasicprofile?action=intro&appid=wx2ea687f4258401a9&lang=zh_CN)

### `read plugin file empty` 的已确认案例

**现象**

```text
innerInstantiate: WXWebAssembly.instantiate failed
wasmFilePath=wasmcode/<hash>.webgl.wasm.code.unityweb.wasm.br
Error: read plugin file empty
```

磁盘上的 Brotli Wasm 文件存在、大小非零、名称和 MD5 均正确，UnityPlugin 也能初始化，因此错误看起来像
开发者工具或 Wasm 分包读取问题。

**本次最终根因**

项目启用了微信好友排行榜，导出结果包含 `openDataContext`、`UseFriendRelation` 和 Layout provider，
但正式 AppID 只添加了 Unity 插件，没有添加开放数据域 Layout 插件。添加
`wx7a727ff7d940bb3f` 后，游戏恢复正常。

这个错误信息具有误导性：缺少的是 Layout 授权，最终异常却出现在 UnityPlugin 读取 Wasm 的阶段。
因此只要 `game.json` 声明多个插件，就必须逐个核对授权，不能看到 UnityPlugin 已加载便认为插件权限完整。

**修复步骤**

1. 登录微信公众平台并确认当前正式小游戏 AppID。
2. 添加 `game.json` 中声明的 UnityPlugin；启用开放数据域时同时添加 Layout。
3. 重启微信开发者工具，执行“清缓存 -> 全部清除”，重新编译。
4. 仍失败时检查底层 `WeappLog` 中的 `batchgetplugininfo`、`80082` 和
   `real get online plugin <provider> <version>`。

### 日志判读

- `80082, no permission to plugin[...]`：服务器明确拒绝当前 AppID 使用列出的插件，是决定性权限证据。
- `real get online plugin ...`：开发者工具已经从线上取得指定插件，但仍应核对 `game.json` 声明的其他插件。
- `getAccountInfoSync ... customVersion`、`jsbridge not ready`、`reportKeyValue`、`gameTransfer`、
  `navigator.mediaDevices not supported`：在模拟器中通常是非致命能力或时序提示，应以游戏是否继续初始化为准。
- 插件内部打印的版本号可能与开发者工具实际下载版本不同；底层 `WeappLog` 和 `game.json` 更适合确认版本。

### 排行榜依赖

`PulletMiniGame` 启用“微信好友排行榜”后会设置 `UseFriendRelation`，保留 Layout 开放数据域模板，
并在 `game.json` 中声明 Layout 插件。关闭好友排行榜后不应保留该依赖。
全服榜和可信奖励仍应由业务服务器实现；Layout 只负责微信关系链数据在开放数据域内的绘制。

## 发布前验收清单

- 正式 AppID、开发者工具登录账号和后台项目一致。
- `game.json` 中声明的每个 provider 都已在当前 AppID 下添加。
- Request、Download、Upload、Socket 域名按实际使用分别配置。
- 首场景、平台宏、运行环境和输出目录正确。
- 登录、分享、生命周期、网络失败、前后台切换均已真机验证。
- 激励视频只在完整播放后发奖，并使用真实广告位验收。
- 抖音侧边栏回流和微信开放数据域分别在真实入口验收。
- 开发者工具模拟器与真机结果不一致时保存两份日志，并记录工具、基础库和 SDK 版本。
