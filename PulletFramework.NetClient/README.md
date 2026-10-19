# PulletFramework.NetClient

Unity统一客户端包，开发版本3.0.0-pre.3。包ID保持
`com.xmjgame.pullet-framework.netclient`，namespace保持`PulletFramework.NetClient`。
唯一开发源是PulletNet仓库UnityPackages；Framework和业务工程只是另行安排的固定安装副本。

## 当前范围

交付只需一个完整UPM包：连接/主线程交付、typed消息、局域网发现及组合适配均包含在内；
内部仍是五个独立asmdef，命名空间/程序集名/GUID和运行时所有权不变。
不使用的功能不自动启动，不新增Host或Payload队列。服务端/NuGet拆分不变。

原始连接程序集只负责Unity主线程交付、连接配置、生命周期和默认来源Manager。NativeInput锁定
29d923e / Windows CI39205953671 attempt1的Protocol(netstandard2.0)与ClientSDK(netstandard2.1)
两份实际DLL，不含NetCore/Hosting，不原地重建DLL。
raw程序集不引用Messaging/Discovery/Json.NET，也不提供默认业务codec；
消息与发现的程序集位于Modules/，按需使用，不再逐个导入4份附加UPM包。
统一包因发现协议声明com.unity.nuget.newtonsoft-json 3.2.1依赖；即使只用raw也会解析它，
这是简化安装的明确取舍，不承诺更小包体或绝对零依赖。
旧ConfigureMessaging/Subscribe/CallAsync/SendMessageAsync及PulletMessage*接口已移除，
升级前阅读开发仓库Docs/Unity/Messaging-U10-Migration.zh-CN.md；没有静默转发或旧RPC兼容层。

Discovery保留独立程序集，连接核心不持有发现Socket/DTO/策略。
Modules/Discovery提供独立DiscoveryRuntime，Modules/NetClient.Discovery提供组合选择策略与Inspector桥接；
Modules/Messaging提供独立消息会话，Modules/NetClient.Messaging提供现有Runtime上的typed绑定。
Messaging与raw同为29d923e原始候选DLL；Discovery保留d05c59c独立输入。
NativeInput锁定实际包/DLL字节，不能仅凭同名rc.1版本推断来源一致。

## 从旧五包布局迁移

先备份工程Packages/manifest.json、packages-lock.json及嵌入包，再用完整pre.2固定副本替换旧netclient。
移除旧discovery/messaging/netclient-discovery/netclient-messaging四个独立包及其manifest/testables登记；
不能同时保留旧模块包，否则会重复DLL、asmdef和脚本GUID。移动已有文件保留.meta，
程序集/命名空间/API不因包合并改名，但不承诺此前已退役API自动兼容。
不要把Modules内文件拆出来再安装成UPM包。先在隔离工程验证，再安排Framework/业务迁移。
本次只修改开发源，未执行任何下游迁移或原生DLL升级。

旧Manager/Relay发现成员已移除；升级前先保存旧资产/配置/事件备份，
按开发仓库Docs/Unity/Discovery-U9B-Migration.zh-CN.md逐项迁移。
旧autoConnectMode会触发明确的迁移拒绝，不静默解释成固定连接。
只有核对配置/持久事件完成后才ConfirmDiscoveryMigration并保存资产；
Editor脚本迁移时须用Undo.RecordObject/EditorUtility.SetDirty并保存，不以运行期确认代替持久化。
ActiveServer改为ActiveEndpoint；发现元数据由发现消费者保留。

## 连接与使用

通用Prefab保持原GUID，默认不自动连接。固定连接时显式启用Manager.connectOnStart，
或由自己的界面调用ConnectAsync。要自动发现则关闭这个选项，由可选Connector负责启动。
端口只验证所选传输：UdpOnly=1、TcpOnly=4、WebSocketOnly=5；旧2/3及未知值拒绝，不静默降级。
WebSocket路径/TLS仍是显式配置，不从发现结果推断。

```csharp
using PulletFramework.NetClient;
using PulletNet.ClientSDK;

var result = await manager.ConnectAsync(
    new ConnectionEndpoint("127.0.0.1", 7778, 7777, 7779), cancellationToken);
if (result.IsSuccess)
    await manager.SendAsync(payload, ChannelType.ReliableOrdered);

// 原固定配置入口仍可用：
await manager.DisconnectAsync();
await manager.ConnectAsync();

// 退出前等待真实关闭，再销毁对象：
await manager.ShutdownAsync();
```

准确端点入口不补缺端口。ActiveEndpoint是最后成功端点，不等于当前在线；看IsConnected。
修改Inspector、失败候选或断开不覆盖最后成功目标。
新的显式连接/断开使旧意图失效；可选策略通过本地ConnectionRevision观察取代，不新增SDK状态机。
RetireDisconnectedClientAsync只回收断开默认来源，等待真实Remove，不断开活跃来源；
显式移除会丢弃尚未消费的旧来源普通通知，新来源不继承旧句柄事件。

代码订阅Connected、ConnectFailed、Disconnected、PayloadReceived及重连事件。
可选PulletNetworkEventRelay只桥接原始连接/Payload/网络错误的Inspector监听，不包含发现/typed协议事件。
onNetworkError不再转发旧MessageProtocolError；需要业务消息诊断时显式接入typed inputDiagnostic。
原始Payload仅在回调中借用；需跨帧/异步持有时ToOwned并在用完后Dispose。

## 多连接、预算与关闭

多连接使用独立PulletClientRuntime及明确的帧owner，不创建多个单例Manager。
默认Payload预算1024条/4MiB、每帧256条/1MiB，首次创建默认来源时冻结。
准备/排队/消费有效字节都计费；超完整帧预算的单条消息在复制前拒收。
不可靠过载拒收本条、不驱逐旧数据；可靠交付不完整会终止来源，不能自动恢复业务状态。
Send成功是本地接纳，不是对端ACK，更不是业务已处理。

ShutdownAsync永久关闭接纳，并等待已接纳操作、恢复和Runtime真实退出；
一个等待者取消只放弃自己的等待，不取消共享清理。
OnDestroy/OnApplicationQuit只请求关闭，Unity不等待这些回调。不可同步Wait/Result堵住主线程。
使用可选发现组合时先await connector.CloseAsync，再await manager.ShutdownAsync。

## 支持与验证边界

当前实际工具链Unity2022.3.62f3/Windows Editor及x64 Mono Development Low裁剪Player。
U11已用实际PlayMode和最小raw/可选组合Player完成有限收发/场景/退出验收，
准确范围见开发仓库Docs/Unity/U11-PC-Acceptance.zh-CN.md；不是容量、全部平台或正式发布认证。
清单最低2021.3不是已实测声明；早期U6/U7 PlayMode/Mono Player只认证当时输入。
U9发现检查点不替代U11；当前仍不认证OS真实后台、Android IL2CPP/PICO或跨机广播。
WebGL/小游戏暂不支持；网络核心、KCP和目标平台扩展不在本批。

安装使用完整固定包的嵌入副本，不使用工程外绝对路径、软链接或自动追随开发源。
Tools/Export-PulletNetUnityPackage.ps1仅做只读新目录导出；不更新DLL或自动安装下游。
包内Connection Sample保留原始连接/收发/关闭示例，不定义业务协议。
