# 安装、回退与原生Player协作清单

> 当前已升级统一3.0.0包，准确输入与安装步骤见[当前清单](NetClient-3.0.0-Installation.zh-CN.md)。本页下列pre.3/U11内容保留历史身份，不替代本次安装验收。设备等未验范围继续保持。

2026-10-19更新。开发源是PulletNet仓库UnityPackages，当前为3.0.0-pre.3一个完整netclient包，原生输入升级见[候选输入记录](Native-Candidate-Input-Upgrade.zh-CN.md)。布局沿用[单包整合](Single-Package-Consolidation.zh-CN.md)，不是正式发布；旧U11及pre.2安装回执保持原身份，不能替代当前文件核对。

## 当前交接范围与按需使用

主包从本仓库UnityPackages/PulletFramework.NetClient取得，不以Framework/业务副本作为开发源。内部五个程序集保持独立，所有功能只需安装这一包；功能按需构造，不默认启动。raw/Messaging原生输入为准确29d923e原始Windows CI产物，Discovery仍自己的d05c59c独立锁。禁止重新构建同版本DLL后沿用旧哈希或混用其它平台的候选产物。

| 应用需求 | 包内模块 | 依赖与约束 |
| --- | --- | --- |
| 原始连接/字节收发 | Runtime | raw程序集不引用消息/发现，但完整包仍携带它们和Json.NET依赖 |
| 独立发现 | Modules/Discovery | 独立程序集；Json.NET 3.2.1 |
| Manager发现后连接 | Modules/NetClient.Discovery | 组合不拥有Manager，分别关闭并等待 |
| 无网络消息会话 | Modules/Messaging | 显式IMessageCodec，不默认提供JSON/Protobuf实现 |
| 既有Runtime上的typed连接 | Modules/NetClient.Messaging | 单次来源；显式关闭自动重连，不支持typed透明恢复/保留重绑 |
| 发现后typed连接 | Discovery与typed模块组合 | 应用先选端点再创建typed来源；无需Manager发现组合组件 |

只嵌入Packages/com.xmjgame.pullet-framework.netclient。不得另保留四个旧模块包或它们的manifest/testables登记；否则会重复DLL/程序集/GUID。命名空间和程序集仍用PulletFramework.*。合包简化安装，不承诺更少依赖或更小包体。

升级前必须读[发现资产迁移](Discovery-U9B-Migration.zh-CN.md)和[消息API迁移](Messaging-U10-Migration.zh-CN.md)：旧Manager发现/RPC入口、默认JSON及属性推断已退役。保留脚本/Prefab GUID不代表旧持久方法监听仍可用。确认发现迁移是人工核对后的动作，不是自动修复；不得用直接确认或删除Library掩盖丢失配置。

## 只读导出当前输入

在正确的开发分支、源码停止编辑时执行：

```powershell
New-Item -ItemType Directory -Path ./artifacts -Force | Out-Null
./Tools/Export-PulletNetUnityPackage.ps1 -OutputDirectory ./artifacts/unity-preview-export
```

以上入口导出包含五个程序集的统一包。输出必须是不存在的新目录，父目录必须存在。工具不构建、不覆盖源码DLL、不安装业务工程，不允许输出进入源包、使用带重解析点的路径或覆盖已有证据。输出Package子目录是完整包，receipt.json保存包身份、三份NativeInput及逐文件SHA256；没有成功收据的部分输出不得安装。

模块连同主包整体复制，不能单独拆出Modules再安装UPM。U11旧五包快照继续保留回退，不覆盖或为改版本重建DLL；导出是只读固定快照，不是原子发布系统。

导出校验三份NativeInput的精确源码标识、raw wire3和全部四份DLL hash，并拒绝额外DLL/嵌套manifest/重复asmdef名或GUID，再验证复制前后文件。它证明字节快照，不自动证明所有平台或正式发布。使用方须对照准确检查点/提交，不可只看版本。可信本地文件系统并发编辑应先停止；工具不是跨进程锁。

旧Build-PulletNetUnityPackage脚本在存在NativeInput锁时直接拒绝，不允许重新编译并覆盖已验收的DLL。需要新Native输入时另建有限验证任务，记录新包/版本/source/hash及兼容性，不修改旧收据为新结果。

## 业务安装/升级（单独确认后执行）

1. 保留旧完整包、原manifest/lock及服务端包版本，并记录回退点。不要只备份两个DLL。
2. 在隔离业务工程副本中，按上表复制已验证的完整包到各自`Packages/<package.json的name>`，raw为`Packages/com.xmjgame.pullet-framework.netclient`。同时处理所需UPM依赖，不使用工程外file路径、软链接、目录联接或持续自动同步。
3. package.json的name仍为com.xmjgame.pullet-framework.netclient；代码/程序集保持PulletFramework.NetClient，不为改显示名称而改依赖标识。嵌入式包无需额外的工程外依赖，等待Unity生成对应embedded解析记录。
4. 核对全部文件/hash/GUID，检查Unity实际解析路径、编译、目标Player及真实服务器收发。相同包版本字符串不能替代内容核对；不把旧2.x的包体或业务代码覆盖到3.0 DLL上。
5. 只有经批准的业务分支验证通过后才切换正式工程。失败时恢复旧完整包和原依赖锁/对应服务端，不能只退回某个DLL或靠删除Library掩盖版本错误。

业务安装副本保持自身最后一次批准的输入；本清单不实时探测或断言妈祖当前版本。升级时先核对实际manifest/lock、包体和服务器输入，3.0仅支持wire3，不能假定仅替换客户端就兼容旧服务器。本批没有执行上述安装步骤；Framework与妈祖都未随U8—U11同步。

## Player/设备矩阵

| 目标 | 当前证据 | 仍需验证 |
| --- | --- | --- |
| Windows Unity 2022.3.62f3 Editor | U11实际PlayMode raw8/组合16场景、12/22响应/回显及退出 | 受控本机UDP/TCP/ws，不是跨机广播/容量验收 |
| Windows原生Player | U11：x64、Mono、Development、Low裁剪，最小raw7/组合16场景、10/22响应/回显及双方正常退出 | 两份成功构建各1条已归属headless环境光Warning；OS真实后台、Release/IL2CPP、真实Protobuf/AOT及其他版本未验 |
| Android IL2CPP | 待用户协作 | 构建/裁剪/AOT、设备收发、后台恢复、抓取日志 |
| PICO实际目标设备 | 待用户协作 | 真机连接、佩戴/摘下、休眠、Wi-Fi变化、资源收口 |
| macOS/Linux/iOS Unity Player | 未验 | 不根据SDK目标列表或Editor成功自动声明支持 |
| WebGL/小游戏 | 延后 | 当前包和示例程序集排除WebGL，没有原生Socket替代实现 |

### 每次验证固定输入

- Unity完整版本、包提交/逐文件hash、NativeInput的source/CI/DLL hash、平台/设备型号、OS、Mono或IL2CPP、架构、裁剪级别、网络拓扑和对端源码/配置。
- 使用独立、兼容wire3的受控服务器。手机/头显的127.0.0.1是设备自身；本批Windows loopback fixture不是可直接连的移动端服务。应配置测试机LAN监听地址、必要防火墙/网络权限，再测试UDP、TCP、WebSocket各自入口；不要为方便直接公网暴露开发回显服务。
- 简单场景使用一个Manager；多连接使用独立PulletClientRuntime，不创建多个单例Manager。默认来源创建前配置预算，连接成功不代表业务就绪。

### 固定操作和判据

1. 冷启动→连接→模式/ChannelId收发→断开→重新连接；核对来源、元数据和新会话，错误组合明确拒绝。
2. Scene-owned模式先await Shutdown再卸载场景；另测直接卸载的OnDestroy回退路径。DontDestroyOnLoad模式跨场景保持连接，最后由应用显式关闭。重复Manager不能关闭原连接。
3. 实际切后台/回前台、头显佩戴/休眠、网络短断。记录真实Pause/Focus顺序、SDK状态和来源身份、恢复结果；本批PlayMode SendMessage注入不是这些实际平台行为的证据。
4. 退出前await Shutdown并记录开始/结束及错误。Unity不会await OnDestroy/OnApplicationQuit，系统强杀/断电也不能承诺应用异步清理完成。不要在主线程同步Wait/Result。
5. 采集Unity Player日志、对端日志、连接/关闭原因、Runtime队列/准备/消费/丢弃快照、回显校验和关闭完成事实。超时保留首个错误、原日志和配置，不自动重试求绿或增加等待预算。

日常场景按既定1—2分钟规则，不新增30分钟长跑；最终较长测试集中一次，默认10分钟。目标平台构建/运行失败时只限制对应支持声明，不反推所有Native架构需要重做；先定位实际owner，再决定有限修复。
