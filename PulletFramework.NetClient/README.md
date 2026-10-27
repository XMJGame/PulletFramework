# PulletFramework.NetClient

Unity网络客户端，提供连接、多连接管理、主线程消息派发、消息会话与局域网发现。
包ID为`com.xmjgame.pullet-framework.netclient`，命名空间为`PulletFramework.NetClient`。
开发源位于PulletNet仓库的UnityPackages，PulletFramework仓库提供安装副本。

参数用途、默认值、传输生效条件和遗留项见包内 [Configuration.md](Configuration.md)。
Inspector提供常用连接参数，完整配置见上述指南。底层DLL来源与校验值记录在NativeInput.json。

## 模块组成

安装一个UPM包即可使用连接、消息和发现能力，内部按五个程序集组织。可选功能由应用按需创建。

连接程序集负责Unity主线程派发、配置、生命周期和默认连接Manager，底层使用Protocol与ClientSDK，不包含服务端NetCore或Hosting。
raw程序集不引用Messaging/Discovery/Json.NET，也不提供默认业务codec；
消息与发现的程序集位于Modules/，按需使用，不再逐个导入4份附加UPM包。
统一包因发现协议声明com.unity.nuget.newtonsoft-json 3.2.1依赖；即使只用raw也会解析它，
旧ConfigureMessaging/Subscribe/CallAsync/SendMessageAsync及PulletMessage*接口已移除，
升级前阅读开发仓库Docs/Unity/Messaging-U10-Migration.zh-CN.md；没有静默转发或旧RPC兼容层。

Discovery保留独立程序集，连接核心不持有发现Socket/DTO/策略。
Modules/Discovery提供独立DiscoveryRuntime，Modules/NetClient.Discovery提供组合选择策略与Inspector桥接；
Modules/Messaging提供独立消息会话，Modules/NetClient.Messaging提供现有Runtime上的typed绑定。
Messaging、Discovery与raw均为同一干净源码10f9619的3.0.0产物；Discovery自身协议仍为1。
版本与平台测试见开发仓库Docs/Releases/Support-Matrix.md。

## 从旧五包布局迁移

先备份工程Packages/manifest.json、packages-lock.json及嵌入包，再用完整且已校验的3.0.0固定副本替换旧netclient。
移除旧discovery/messaging/netclient-discovery/netclient-messaging四个独立包及其manifest/testables登记；
不能同时保留旧模块包，否则会重复DLL、asmdef和脚本GUID。移动已有文件保留.meta，
合包保留程序集、命名空间和脚本GUID。Modules内的目录不是独立UPM包，不要重复安装。

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

## 运行环境

连接与组合功能已有Unity2022.3.62f3 Windows Editor及x64 Mono Player测试，详见开发仓库Docs/Unity/U11-PC-Acceptance.zh-CN.md。
Inspector另有Unity2021.3兼容测试。设备、系统后台和跨机广播测试按支持矩阵查询；WebGL与小游戏暂不支持。

安装使用完整固定包的嵌入副本，不使用工程外绝对路径、软链接或自动追随开发源。
Tools/Export-PulletNetUnityPackage.ps1仅做只读新目录导出；不更新DLL或自动安装下游。
包内Connection Sample保留原始连接/收发/关闭示例，不定义业务协议。
