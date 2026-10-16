# 安装、回退与原生Player协作清单

2026-10-16。开发源是PulletNet仓库UnityPackages，Framework是验证后的安装副本，业务工程使用自身Packages内固定副本。当前包仍为3.0.0-pre.1开发检查点，不是正式发布，不自动升级妈祖/XR。

## 只读导出当前输入

在正确的开发分支、源码停止编辑时执行：

```powershell
New-Item -ItemType Directory -Path ./artifacts -Force | Out-Null
./Tools/Export-PulletNetUnityPackage.ps1 -OutputDirectory ./artifacts/unity-preview-export
```

输出必须是不存在的新目录，父目录必须存在。工具不构建、不覆盖源码DLL、不安装业务工程，不允许输出进入源包、使用带重解析点的路径或覆盖已有证据。输出Package子目录是完整包，receipt.json保存包身份、NativeInput及逐文件SHA256；没有成功收据的部分输出不得安装。

导出会校验NativeInput的精确源码标识、wire3和两份DLL hash，再验证复制前后文件。它证明字节快照，不证明该工作区源码已经经过测试、平台验收或正式发布。使用方仍需对照本目录的准确检查点和提交，不可只看相同版本号。可信本地文件系统并发编辑应先停止；工具不是跨进程锁或原子发布系统。

旧Build-PulletNetUnityPackage脚本在存在NativeInput锁时直接拒绝，不允许重新编译并覆盖已验收的DLL。需要新Native输入时另建有限验证任务，记录新包/版本/source/hash及兼容性，不修改旧收据为新结果。

## 业务安装/升级（单独确认后执行）

1. 保留旧完整包、原manifest/lock及服务端包版本，并记录回退点。不要只备份两个DLL。
2. 在隔离业务工程副本中，复制已验证导出的Package到 `Packages/com.xmjgame.pullet-framework.netclient`。不使用工程外file路径、软链接、目录联接或持续自动同步。
3. package.json的name仍为com.xmjgame.pullet-framework.netclient；代码/程序集保持PulletFramework.NetClient，不为改显示名称而改依赖标识。嵌入式包无需额外的工程外依赖，等待Unity生成对应embedded解析记录。
4. 核对全部文件/hash/GUID，检查Unity实际解析路径、编译、目标Player及真实服务器收发。相同包版本字符串不能替代内容核对；不把旧2.x的包体或业务代码覆盖到3.0 DLL上。
5. 只有经批准的业务分支验证通过后才切换正式工程。失败时恢复旧完整包和原依赖锁/对应服务端，不能只退回某个DLL或靠删除Library掩盖版本错误。

妈祖当前固定2.0.2，3.0仅支持wire3。其升级必须同时处理业务服务器、客户端接入API及握手模式，不存在“仅替换客户端就能兼容旧服务器”的承诺。本批没有执行上述安装步骤。

## 原生Player/设备矩阵（尚待实际执行）

| 目标 | 当前证据 | 仍需验证 |
| --- | --- | --- |
| Windows Unity 2022.3.62f3 Editor | U5的135项/320次，U6实际PlayMode的8场景/12回显 | 不能替代独立Player与进程退出 |
| Windows原生Player | U7：Unity2022.3.62f3、x64、Mono、Development、Low裁剪，7场景/10回显及双方正常退出 | OS真实后台；Release/IL2CPP及其他版本组合不在本次证据内 |
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
