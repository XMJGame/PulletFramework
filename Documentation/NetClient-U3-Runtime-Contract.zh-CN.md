# U3/U4：Unity多连接Runtime合同与接入

2026-10-16。本文件描述薄的 `PulletClientRuntime`，不改变Native网络协议或连接状态权威。当前源码仍是开发候选；验证使用准确dac3c64本地RC2，正式目录的NativeInput/DLL尚未切换。必须先满足新SDK输入的远端门槛，再把这一职责作为可用包提交；不要直接拿旧RC1退出行为冒充已修复。

本地实现与测试状态见[Runtime检查点](NetClient-U3-Runtime-Local-Checkpoint.zh-CN.md)，不是第三套执行计划。

## 谁负责什么

SDK `NetClientHost`创建和最终Dispose客户端；`HostedClient.Client`是借用，可以Connect/Send/Disconnect/Rebind，不可由另一owner直接Dispose。Unity Runtime只拥有自身接纳、主线程交付、预算和关闭结果，不重建连接状态机、不加入RPC/业务MessageId/发现/自动连接策略。

在Unity主线程构造Runtime，每帧调用一次Update；Update拒绝异线程和递归。其他线程可以请求Create/Remove/Close，所有SDK事实在原回调中先更新交付接纳，不等下一帧才失效。清理不依赖继续Update；网络线程不运行Unity用户回调。

每个来源用准确HostedClient引用归属，Key只供查询/显示。同名移除完成且其未交付故障已被消费后才可复用；旧句柄不能向新来源发事件、Payload或移除操作。DispatchGeneration是本层交付代际，不是wire中的SessionEpoch/BindingEpoch。新Handshaking/断开使旧Payload失效；没有经过Handshaking的保留Session重绑只暂停/恢复接纳，不伪造新Session。历史生命周期事实仍携带当时来源/代际，不是当前状态的另一份真相。

## 交付与过载

复用唯一ClientDispatchQueue，不把旧Manager的队列串进来。默认32来源、全局4096条/16MiB、每源1024条/4MiB、每帧256条/1MiB；先预留再复制，准备中/排队/消费中直到Owned.Dispose都计费。字节指有效Payload，不是ArrayPool容量/RSS。每源FIFO、跨源round-robin，条数/字节同时受限；不抢占耗时用户回调，不承诺帧时长SLO。

生命周期默认每源128条（含当前正在消费记录）、每帧64条；正常记录有界，Payload须等本来源之前的生命周期记录交付后才能消费。记录数量预算不等于异常对象/诊断字符串的精确内存预算。不可靠满载丢本次入站，不驱逐其他源；可靠溢出/复制失败、生命周期满载只使本来源进入固定首故障槽，关闭交付并发起受管SDK移除。故障不递归入满队列，不自动恢复不完整交付；应用通过DeliveryFaulted或Close/Remove结果制定重新同步策略。

Payload事件只借用本次Memory；跨回调/跨帧/异步保存必须先ToOwned，用完Dispose。每个订阅者异常独立报告，诊断订阅者再抛异常只累计DiagnosticHandlerFailures，不跳过释放或其他来源。接入时先订阅事件再连接，不能期待无订阅者时无限保留已消费通知。

LifecycleChanged.Detail映射：StateChanged→ConnectionStateChangedEvent；Connected→ConnectedEvent；ConnectFailed→ConnectFailedEvent；Disconnected→DisconnectedEvent；ReconnectAttempt→ReconnectAttemptEvent；Reconnected→null；ReconnectFailed→ReconnectFailedEvent。网络当前状态直接读取Source.Client.State。

## 移除和关闭的完成含义

RemoveAsync立即关闭准确来源交付，清理普通排队记录，等待SDK移除、准备/消费Payload和当前已进入Unity回调。移除结果的发布必须与“当前Callbacks==0”在同一锁下仲裁，不以旧的空闲信号代替当前回调责任。终态通知允许在SDK移除完成后交付；该未来通知不是先前SDK清理的一部分，但全局Close必须等待自己请求时已进入的通知回调。

CloseAsync不可逆，拒绝新Create/Remove；已接纳移除继续完成。重复Close共享同一任务；等待者取消只取消自己的等待，不取消真实清理。Close不用Update才能前进，逐源结果包含DeliveryFailure/CleanupError；一个owner失败不跳过其他owner和SDK Host最终join。成功移除后已忘记的来源不进入以后Close的集合，调用者应保留自己的Remove结果。

尚未交付故障保留一个来源槽及名称；Update消费故障且清理完成后才回收。若不再Update，Close直接聚合未交付故障并收口，不能永远依赖下一帧。代码可以在Payload/生命周期/故障回调中**请求**Remove/Close，但不可同步等待需要自己退出的任务，也不可将await任务解释成回调内同步完成。清理任务脱离SDK回调的执行上下文标记，由稳定完成任务跟踪，没有自动超时成功或无主重试。

## 最小使用示意（输入切换后）

```csharp
var runtime = new PulletClientRuntime(); // Unity主线程
runtime.PayloadReceived += e => {
    // e.Source.Key / e.Source引用区分来源；此处同步借用e.Payload。
    HandleBytes(e.Source, e.Payload);
};
runtime.DeliveryFaulted += e => ReportIncompleteDelivery(e.Source, e.Reason);
var source = runtime.Create("endpoint-a", new NetClientOptions {
    Connection = new ConnectionOptions {
        Host = "127.0.0.1", ConnectionMode = ConnectionMode.UdpOnly, UdpPort = 7777
    }
});
var connected = await source.Client.ConnectAsync();
if (connected.IsSuccess) await source.Client.SendAsync(payload, channelId: 0);
// 由自己的Unity帧owner每帧调用runtime.Update()。
// 由回调外可观察的应用退出流程等待：
var closed = await runtime.CloseAsync();
// 检查closed.IsSuccess及每个Clients结果，不把OnDestroy返回当作清理完成。
```

HandleBytes/ReportIncompleteDelivery/payload是应用自行提供的示意，不是新增框架API。Create不隐式连接；本批没有改MonoBehaviour、旧Manager、Inspector/Prefab或可选Messaging/Discovery。U5/U6再统一迁移门面和应用策略owner，不在这里复制另一套连接生命周期。
