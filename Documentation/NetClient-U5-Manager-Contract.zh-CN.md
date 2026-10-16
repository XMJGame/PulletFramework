# U5：默认来源 Manager 与可等待应用关闭

2026-10-16。本批只将旧MonoBehaviour接入已完成的Runtime，并收口它持有的应用策略/发现任务。Native网络核心、wire3、DLL输入、业务协议不改；不是新的连接状态机。开发在 `codex/unity-runtime-manager` 的 `UnityPackages`，验证后才向Framework同步，不更新妈祖/XR安装。

## 单一所有权

SDK NetClientHost创建/释放NetClient，PulletClientRuntime持有交付预算及准确来源；Manager只借用一个`default` HostedClient。没有外部SDK客户端Attach或手动Dispose路径。端点切换等待Runtime.RemoveAsync，不以同步Dispose冒充退出。

Manager的连接意图/OperationGate仅决定应用选择哪个端点，并串行化连接、断开和挂起/恢复命令，不维护SDK状态。IsConnected直接读取SDK。Runtime只在Awake所属Unity线程首次创建；在该线程使用Manager连接/配置/Update。Shutdown可以由其他线程请求。首个来源创建时冻结Payload条数/字节/帧配置。

网络生命周期和Payload直接由Runtime交付，不串接旧EventPump或另一套Payload缓存。默认1024条/4MiB、每帧256条/1MiB；单条超过完整帧字节预算拒收。可靠交付不完整后默认来源被退役，Manager请求整体Shutdown，不能自动恢复不完整的业务状态。Shutdown保留该终态，即使不再Update。

可选Messaging仍在包装层。仅配置Messaging才为它的byte[]接口复制一份；纯原始回调借用Runtime内存。订阅快照按订阅变化更新，不每包GetInvocationList。每个原始订阅者独立隔离异常，回调内发生来源/代际变化后不再交付后续旧Payload。SDK实际Handshaking/断开边界立即取消待完成Messaging请求，不等Unity事件到达才取消。

显式Remove丢弃未消费普通生命周期记录；已交付记录仍是历史事实，同名替换不继承旧句柄事件。这是相对旧Manager“移除后仍发布排队事件”的显式行为调整，与既定Runtime移除合同一致。

## 有界应用责任与关闭

Manager最多32个同时接纳的应用操作，覆盖显式连接、自动策略、可等待发现、断开、挂起/恢复和应用暂停策略；回调正在执行时也进入关闭等待。自动恢复由原Recovery owner的Completion/StopAsync单独跟踪。策略的失败连接清理改为Func<Task>，必须真实完成才能下一轮，不新增独立生命周期模块。

仅发现/应用控制事实使用独立的128条有界队列（包含当前消费记录），每帧最多64条；SDK记录不再穿过它。满载保留固定首错误，直接请求Shutdown并清空未交付事实，不把终态塞进满队列。操作容量满拒绝新操作，不无限创建任务。

`ShutdownAsync(CancellationToken)`不可逆，在同一接纳锁下关闭入口、发布唯一共享Task，随后请求Recovery停止、取消应用lifetime、关闭Runtime；所有stop先请求，再等待已接纳应用操作、Recovery真实退出、Runtime及当前回调结算，最后释放Messaging/取消源。不能先等OperationGate而让阻塞Connect等不到网络中断。一个取消回调或清理失败不跳过其他owner，错误以AggregateException保留。

预取消不启动关闭。后续单个等待者取消只放弃自己等待，不取消共享清理；无token重复调用返回同一Task。回调可以请求关闭，但不得同步等待需要自己返回的Task。无需继续Runtime.Update才能关闭，仍须保持原Unity SynchronizationContext可前进，不允许主线程Wait/Result阻塞。

OnDestroy和OnApplicationQuit只请求并观察关闭；Unity不会等待它们。需要退出收据时，应用必须在销毁前显式await ShutdownAsync。Pause/Focus用两个应用原因合并，读取最新意图并走原命令gate，避免失焦与暂停互相提前Resume；只恢复此前已连接的默认来源，不隐式发现/换服。实际Player/头显休眠仍待平台验收。

Manager发现使用独立DiscoverAsync，不再订阅或停止进程静态StartDiscovery。关闭等待本轮已发起的广播/Receive。发现Core在自己的token取消时将Send/Receive的Socket释放错误归为本轮取消，保留其他错误；finally关闭socket并await未完成Receive，不遗留无主IO。

## 旧机制/测试退役映射

旧BoundedPayloadQueue、PulletMainThreadEventPump及各自6项测试从当前源码退役，可从Git及迁移前归档恢复。不是按覆盖率删除，也不把旧失败删除求绿：

| 原保障 | 当前保障/测试 |
| --- | --- |
| 普通Action FIFO/异常隔离 | ManagerLifecycle.ApplicationActionsAreOrderedAndCallbackFailureDoesNotSkipTheNextAction |
| Session失效清空、代际保留、晚到输入安全 | ClientDispatchQueue.InvalidateOnlyClearsItsSourceAndNewSessionHasDistinctGeneration；Runtime.NewHandshakeInvalidatesOldPayloadAndAdvancesDispatchGeneration；Close/Retire竞态 |
| Messaging回调失效后不发Raw | ManagerLifecycle.MessagingInvalidationSuppressesRawPayloadDelivery |
| 帧数/字节预算和元数据 | ClientDispatchQueue的FrameByteBudget/RoundRobin/DeliveryBorrowsOwnedCopy；Runtime.PayloadHasExactSourceGenerationAndChannelMetadata |
| 可靠溢出、清空释放、消费/复制所有权 | ClientDispatchQueue的ReliableOverflow/ConsumingPayload/CloseJoinsPreparation；ManagerLifecycle.ReliableOverflowRemainsFailedOnShutdownWithoutAnotherFrame |
| 旧丢弃最老策略 | 显式替换为当前入站拒收，不驱逐其他源；ClientDispatchQueue.UnreliableOverflowNeverEvictsAnotherSourcesPayload |
| 已接纳生命周期有序、旧来源迟到隔离 | ManagerTests.LifecycleFactsAreOrderedUntilExplicitSourceRetirement；Runtime.NameReuseAndLateOldFactsCannotTargetTheReplacement |

验证、首轮失败和剩余边界记录在本目录后续检查点/机读收据；本合同不是未执行平台的验收声明。
