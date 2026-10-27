# PulletFramework.NetClient.Messaging

统一netclient包内的可选Unity typed组合模块，不再独立安装UPM包。
namespace/程序集仍为PulletFramework.NetClient.Messaging；版本与迁移步骤见根README。
不包含 DLL、不创建第二个 NetClientHost、不增加 Payload 队列。

## 一条绑定就是一次网络会话

在 Unity 主线程、使用同线程构造的既有 PulletClientRuntime 创建 PulletMessageConnection，
传入本地 key、NetClientOptions、MessageProtocolOptions 和应用的 IMessageCodec。
绑定通过这个 Runtime 创建唯一的 HostedClient；最终 SDK Dispose 始终由 Runtime/SDK Host 执行。
没有 Attach 外部客户端、全局 current-client 路由或默认序列化器。

先通过 connection.Session.Subscribe<T>(id, handler) 配置订阅，再 await connection.ConnectAsync()。
连接成功并明确确认 SessionContinuity.NewSession 后，IsReady 才为 true，才接受消息发送。
通过 Session.CallAsync / SendNotificationAsync / SendResponseAsync 使用独立 Messaging 的同一套 RPC/通知 owner。
应用每帧只调用已有 runtime.Update()；不再泵第二队列。

第一版明确使用一次性 incarnation：一个绑定只允许一次 Connect 尝试。
显式关闭并等待 connection.Completion 后，才能使用同名的新绑定连接新 Session。
失败重试也创建新绑定，不在原 HostedClient 上重置 RpcId；名称复用不复用来源身份。

network.Reconnect.EnableReconnect 必须由调用者显式设为 false，否则构造时拒绝、不会修改配置。
通道必须是 ReliableOrdered，默认 ChannelId=0；不按 DeliveryMode 枚举值猜 ID。
不暴露 SDK 客户端供操作。虽然 Runtime 可查询借用来源，使用者不能绕过绑定调用其生命周期方法。
初始 Connected 在 SDK 实际同步发布点观察；收到后续恢复/退役事件便永久关闭本绑定，
不会根据晚到的 Unity Connected 事件、SessionId 数值或当前布尔状态重建消息 registry。
**本版不支持同一绑定上的 Suspend/Resume、透明重连或 UDP PreservedSession/Rebind。**

## 输入与诊断

Runtime 在复制时捕获准确 HostedClient 和 DispatchGeneration，派发前检查/作废旧代际。
绑定只接受自己的准确来源与选定可靠有序 lane，直接借用现有 Payload，不重新复制 Body。
同一个 HostedClient 永不安装第二个消息 registry，所以消费时不会把旧来源改贴成新消息来源。
响应不进入通知订阅；全部头校验、RPC 匹配、容量和订阅隔离复用 PulletNet.Messaging。

可选 inputDiagnostic 收到 MessageInputResult；Kind 只是路由分类，须检查具体 Response/Notification 结果。
此回调也在原 Runtime 主线程派发中，异常由 Runtime.CallbackError 隔离报告，不引入诊断队列。
通知 handler/codec 的诊断可使用 diagnostic 与 Session 的两个 Snapshot。
本地 Accepted 不是物理完成、远端 ACK 或业务处理；不添加业务确认或请求重放。

## 关闭与线程

消息 API/Connect 在创建它们的 Unity 主线程调用；RequestClose/Close 可以从其他线程请求。
在通知、codec 或诊断回调里只能请求关闭并返回，不可同步等待或 await 自己的退出，禁止 async void handler。
稳定 Completion 等待本来源真实移除、已接纳 Connect、消息准备/发送及已进入 Runtime 回调退出。
先关闭消息接纳，允许 Runtime 先终止旧 IO，再 join 消息工作；不会等阻塞 IO 的消息退出后才停止 IO。
关闭不需要额外 Update，不关闭 Runtime 内的其他来源；一次等待者取消不取消共享清理。
Runtime 主动关闭/移除来源时，本绑定同样观察其准确退出 Task，关闭并等待消息 owner。
GetRemovalCompletion 只观察，不主动移除；必须在来源注册仍可查询时捕获，不能以后按名称找替代项。

只关闭 Session 不等于关闭网络：IsReady=false，但 binding.Completion 仍等待 connection.CloseAsync。
调用 Runtime.CloseAsync 只能证明 Runtime 自己的 owner 退出；应用还应 await connection.Completion，
以确认可选消息工作也已完成。清理失败通过 Completion 故障报告，不用提前成功或强制归零掩盖。

## 当前迁移边界

这不是旧 PulletNetworkManager.ConfigureMessaging 的兼容包装。
U10已迁出旧Manager RPC/JSON；raw无消息路由或Json.NET依赖，不需要安装本可选组合包。
旧属性推断、默认JSON与订阅组工具明确退役；显式ID、IMessageCodec和Native订阅句柄替代。
公开入口、关闭语义及旧测试行为去向见开发仓库Docs/Unity/Messaging-U10-Migration.zh-CN.md。
U11已完成Unity2022.3.62f3 Windows实际PlayMode及x64 Mono Development Low裁剪Player有限验收，
含发现后typed、回调关闭、pending退役和双来源隔离；详见开发仓库Docs/Unity/U11-PC-Acceptance.zh-CN.md。
测试使用标量codec。目标设备情况见支持矩阵；同一绑定的重连、保留重绑仍不支持。
