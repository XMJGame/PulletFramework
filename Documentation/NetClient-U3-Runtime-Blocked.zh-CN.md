# U3/U4-B：薄 Runtime 接入，UDP 退出门槛未通过

2026-10-16。接续 `120e37b`，同一 `codex/unity-single-connection` 分支。
本批不是 U3/U4 验收成功，也不撤销 U2/U3-A 在各自实际场景中的结果。

## 已推进的内容

工作区新增 `PulletClientRuntime`：SDK NetClientHost 仍是客户端最终释放 owner；Unity 只管理准确 HostedClient 来源、主线程交付代际、派发预算和自身退出。未接入旧 Manager，不串联两套队列，不加入业务 MessageId、RPC 或发现策略。

生命周期按来源有界，数量包括当前消费中的记录；可靠数据或生命周期溢出使用独立固定故障槽，立即关闭本来源交付并请求受管移除，其他来源继续。全局 Close 冻结新操作，加入已接纳移除、准备/排队/消费 Payload 和已进入的 Unity 回调。终态通知可能晚于 SDK 移除结束，所以全局关闭还须加入当前通知回调，不能复用之前已完成的空闲信号。用户不得在回调中同步等待自身退出。

新契约和实现目前保留为**未提交的实现工作区**，不把公共入口作为已验收产物交付；下一步先处理下述真实退出阻挡，再完成关键竞态独立重复验证和集成门槛。

## 原始结果，不以复跑覆盖

原件：`G:/XuMingJun/2026/PICO GPS XR/PulletNet/artifacts/unity-u3-runtime-20261016`。
命令、退出码、XML/日志 hash 和工作区源码 hash 见 `Evidence/netclient-u3-runtime-blocked-local.json`。

| 运行 | 结果 | 含义 |
| --- | --- | --- |
| targeted | 18/18，exit 0 | 最初适配层定向用例，尚未加入后续4项 |
| checkpoint | 无最终XML，人工终止隔离Unity，exit -1 | 真实双连接中的某次等待超时；嵌套IEnumerator失败后外层仍读取未完成Task.Result，主线程同步阻塞。原输入副本、日志及线程快照保留，不视为完整测试执行 |
| real-recheck-tests | 1/2通过，exit 2 | 测试改为单一异步body，消除超时后同步Result读取；正常移除/名称复用场景仍超时，可靠溢出自动清理场景通过 |
| sdk-control | 2/3通过，exit 2 | 绕过新Runtime/队列，直接使用SDK公开API；RemoveConnectedUdp、RemoveUdpAfterEcho通过，DisconnectUdpAfterEcho及其teardown未完成 |
| unit-checkpoint | 107/107，0跳过，exit 0 | 61项原包 + 24项派发内核 + 22项Runtime测试；不含Validation~真实连接测试，不代表集成通过 |
| sdk-disconnect-independent | 0/1通过，exit 2 | 新Unity进程只执行SDK DisconnectUdpAfterEcho，再次停在disconnect及host teardown；连接、发送和echo均已完成 |

没有为此放宽10秒观察期限、修改SDK握手/关闭期限、忽略失败或重跑取最后绿灯。两个fixture均通过真实服务端Stop/Dispose退出，failures/sendFailures为空；分别回显2次和8次。这是服务端资源退出证据，不是客户端清理成功。

## 已有证据与尚未证明的部分

首次挂起进程使用 Mono debugger 只读采集后恢复/脱离：

- 主线程在新验证测试 TearDown 的 `Task.Result` 中同步等待。该测试失败退出缺陷已改为 `UnityTaskTest.Run(async ...)`，后续失败正常产出 XML 并退出2，不再通过读取未完成Result挂住测试主线程。
- 后台线程在 `NetClientHost.CloseEntryAsync → NetClient.IDisposable.Dispose → ClientConnection.Dispose` 等待。
- 该连接的 `_receiveLoops._active=null`、`_retired` 含一代；其中 UDP ReceiveRun 的任务未完成，SendObserver 已完成，操作 gate 被占用，`_failureWork=null`。快照未显示它在等待新的恢复任务。
- 新Runtime以外的SDK公开API控制用例在两个独立Unity进程中也未完成UDP断开。因此，**不能只在新Unity管理层做补偿后宣布解决**。

仍未确定：旧UDP接收为何未退出、物理Socket完成/Mono事件参数/退出时序的准确阻挡关系，及相同条件在.NET运行时或Player上的结果。不能从当前证据直接宣布硬件问题、Mono缺陷、所有UDP断开必败，或所有平台Native都有同一缺陷。直接SDK Remove的两个通过也必须保留，不能写成所有Host移除均失败。

只读debugger第一次在PowerShell Core中受旧BeginInvoke限制，第二次Windows PowerShell捕获线程时缺Mono.Cecil；加载准确程序集后成功保存。此为采集工具问题，不记作PulletNet根因。终止前验证PID62328及隔离project命令行，保留记录；未终止用户业务Editor或服务。

## 范围和下一停止点

Native代码、wire、两份DLL、旧Manager、Prefab/GUID和妈祖/XR正式依赖均未改；未推送、发布或运行Native全量/CI/长压。DLL仍由NativeInput锁定到 `fa0145c4e86b1e9c3d4ca509711b5bbbcc1f105d` 的RC1；不存在更新DLL后沿用旧收据的问题。

本批在真实退出门槛处停止，新增竞态20轮尚未执行，不能以107项通过代替。下一有限前置任务是定位SDK UDP旧接收与物理IO的真实退出关系，建立可控反例后修 owning responsibility；若确需Native修改，先确认这一范围例外。不自动重新审计整个Native，不在Unity加超时成功、跳过接收等待、强行清零预算或调用内部Dispose绕过SDK Host。

此阻挡关闭后，继续原U3/U4计划：真实双源独立移除/同名替代和可靠过载隔离、新关键竞态独立20轮、包检查点；之后才是U5/U6旧门面及生命周期迁移，最后U7 Player/IL2CPP/PICO。没有新增第三套架构路线。

## 后续有限SDK修复进展（原失败不回写）

用户再次授权继续后，已在Native UDP传输适配器定位并修复实际根因：本机Unity2022.3.62f3 Mono的Memory+CancellationToken Socket扩展在完成回调的Task结算try/catch之外抛取消异常，使原Task无法完成。纯Socket反例及准确本机System.dll IL已保留。SDK改为显式单物理read/缓冲owner、取消逻辑等待但复用原read，Close中断Socket并等待真实终态；不改Unity管理层补偿、不改wire。完整说明为`G:/XuMingJun/2026/PICO GPS XR/PulletNet/Docs/Reviews/2026-10-16-Unity-Udp-Receive-Exit-Owner.md`。

新DLL仅在另一隔离目录`artifacts/udp-exit-owner-20261016/unity/Package`使用，正式Framework NativeInput/DLL仍为原RC1输入。最终SDK618/618、API/架构守卫10/10通过，新UDP owner12场景×20独立进程240/240通过；修复版真实Unity11/11及含其的包检查点118/118通过。首次测试服务已不运行造成9项连接失败，保留记录；之后验证脚本显式管理独立fixture启动/Ready/真实退出。

Unity关键8场景独立20轮在第11进程遇到Editor初始化访问冲突`0xc0000005`，尚未执行测试、无XML、fixture回显0；前10轮80/80通过。按规则停止，未重跑覆盖，12—20未执行；本轮没有完整通过Unity重复门槛或签收U3/U4，适配实现继续留工作区。下一步先确认启动异常对补验方式的处理，再做候选输入锁/消费者更新；不得由本段推导正式包已修复、业务已升级或Unity全平台可靠。

随后用户授权有限补验：原1—11完整保留，新增12—21全部通过；累计21次启动尝试、20次实际测试执行160/160通过，第11次测试前AV未结案。Native准确dac3c64隔离导出生成本地RC2，四包/元数据/API及15步公开消费者通过；准确包提取DLL的隔离Unity118/118、服务端20echo及真实退出通过。新版隔离NativeInput的CI字段为null，不继承旧CI；本目录正式DLL/NativeInput仍未切换。详见Native的`Docs/Reviews/2026-10-16-UDP-Exit-Candidate-Local.md`及同名Evidence收据。这只解除SDK退出专项的本地阻挡，薄Runtime自身新增竞态/开发输入门槛和U3/U4签收仍待完成。

其后Runtime本地功能检查点已完成25项定向、12场景20独立进程240/240及包121/121；当前状态以[Runtime本地检查点](NetClient-U3-Runtime-Local-Checkpoint.zh-CN.md)为准。本页保留原失败，正式输入切换仍待新SDK远端门槛，不回写为当时已通过。
