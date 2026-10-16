# U3/U4-B Runtime本地检查点：实现验证完成，输入切换待远端门槛

2026-10-16，接续SDK退出专项及RC2隔离候选，不是新一轮Native审计。当前工作区实现 `PulletClientRuntime` + 其公开合同 + 派发内核接入；未迁旧Manager，不切妈祖/XR，不推送/新CI/长压，不拆Messaging/Discovery。使用规则见[当前Runtime合同](NetClient-U3-Runtime-Contract.zh-CN.md)。

## 本批责任收口

- 准确HostedClient来源、有界生命周期/诊断记录、固定故障槽与已有有界Payload owner接在一起。SDK仍拥有实际连接/恢复/最终Dispose；Runtime只拥有交付与自己的关闭接纳。
- 可靠溢出/复制失败/生命周期容量耗尽只收口本来源；状态失效在SDK原回调中执行。旧同名句柄、旧派发代际不能混入新来源；没有Handshaking的保留重绑保持交付代际。
- SDK移除、准备中/消费中Payload、已进入Unity回调均加入退出。审阅发现终态通知可在旧空闲信号后进入，移除完成发布改为在同一gate中检查当前Callbacks==0；不是增加另一个生命周期协调器，也不声称已复现旧窗口的失败。
- Close冻结新操作，加入已接纳移除和当前终态回调；等待者取消不取消owner清理。每个清理owner都被尝试，结果区分交付不完整和清理错误。不依赖Update完成网络清理，不同步等待自己的回调，不强制超时成功。

## 证据

新隔离目录：`G:/XuMingJun/2026/PICO GPS XR/PulletNet/artifacts/unity-u3-runtime-closeout-20261016`。输入为dac3c64 RC2包中实际提取的Protocol/ClientSDK DLL，不是正式目录旧RC1。SDK自身12场景20轮与真实SDK退出8场景累计20次执行沿用各自原件，本批不再重复。

本批定向25/25通过（先前22项+3项新增），新增为准备复制中移除/拒绝迟到发布、可靠复制失败保留首因并隔离来源、并发创建与关闭逐owner结算。使用真实MemoryManager/可控屏障，不为注入增加生产队列/定时器；复制/回调借用预算直到实际释放都计费。

12个关键场景各20次独立Unity进程，总计**240/240、0跳过**，全部进程exit0：可靠溢出、生命周期溢出、同名迟到隔离、Payload回调Close、生命周期回调Remove、单等待者取消、Close加入已接纳Remove、无后续帧聚合终态、故障槽回收、SDK移除后终态回调Close、准备中Remove、并发Create/Close。纯字段/配置和同步错误结果没有重复20遍。

随后一次包检查点**121/121、0跳过、exit0**：61项旧包测试+24项派发内核+25项Runtime+11项真实连接测试。真实11项包含当前Runtime双源正常移除/同名替代和可靠过载隔离，另复用SDK退出/重绑、握手取消/期限及旧Manager三传输路径。服务使用准确RC2 NetCore/Protocol，20次回显、发送/清理失败为空，真实Stop/Dispose exit0。

每轮XML、日志、命令和退出码均保留，源码与隔离副本hash相同；无本批测试失败或启动崩溃。**此前另一批第11次Editor启动AV仍未结案**，本批20次正常启动不能反推它已解决。精确收据见[Runtime本地证据](Evidence/netclient-u3-runtime-closeout-local.json)。

## 为什么暂不作为可用包提交

正式Framework NativeInput和两个DLL仍为fa0145c的RC1；其中尚无本次UDP退出修复。新RC2只有本地四包/15步消费者及隔离Unity证据，dac3c64还没有对应远端CI。按既定输入门槛，不把旧CI认证挂到新代码、不在正式包混入未经该门槛的DLL，也不提交引用缺失Runtime类型的测试半成品。

因此本批只提交文档和证据；Runtime/Contracts/测试和ClientDispatchQueue接入保留工作区，明确是**本地功能检查点，不是已安装U3/U4产物**。下一步需要单独授权推送Native本地main的UDP修复及证据文档，执行一次Windows专项CI；通过后更新Framework开发输入锁/DLL并提交完整Runtime职责。若远端失败按准确输入保留和定位，不无限重跑。

之后才是U5/U6旧门面与应用生命周期迁移；U7 Player/IL2CPP/PICO继续按平台独立验收。妈祖/XR正式升级、发布及全平台稳定声明仍不在自动执行范围内。
