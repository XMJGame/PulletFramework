# U10消息层迁移：raw不再内置RPC或JSON

2026-10-18，替换本地02e3534及以前Unity包装层的旧消息API；不是Native API或wire3修改。
开发包当前仍为3.0.0-pre.1，未发布新包或自动升级下游。
安装前保存旧工程/资产；保留Manager/Relay脚本及Prefab GUID，不意味着旧C#消息接口二进制兼容。

## 入口与破坏性变化

raw PulletNetworkManager只负责原始连接、Payload、交付和关闭，所有原始入口保持。
移除ConfigureMessaging、IsMessagingConfigured、Subscribe、Unsubscribe、SendMessageAsync、CallAsync、
CancelPendingMessages、UnhandledBusinessMessage、MessageProtocolError；移除raw内PulletFramework.Messaging类型。
不提供Obsolete转发层、双pending字典、旧serializer兼容缓存或双消息分发。

| 旧入口/类型 | 迁移方式 |
| --- | --- |
| ConfigureMessaging(protocol, serializer) | 独立MessageSession，或既有Runtime上创建可选PulletMessageConnection；显式传IMessageCodec |
| PulletMessageProtocolOptions | PulletNet.Messaging.MessageProtocolOptions；显式核对Magic/version/maximumBodyBytes |
| PulletMessageFrame / Kind / Encoding | PulletNet.Messaging.MessageFrameCodec / MessageKind / MessageEncoding，不再复制Body |
| Subscribe<T>(id, handler) | connection.Session.Subscribe<T>(id, handler)，返回Native IMessageSubscription |
| manager.Unsubscribe(handle) | handle.Unsubscribe()/Dispose；需真实退出时await handle.Completion/UnsubscribeAsync |
| MessageSubscriptionGroup | 应用持有它创建的订阅句柄集合，逐项Unsubscribe并join Completion；没有另一个框架订阅owner |
| PulletMessageAttribute/Contract/Metadata及无ID重载 | 退役；显式传通知/请求/响应ID，种类由SendNotification/Call/SendResponse方法决定；业务元数据留在应用 |
| JsonMessageSerializer / IMessageSerializer | 退役；应用实现IMessageCodec，或另行采用已验证codec；没有默认JSON/自动编码协商 |
| SendMessageAsync | Session.SendNotificationAsync(id,value,timeout,token)，返回明确MessageSendResult，不忽略Rejected |
| CallAsync | Session.CallAsync(requestId,responseId,value,timeout,token) |
| CancelPendingMessages | 单次/模块取消使用应用传入的CancellationToken；来源失效则关闭准确消息会话，网络组合用connection.CloseAsync，不在保留Session上重开RpcId |
| UnhandledBusinessMessage/MessageProtocolError | inputDiagnostic中的具体Response/Notification状态、FrameError；Native通知diagnostic和Snapshot |

PulletNetworkEventRelay.onNetworkError字段及已有监听保持，仅桥接网络/交付错误，
不再暗含消息协议错误；原先依赖该路径的UI/日志应显式接到typed inputDiagnostic/diagnostic。
没有删除任何Relay序列化事件字段，因此不猜测或自动重写旧持久监听。
升级前还须搜索自己场景/Prefab中的旧m_MethodName（特别是CancelPendingMessages），
将直接调用已移除Manager方法的持久事件显式迁移；GUID不变不能保证旧方法监听仍有效。

Native信封仍为原先24字节小端布局，但不等于业务包互通已经自动认证：
必须对齐Magic/version/encoding/业务ID和codec。Native默认最大Body是64KiB，旧默认1MiB；
若确需旧上限，应显式指定，同时核对RPC预留字节及网络Payload/帧预算，不静默扩大。
Native RPC/通知现在有明确容量与最大期限限制，超时/取消后的发送资源仍跟踪到真实退出。
消息来源关闭使pending以ObjectDisposedException终止，不再伪装成调用方取消；调用方token取消仍为取消结果。
错误响应ID不再消耗正确pending；未知/迟到响应不进入通知，也不会自动重放。

## 网络接入与关闭

简单raw应用仍用PulletNetworkManager，不安装Messaging。
typed应用使用已有PulletClientRuntime和可选NetClient.Messaging，不把独立拥有私有客户端的Native
Messaging.ClientSDK.MessageClient塞入Runtime。一个Runtime可创建多个明确来源。
应用显式指定EnableReconnect=false；不会修改Manager自动恢复设置或接管已创建的Manager客户端。

先构造MessageSession/订阅，再连接；每帧只泵该Runtime.Update。
PulletMessageConnection是一次性incarnation；同名新绑定必须等旧绑定Completion真实退出。
不支持该绑定的透明自动恢复/Suspend/Resume/UDP保留重绑，不能用新的本地Source掩盖保留Session。
发现可先使用独立DiscoveryRuntime选择端点，再创建typed配置；不把raw自动连接Connector强接到typed来源。

退出先请求消息接纳关闭，允许网络owner停止IO，再等待旧消息和已进入回调。
在handler/codec内只请求Close并返回，外部await connection.Completion；不能在回调里等自己。
随后再关闭Runtime。Runtime先被关闭时，也必须join绑定Completion确认可选消息退出。
旧版Dispose即返回不再被解释为真正清理完毕；本地Accepted不代表远端ACK或业务已处理。

## 删除测试的逐项行为去向

旧文件PulletMessagingTests.cs的14个测试不因名字旧而直接丢弃：

| 旧测试 | 对应去向/明确退役 |
| --- | --- |
| JsonSerializer_RoundTripsPublicFields | 默认JSON能力退役；应用codec/真正Protobuf验收由使用方负责，不用标量夹具冒充JSON/Protobuf认证 |
| Frame_RejectsWrongBusinessMagic | MessageSessionTests.MalformedInputsAndRequestsNeverReachCodecOrHandlers（本批补明确MagicMismatch和零callback断言） |
| Client_AcceptsCustomSerializer | 独立MessageSession的显式ScalarCodec收发及三传输真实RPC/通知 |
| TypedNotification_InfersMessageIdAndKindFromContract | 属性推断退役；NotificationAndResponseSendsHaveExplicitLocalAdmission明确检查种类/RpcId与显式ID |
| TypedSubscribe_RejectsRequestContract | 属性契约退役；MalformedInputsAndRequestsNeverReachCodecOrHandlers确保Request不进入通知 |
| Subscription_CanBeExplicitlyUnsubscribed | 本批ExplicitUnsubscribeStopsDeliveryAndIsIdempotent；原UnsubscribeIsCountedUntilAdmittedDispatchActuallyReturns保留 |
| SubscriptionGroup_UnsubscribesAllOwnedSubscriptions | 本批ApplicationOwnedSubscriptionHandlesRetireOnlyTheirOwnCallbacks，验证应用集合逐项退订及非本组订阅继续有效 |
| Subscribe_RejectsDifferentTypesForSameMessageId | 本批OneNotificationIdCannotUseTwoDtoTypes |
| HandlerFailure_DoesNotBlockRemainingSubscribers | NotificationExceptionsStayIsolatedAndPayloadIsOnlyBorrowed，保留Native诊断与借用检查 |
| Rpc_CompletesOnlyMatchingResponse | ExactResponseCompletesOnceAndNeverEntersNotificationRouter及SourceAndResponseIdsRejectBeforeDecodingAndDoNotConsumePending |
| CancelPending_EndsOldSessionRpc | OldSourceCannotCompleteAnotherSessionEvenWhenRpcIdsMatch及真实RuntimeCloseCancelsPendingRpcWithoutAnotherFrame；关闭结果语义见上 |
| Rpc_TimeoutIncludesTimeSpentWaitingForSend | TimeoutIncludesSendAndDoesNotReleaseUnfinishedIo，保留资源直到真实退出 |
| Rpc_DisconnectCancellationDoesNotWaitForPendingSend | CancellationRetainsBudgetAndCloseJoinsNonCooperativeSend，调用结果与owner完成分开 |
| UnknownRpcResponse_IsNotDeliveredToNormalSubscribers | 本批UnknownResponseNeverCallsNotificationSubscribers，加原重复响应测试 |

Manager测试MessagingInvalidationSuppressesRawPayloadDelivery迁为可选组合网络测试
CloseInsideNotificationJoinsRuntimeCallbackAndSkipsLaterHandlers（本批新增后续raw订阅零调用断言）；
raw SessionBoundaryInsidePayloadPreventsStaleSecondSubscriber原样保留。
它们分别验证typed请求关闭/Native实际代际变化后，不再调用后续raw订阅，不保留旧消息路由。
没有删除其余raw生命周期/关闭/故障/自动恢复测试；新增raw程序集依赖和已退役API边界检查。

## 包与尚未覆盖

raw package.json不再依赖Json.NET，asmdef仅引用Protocol/ClientSDK；可选Messaging/组合包不引用JSON。
独立Discovery按自身协议仍可依赖Json.NET，不把“raw无JSON”误写成整个发现功能无JSON。
核验开发源、隔离嵌入包、readonly导出；不更新PulletFramework/妈祖安装镜像或推送。
U11最后集中实际PlayMode/Mono Player，Android IL2CPP/PICO及Unity2021.3仍待验。
