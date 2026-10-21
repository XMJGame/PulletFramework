<!-- Generated fixed usage snapshot; authority: Docs/Usage/Configuration.md at b504f01. -->
# 配置指南：先接通，再按证据调优

本页是当前参数的使用入口，覆盖 NetCoreOptions 的 89 个叶子参数及 NetClientOptions 的 32 个叶子参数。子对象不重复计数。可选 Hosting、Messaging、Discovery 和 Unity 配置在文末单独导航，不应把所有模块的参数复制到每个应用。

默认值和字段名由源码决定；[API 与配置契约](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/Docs/Architecture/Contracts/Protocol-API-and-Configuration.md)说明线程、握手、保活和结果语义，本页说明如何选择参数。**有消费者不等于需要每个用户手调；通过校验也不等于部署容量达标。**

## 1. 最小入口与配置时机

先决定单连接使用 UDP、TCP 还是 WebSocket；组合连接使用独立实例，不是一个 Session 混装传输。服务端仅填写 Options 不会启动对应 Transport，还须调用 UseUdp / UseTcp / UseWebSocket。

```csharp
// 服务端：其余使用核心默认值，不把应用参数覆盖误称为库默认值。
var options = new PulletNet.NetCore.Core.NetCoreOptions();
options.Listen.UdpHost = "0.0.0.0";
options.Listen.UdpPort = 7777;
var runtime = PulletNet.NetCore.Core.NetCoreBuilder.CreateServer(options)
    .UseUdp().BuildServer();
await runtime.StartAsync(); // 调用方须检查 NetResult，并最终 Stop / DisposeAsync。

// 客户端：这里的目标必须是客户端可达的地址，不能填写 0.0.0.0。
var clientOptions = new PulletNet.ClientSDK.NetClientOptions();
clientOptions.Connection.Host = "127.0.0.1";
clientOptions.Connection.UdpPort = 7777;
clientOptions.Connection.ConnectionMode = PulletNet.ClientSDK.ConnectionMode.UdpOnly;
// 如果业务需要更新票据后重新入场，由应用管理恢复：
clientOptions.Reconnect.EnableReconnect = false;
// 交给 new NetClient(clientOptions)，注册回调后 ConnectAsync，最终等待释放。
```

这是配置片段；带收发、结果检查和完整释放的例子见[快速开始](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/Docs/Usage/Getting-Started.md)与[原始回显](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/Samples/RawPayloadEcho/Program.cs)。TCP / WS 选择对应端口、模式和 Use 方法；WS 的 path 须匹配，TLS 还需要证书/部署配置，不是勾选开关即完成安全验收。

- 服务端在 BuildServer 捕获深拷贝，客户端在构造时捕获并校验；修改调用方原 Options 或 Runtime.Options 返回的副本，不会热改实例。复制期间不要并发写 Options。
- UseUdp(port) / UseTcp(port) / UseWebSocket 的显式端口、路径、主机参数会覆盖服务端快照内对应 Listen 值；不要在两处配置互相矛盾的值。自定义 Transport 的 Socket 参数由适配器自身负责，不能假设它自动读取所有内置 TransportOptions。
- 下表“条件生效”指运行消费者，不保证未使用字段免于校验。服务端通常仍校验 TCP/WS 地址端口、禁用 Worker 的正数预算、遗留字段等；Group、握手速率有各自的条件校验。
- 表内字节数均为十进制整数；MiB=1048576 字节。TimeSpan 默认值统一写成毫秒以便比较，赋值时仍用 TimeSpan，不改属性类型。

## 2. 配置分层与调优顺序

常用：实际地址/端口、Session 容量/超时、Tick 频率、握手期限/到达率、UDP MTU、统计报告；TCP/WS 用户自己的地址端口也是常用项。高级：各阶段资源、重试/公平性、执行线程与 Socket。可选：显式使用 Group / Messaging / Discovery 后再配置。固定：ProtocolVersion。遗留：三个没有运行消费者的兼容字段。

| 观察到的现象 | 先查什么，再考虑调整 | 不应采取的替代做法 |
| --- | --- | --- |
| 批量连接超时 | 监听/可达性、Runtime 是否推进、握手限流及候选水位；初始握手至少 Request/Confirm 各扣一次，重试再扣。按到达率设置全局速率/突发并保留来源保护 | 直接关闭限流，或只加客户端等待时间；在线人数不等于每秒建连数 |
| UDP 高 PPS 丢包 | 实际 OS 接收缓存、接收拒收/处理速率、Tick、应用扇出；必要时调 UdpReceiveBufferSize | 把 MTU、OS 缓存、托管队列当同一容量；无限加深队列 |
| 本地发送背压 | 先识别逻辑接纳、准备、物理驻留、完成记录中哪一项满，再看消费/IO 是否持续跟得上 | 所有上限乘十；Send 成功当作远端收到 |
| 可靠数据延迟 | RTT、丢包、重传、每 ID 在途跨度、队列和调度；按实测设重试间隔与预算 | 将 MaxPendingPackets 调大当作扩大 ACK 窗口；TCP/WS 叠加 UDP ARQ |
| 回调卡顿 | 派发线程、队列、单回调耗时、应用是否阻塞；决定 TickInline 或 SingleThread | 在回调中同步等自身退出；误认为 SingleThread 是 Unity 主线程 |
| 保活误判/恢复冲突 | 联合检查客户端间隔/抖动/整轮探测期限与服务端 SessionTimeoutMs；确认只有一个恢复 owner | 认为任意 Ping 超时等于断线，或默认两端期限自动协商 |

先改一个责任域并比较延迟、丢弃、队列水位和内存；无业务 SLO 时只报告基准。多 Runtime 预算累加，没有进程统一内存上限。保活详细判定（含 UDP 新确认进展）以[契约](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/Docs/Architecture/Contracts/Protocol-API-and-Configuration.md#保活探测独立控制面不是可靠数据channel)为准，不能用简单的“几次没回复”替代。

## 3. 服务端参数索引

路径相对 NetCoreOptions。每行列出默认值、作用/消费者及主要限制。常用不是要求全部显式填写。数值范围是配置校验，不是吞吐保证。

<!-- server-options -->
### 基础、调度与会话

| 路径 | 默认值 | 层级 / 条件、消费者和调整边界 |
| --- | --- | --- |
| `ProtocolVersion` | `3` | 固定；验证器仅接受当前 wire3，不是降级开关 |
| `Tick.TickRate` | `60` | 常用；TickScheduler 每秒目标 Tick 数，>0；增大增加调度成本，不等于业务快照频率 |
| `Tick.TickWorkBudgetMs` | `0` | 高级；Tick 工作超预算阈值，毫秒，>=0；0 使用 Tick 间隔，不是强制中断工作 |
| `Tick.SkipMissedTicksOnOverload` | `true` | 高级；TickScheduler 超预算后重设节拍策略，不提供硬实时保证 |
| `Session.MaxSessions` | `1024` | 常用；SessionManager 会话数上限，>=0；0 拒绝新会话，不是无限；不是已认证在线容量 |
| `Session.ConnectionMode` | `Auto` | 条件；构建校验/握手选择传输族；须与注册传输匹配，多类型 Auto 需要启用 Group |
| `Session.SessionTimeoutMs` | `10000` | 常用；Session 超时回收，毫秒，>=1000；结合客户端保活与调度余量，增大会延后回收死连接 |

### 握手：速率、未完成候选和回执是不同边界

| 路径 | 默认值 | 层级 / 条件、消费者和调整边界 |
| --- | --- | --- |
| `Handshake.HandshakeTimeoutMs` | `15000` | 常用；候选/幂等回执期限，毫秒，1000..60000；重试不刷新，客户端期限独立 |
| `Handshake.UdpTokenExpireMs` | `30000` | 高级/UDP；候选凭据期限上限，毫秒，1000..60000，与握手期限取小值；不是已建会话轮换周期 |
| `Handshake.EnableHandshakeRateLimit` | `true` | 高级；候选接纳同时控制全局和来源速率，不关闭缓存/并发保护 |
| `Handshake.MaxHandshakeRequestsPerSecond` | `80` | 常用；每 Runtime 握手控制帧/秒，启用限流时 1..1000000；含初始/重绑/重试 |
| `Handshake.HandshakeGlobalRateLimitBurst` | `160` | 常用；同一全局桶突发帧数，启用限流时 1..1000000；不等于持续速率 |
| `Handshake.MaxHandshakeRequestsPerEndpointPerSecond` | `80` | 高级；每准确注册+远端 IP/端口的帧/秒，启用限流时 1..1000000，不是每 IP 用户配额 |
| `Handshake.HandshakeRateLimitBurst` | `160` | 高级；每来源突发帧数，启用限流时 1..1000000 |
| `Handshake.MaxHandshakeRateLimitEndpoints` | `4096` | 高级；来源桶数 1..65536；满时拒绝新来源，不挤掉未恢复桶 |
| `Handshake.MaxConcurrentHandshakes` | `256` | 高级；初始/重绑未完成候选数 1..65536，不包含仅保留回执 |
| `Handshake.MaxRetainedHandshakeCandidates` | `1024` | 高级；初始/重绑候选和回执合计记录数 1..65536 |
| `Handshake.MaxRetainedHandshakeBytes` | `1048576` | 高级；候选缓存所持数组字节数 1..67108864，不含所有元数据/RSS |

### 接收、数据报与事件派发

| 路径 | 默认值 | 层级 / 条件、消费者和调整边界 |
| --- | --- | --- |
| `Receive.MaxReceiveQueueSize` | `32768` | 高级；ReceiveQueueSet 每种传输的数据队列事件数，>0；满时拒绝新输入，不是淘汰旧包 |
| `Receive.MaxControlReceiveQueueSize` | `8192` | 高级；直接控制帧独立接收队列事件数，>0；三传输按帧分类，不只 TCP/WS |
| `Receive.MaxReceiveEventsPerTick` | `4096` | 高级；接收模块每 Tick 数据事件总处理数，>0；调大可能增加 Tick 时间 |
| `Receive.MaxControlReceiveEventsPerTick` | `2048` | 高级；每 Tick 控制队列事件处理数，>0 |
| `Receive.MaxPendingControlOperations` | `1024` | 高级；ControlOperation 保留数，含 IO 与待提交完成记录，>0；不是会话数 |
| `Receive.MaxControlCompletionsPerTick` | `2048` | 高级；每 Tick 控制 IO 完成提交数，>0 |
| `Packet.MaxUdpBatchFrames` | `64` | 高级/UDP；合包/解析允许的批次 frame 数，>0；仍受数据报大小约束 |
| `Packet.Mtu` | `1200` | 常用/UDP；完整框架数据报字节数 256..65507，不含 IP/UDP 头；无路径探测/自动分片 |
| `Dispatch.EventDispatchExecutionMode` | `SingleThread` | 高级/原始事件；SingleThread 独立有界 Worker 或 TickInline；构建前确定回调执行域 |
| `Dispatch.MaxEventDispatchQueueSize` | `32768` | 高级/原始事件+SingleThread；Worker 队列条数，>0；TickInline 不创建该队列，仍校验 |
| `Dispatch.MaxPayloadsPerTick` | `4096` | 高级/原始事件；派发 Tick 取出 Payload 数，>0，不保证同 Tick 完成异步 Worker 回调 |
| `Dispatch.MaxPayloadsPerLinkPerTick` | `64` | 高级/原始事件；每 link 每 Tick 交付数，>0，限制单连接独占 |

### UDP 可靠性

| 路径 | 默认值 | 层级 / 条件、消费者和调整边界 |
| --- | --- | --- |
| `Reliable.MaxReliableRetries` | `10` | 高级/UDP可靠；最大重试次数，>0；耗尽终止 Session，不静默跳过缺口 |
| `Reliable.MaxResendBackoffExponent` | `0` | 高级/UDP可靠；退避指数上限 0..10；0 固定间隔，增大会延后失败终态 |
| `Reliable.ResendTimeoutMs` | `200` | 高级/UDP可靠；重传基础期限，毫秒，>=50；结合 RTT/抖动，过小引发额外重传 |
| `Reliable.MaxResendsPerTick` | `1024` | 高级/UDP可靠；每 Tick 重传工作数量，>0 |
| `Reliable.AckOnlyIntervalMs` | `50` | 高级/UDP；ACK-only 最小发送间隔，毫秒，>0；减小增加包率 |
| `Reliable.MaxAckOnlyPacketsPerTick` | `1024` | 高级/UDP；每 Tick ACK-only 工作数，>0 |

### 发送：接纳、调度、物理驻留与完成责任

| 路径 | 默认值 | 层级 / 条件、消费者和调整边界 |
| --- | --- | --- |
| `Send.MaxPendingLogicalSends` | `32768` | 高级；逻辑接纳总条数，>0；准备、输入、Channel 等待及可靠未确认原件共同占用 |
| `Send.MaxPendingLogicalSendBytes` | `67108864` | 高级；逻辑接纳有效字节总量，>0；不是池容量 |
| `Send.MaxPendingLogicalSendsPerSession` | `4096` | 高级；单 Session 跨 Channel 逻辑保留条数，>0 |
| `Send.MaxPendingLogicalSendBytesPerSession` | `4194304` | 高级；单 Session 逻辑有效字节，>0，准备复制也占用 |
| `Send.MaxSendInputsPerTick` | `4096` | 高级；每 Tick 输入移交 Channel 条数，>0 |
| `Send.MaxSendInputBytesPerTick` | `2097152` | 高级；每 Tick 输入有效字节，>0；单条超过也在接纳前拒绝 |
| `Send.MaxPreparedMessagesPerTick` | `4096` | 高级；每 Tick 编码应用消息数，>0，不是数据报数 |
| `Send.MaxPreparedBytesPerTick` | `2097152` | 高级；每 Tick 编码物理字节，>0；完整单帧也须能容纳 |
| `Send.MaxPreparedMessagesPerChannel` | `64` | 高级；每 Channel 每 Tick 编码条数，>0，保护公平性 |
| `Send.MaxPreparedBytesPerChannel` | `65536` | 高级；每 Channel 每 Tick 编码物理字节，>0；单帧超过也拒绝 |
| `Send.MaxSendQueueSize` | `32768` | 高级；物理发送队列槽数，含入队准备，>0；出队释放槽位，不代表 IO 完成 |
| `Send.MaxPendingPhysicalSends` | `32768` | 高级；物理驻留帧数，含准备/队列/Worker/IO/即时回调，>0 |
| `Send.MaxPendingPhysicalSendBytes` | `67108864` | 高级；上述帧有效字节总量，>0，不是实际租用容量/RSS |
| `Send.MaxSendEventsPerTick` | `4096` | 高级；每 Tick 从物理队列取出的事件数，>0 |
| `Send.MaxPendingSendCompletions` | `32768` | 高级；已保留的状态完成责任数量，跨队列/Worker/IO/完成阶段，>0 |
| `Send.MaxSendCompletionsPerTick` | `4096` | 高级；Receive Phase 每 Tick 提交物理发送完成数，>0 |
| `Send.MaxChannelFlushesPerTick` | `8192` | 高级；每 Tick Channel flush 次数，>0 |
| `Send.MaxSendPerSessionPerRound` | `4` | 高级；单 Session 单轮发送包数，>0；避免独占 |
| `Send.EnableTransportSendWorker` | `true` | 高级；发送模块使用 Worker 分片或内联物理 IO；不改变状态提交 owner |
| `Send.TransportSendWorkerCount` | `1` | 高级/Worker；分片执行任务数，>0；不是多线程状态 Tick 或 CPU 核数承诺 |
| `Send.MaxTransportSendWorkerQueueSize` | `65536` | 高级/Worker；合计待调度槽数，>0；满时留原主队列等后续 Tick |

### 传输、监听与周期诊断

| 路径 | 默认值 | 层级 / 条件、消费者和调整边界 |
| --- | --- | --- |
| `Transport.MaxPendingSends` | `4096` | 高级/内置传输；单 Transport 挂起发送数，>0，与上层物理驻留限额不同 |
| `Transport.StopTimeoutMs` | `5000` | 遗留无效；仅复制/校验 >=100ms，不裁决实际停止，勿在新配置中设置 |
| `Transport.ReceiveBufferSize` | `65536` | 高级/TCP+WS；TCP Socket 接收缓存、WS 接收工作缓冲字节数，>=1024；不控制 UDP |
| `Transport.UdpReceiveBufferSize` | `262144` | 高级/UDP；共享 Socket OS 接收缓存请求字节，1024..16777216；启动日志核对实际值 |
| `Transport.SendBufferSize` | `65536` | 高级/UDP+TCP；Socket 发送缓存字节请求，>=1024；WS 不读取此项 |
| `Transport.TcpBacklog` | `1024` | 高级/TCP；监听待 accept 连接 backlog，>0，不是已建立会话上限 |
| `Transport.TcpNoDelay` | `true` | 高级/TCP；禁用 Nagle，优先小包延迟；不影响 UDP/WS |
| `Metrics.ReportIntervalMs` | `0` | 常用；RuntimeMetrics 周期报告间隔，毫秒，>=0；0 关闭周期报告，不是禁用所有统计 |
| `Listen.UdpHost` | `0.0.0.0` | 常用/UseUdp；本机 IP 监听地址，非域名，不是客户端可达地址 |
| `Listen.UdpPort` | `7777` | 常用/UseUdp；实际 UDP 监听端口 1..65535，可被 UseUdp(port) 覆盖 |
| `Listen.AdvertisedUdpHost` | `null` | 遗留无效；仅复制/校验，不写入 wire3 握手，不重定向客户端 |
| `Listen.AdvertisedUdpPort` | `null` | 遗留无效；仅复制/校验有值时 1..65535，不改变监听或客户端端口 |
| `Listen.TcpHost` | `0.0.0.0` | 常用/UseTcp；本机 IP 监听地址，非域名 |
| `Listen.TcpPort` | `7778` | 常用/UseTcp；实际 TCP 监听端口 1..65535 |
| `Listen.WsPort` | `7779` | 常用/UseWebSocket；HttpListener 监听端口 1..65535 |
| `Listen.WsHost` | `+` | 常用/UseWebSocket；HttpListener 主机前缀，非空；+ 通配，不是客户端地址 |
| `Listen.WsPath` | `/ws` | 常用/UseWebSocket；路径前缀，必须以 / 开头；与客户端路径匹配 |
| `Listen.WsUseTls` | `false` | 条件/UseWebSocket；选择 HTTPS/WSS 前缀，不自动安装证书 |

### 可选 Group

| 路径 | 默认值 | 层级 / 条件、消费者和调整边界 |
| --- | --- | --- |
| `Groups.Enabled` | `false` | 可选；构建同 Runtime Group manager；普通多客户端无需开启 |
| `Groups.AllowRemoteCreate` | `false` | 可选/Group；允许远端创建，须先 Enabled；凭据不替代加密部署 |
| `Groups.AllowRemoteIssue` | `false` | 可选/Group；允许 primary 签发关联凭据，须先 Enabled |
| `Groups.MaxGroups` | `1024` | 可选/Group；每 Runtime 组数，1..1048576，且 <=Session.MaxSessions |
| `Groups.MaxMembersPerGroup` | `2` | 可选/Group；每组 Session 数 2..8，不是业务房间 |
| `Groups.MaxPendingCommands` | `128` | 可选/Group；manager 命令保留数 1..65536 |
| `Groups.MaxCommandsPerTick` | `16` | 可选/Group；每 Tick 命令数 1..MaxPendingCommands |
| `Groups.MaxAssociationRecords` | `1024` | 可选/Group；Create 回执/attach 凭据合计数 1..65536 |
| `Groups.MaxMessagesPerDirection` | `4096` | 可选/Group；每组每方向逻辑消息条数 1..1048576 |
| `Groups.MaxPayloadBytesPerDirection` | `4194304` | 可选/Group；每方向逻辑有效字节 1..1073741824 |
| `Groups.MaxRetainedCapacityPerDirection` | `8388608` | 可选/Group；逻辑实际缓冲容量字节，>=有效字节限额且 <=2147483648 |
| `Groups.MaxPhysicalSendsPerGroup` | `4096` | 可选/Group；每组物理驻留数量 1..1048576 |
| `Groups.MaxPhysicalSendBytesPerGroup` | `4194304` | 可选/Group；物理有效字节 1..1073741824 |
| `Groups.MaxPhysicalSendCapacityPerGroup` | `8388608` | 可选/Group；物理实际容量字节，>=物理有效字节限额且 <=2147483648 |
<!-- /server-options -->

主要消费者：[Tick](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/NetCore/Tick/TickScheduler.cs)、[SessionManager](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/NetCore/Session/SessionManager.cs)、[接收组装](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/NetCore/Core/Composition/ServerReceivePathComposer.cs)、[事件组装](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/NetCore/Core/Composition/ServerEventDispatchComposer.cs)、[发送](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/NetCore/Packet)、[物理发送](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/NetCore/Transport/TransportSendQueueModule.cs)、[三传输](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/NetCore/Transport)、[Group 转换](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/NetCore/Core/GroupOptions.cs)。具体范围查[验证器](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/NetCore/Core/NetCoreOptionsValidator.cs)及[Group limits](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/NetCore/Session/ConnectionGroupManager.cs)。没有统一的自动“人数→所有缓存”配置公式。

## 4. 客户端参数索引

路径相对 NetClientOptions。常用是选择与恢复策略，不是把高级预算全部写进 Unity Inspector。客户端与服务端的同名期限、MTU、预算不会自动互改；需分别配置并符合实际协商限额。

<!-- client-options -->
### 连接与握手

| 路径 | 默认值 | 层级 / 条件、消费者和调整边界 |
| --- | --- | --- |
| `ProtocolVersion` | `3` | 固定；构造校验仅接受 wire3，不是选择旧服务器版本 |
| `LogicalChannels` | `null` | 高级；握手协商 ID→模式表，null 使用标准表；自定义 1..8 项，ID 0..31；native 只接受 ReliableOrdered |
| `Connection.Host` | `127.0.0.1` | 常用；目标 IP/域名，非空；不等于服务器监听通配地址 |
| `Connection.TcpPort` | `7778` | 常用/TCP；目标端口 1..65535 |
| `Connection.UdpPort` | `7777` | 常用/UDP；目标端口 1..65535 |
| `Connection.WsPort` | `7779` | 常用/WS；目标端口 1..65535 |
| `Connection.WsPath` | `/ws` | 常用/WS；非空目标路径，与服务端匹配 |
| `Connection.WsUseTls` | `false` | 条件/WS；选择 ws/wss，不代替证书验证和部署验收 |
| `Connection.ConnectionMode` | `UdpOnly` | 常用；单连接仅 UdpOnly/TcpOnly/WebSocketOnly，旧组合值不支持 |
| `Handshake.HandshakeTimeoutMs` | `5000` | 常用；建立操作期限，毫秒，100..60000；不是保活期限或强制 IO 释放期限 |
| `Handshake.UdpHelloRetryIntervalMs` | `300` | 高级/UDP；握手重试间隔毫秒，>0；UDP 时不得超过握手期限 |

### 保活与恢复策略

| 路径 | 默认值 | 层级 / 条件、消费者和调整边界 |
| --- | --- | --- |
| `KeepAlive.EnableKeepAlive` | `true` | 常用；LivenessMonitor 是否启动主动控制面保活 |
| `KeepAlive.KeepAliveInterval` | `5000ms` | 常用；上一轮真正结束后的探测间隔，TimeSpan>=0；还要计入抖动和探测耗时 |
| `KeepAlive.KeepAliveJitterMs` | `1000` | 高级；探测间隔随机抖动毫秒，>=0，避免集中唤醒 |
| `KeepAlive.KeepAliveTimeout` | `5000ms` | 常用；整轮控制探测期限，TimeSpan>0，不是每次重试各有一份 |
| `KeepAlive.UdpMaxAttempts` | `3` | 高级/UDP；一轮总尝试数 1..8，含首次；TCP/WS 始终一次 |
| `KeepAlive.UdpRetryInterval` | `1000ms` | 高级/UDP；探测开始间最小间隔，TimeSpan>0 且 <=Int32.MaxValue 毫秒；总期限优先，不追赶补发 |
| `Reconnect.EnableReconnect` | `true` | 常用；SDK 自动恢复；需要新票据/业务重新入场时由应用接管并关闭本项，MessageClient 要求 false |
| `Reconnect.MaxReconnectAttempts` | `3` | 常用/自动恢复；尝试次数 >=0，不是无限重试承诺 |
| `Reconnect.ReconnectBaseDelayMs` | `300` | 高级/自动恢复；初始退避毫秒，>=0 |
| `Reconnect.ReconnectMaxDelayMs` | `3000` | 高级/自动恢复；退避上限毫秒，>=0 |

### UDP 可靠性、发送与接收工作缓冲

| 路径 | 默认值 | 层级 / 条件、消费者和调整边界 |
| --- | --- | --- |
| `Reliable.AckOnlyIntervalMs` | `25` | 高级/UDP；ACK-only 最小间隔毫秒，>=0；与服务器可不同 |
| `Reliable.ResendTimeoutMs` | `200` | 高级/UDP可靠；重传基础期限毫秒，>=0；按 RTT/抖动调整，不靠减少到 0 提升吞吐 |
| `Reliable.MaxResendBackoffExponent` | `0` | 高级/UDP可靠；退避指数上限 0..10，0 固定间隔 |
| `Reliable.ResendScanIntervalMs` | `25` | 高级/UDP；维护扫描毫秒，UDP 要求 >0，不代表操作系统精确唤醒 |
| `Reliable.MaxRetries` | `10` | 高级/UDP可靠；最大重试次数 >=0，耗尽按投递失败终态处理 |
| `Reliable.MaxPendingPackets` | `1024` | 高级/UDP可靠；每逻辑 ID 保留条数上限 >0，含等待激活；还与内部 1024 条/ID 和 Send 总预算取更严格值，不扩大每 ID 固定 33 位置在途跨度 |
| `Send.UdpSendFlushIntervalMs` | `2` | 高级/UDP；新发送唤醒后的合批等待毫秒，>=0，0 不额外等待；维护扫描不叠加此延迟 |
| `Send.UdpMtu` | `1200` | 常用/UDP；合包目标数据报字节 256..65507，实际还受协商和帧头约束，无自动分片 |
| `Send.MaxUdpQueuedPayloads` | `8192` | 高级/UDP；输入保留条数 >0，覆盖准备/排队/等待窗口/立即发送在途 |
| `Send.MaxUdpQueuedPayloadBytes` | `8388608` | 高级/UDP；上述输入有效字节 >0，不是进程 RSS，也不解除每 Channel 内部边界 |
| `Socket.ReceiveBufferSize` | `8192` | 高级/UDP+WS；接收工作缓冲字节 >0，UDP 至少 1200；WS 按帧上限截取，TCP 不读取此项；不是服务端 UdpReceiveBufferSize 的同义字段 |
<!-- /client-options -->

主要消费者：[选项验证/快照](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/ClientSDK/Api/NetClientOptions.cs)、[连接](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/ClientSDK/Connection)、[控制面](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/ClientSDK/ControlPlane)、[UDP 发送/每 ID 预算](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/ClientSDK/Channel/DatagramSendSet.cs)、[传输](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/ClientSDK/Transport)。默认 Channel 表及有效载荷限制见[API 契约](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/Docs/Architecture/Contracts/Protocol-API-and-Configuration.md#2-channelid不是投递模式)。

## 5. 可选模块：独立预算，不是重复开关

| 入口 | 使用时机、配置 owner 与限制 |
| --- | --- |
| [Hosting / 多 Runtime](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/Docs/Usage/Multi-instance.md) | 注册名称、构建委托和运行期 FailurePolicy 是 Host 组合策略；启动失败仍整体回滚。每个 Runtime 分别配置，不创建进程全局预算 |
| [NetClientHost](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/Docs/Architecture/Contracts/ClientHost.md) | maximumClients 控制宿主可创建的客户端数，网络参数仍分别传给每个客户端；不是让单客户端有多个 Transport |
| [Messaging](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/Messaging/README.md) | RpcSessionOptions 管活动/待响应/准备/发送数量、保留字节与最大期限，NotificationRouterOptions 管订阅数；不是网络窗口。应用信封版本、codec 和业务超时由应用决定 |
| [Messaging.ClientSDK](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/Messaging.ClientSDK/README.md) | channelId 必须是已协商 ReliableOrdered；输入接收策略由调用方提供。网络重连要求关闭，不增加第二套网络参数或隐式业务重试 |
| [Discovery](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/Discovery/README.md) / [响应器](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/Discovery.Hosting/README.md) | 独立 UDP 搜索 Destination、搜索期限/次数/间隔、结果/输入/保留字节预算；响应器管理发现端口、服务类型、公布元数据和限速。这些字段有真实消费者，不等同于 NetCore 的遗留 AdvertisedUdp* |
| [Unity](https://github.com/XMJGame/PulletNet/blob/b504f01de52929df0e808b8761a900c6bd37c782/Docs/Usage/Unity/README.md) | Manager/Connector 是使用入口与策略；主线程队列数量/字节和每帧预算有自己的 owner，不等同网络队列；高级配置按需展开，不要求同步暴露本页所有参数 |

发现的 255.255.255.255 是默认 IPv4 广播目标，不应删除自定义能力：多网卡、定向子网广播或回环验证可能使用不同目标。发现结果是候选，不是鉴权，也不保证服务器已监听。

## 6. 遗留字段与兼容退役

当前三个遗留字段是 Listen.AdvertisedUdpHost、Listen.AdvertisedUdpPort、Transport.StopTimeoutMs。完整生产调用追踪只发现声明、校验和快照复制；测试赋值或能从 Runtime.Options 读取不等于影响运行。**它们没有被删除，也没有新增 Obsolete 警告**，避免无授权的源码/二进制破坏或 WarningsAsErrors 失败。

新接入不设置三项，已有接入先移除误导性的应用开关并验证实际目标来源；客户端可达入口由应用 API/部署或独立 Discovery 提供，真实关闭仍等待 IO、执行器与资源结算。未来删除须独立记录版本/API 迁移并验证消费者，不覆盖现有 3.0.0 包。不为让旧配置“有效”而增加第二套重定向或停机计时机制。

ProtocolVersion 保留兼容 setter，但只接受当前值；不作为应用配置文件/Inspector 中的可调入口。其余高级配置保留，不新增重复 Options 模型、热改层或隐式人数预设。本页不改变默认值、协议、API、安装镜像或平台声明。
