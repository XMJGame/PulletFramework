# NetClient U2 单连接适配检查点

2026-10-16。用户已授权 Unity 实现，范围仅 U2；不重开 Native 审计或根模型。源码分支 `codex/unity-single-connection`，起点 `0712e99559cecea06876ab1ceccce119bf03c4dc`。此前 U1 的旧 CI 阻挡是当时事实，以下为当前检查点；不重复 U1，也不将本记录升级为另一套路线图。

## 交付与输入

- Unity 包为 `3.0.0-pre.1` 开发预览，非正式发布；Native 默认版本不变，没有重新打包/升 RC2。`NativeInput.json` 锁定实际消费的 `fa0145c4e86b1e9c3d4ca509711b5bbbcc1f105d`、CI38870857874/attempt1、RC1 nupkg/DLL SHA256。该 CI 仅 Windows；不是最新同源双平台验收。已有输入作为隔离开发基线使用，不豁免最终候选门槛。
- 从原始已核验 nupkg 提取 Protocol netstandard2.0 / ClientSDK netstandard2.1 两份 DLL。先在新建的隔离 Unity 工程验证，随后才更新本分支包内 DLL；现有 DLL/script/prefab `.meta` GUID 保留。NetCore/Hosting 不进入 Unity 包。
- 默认 `UdpOnly`；只允许 1/4/5 三种单连接模式，仅校验所用传输端口。序列化旧组合值 2/3 明确拒绝并提示迁移，未知值同样拒绝，不改编号、不静默重解释。
- 保留旧 `SendAsync(byte[], ChannelType, ...)` 门面，委托 SDK 的 `SendDefaultAsync`；增加明确命名的 `SendOnChannelAsync(byte[], byte, ...)`。成功仍只代表本地接纳，不表示物理完成、远端 ACK 或业务处理。未协商 ID 及 TCP/WS 不支持的语义由 SDK 明确拒绝。
- 主线程队列移交时保留实际 ChannelId、TransportType、ChannelType。元数据测试还验证借用输入改变后，队列中的 owned 副本保持独立。

## 验证与原始失败

隔离路径：`G:/XuMingJun/2026/PICO GPS XR/PulletNet/artifacts/unity-u2-20261016`。Unity2022.3.62f3、Windows Editor / EditMode；不是 Windows Player、PlayMode 场景生命周期、Android 或 IL2CPP/PICO。

最终 `final/editmode.xml`：**64/64 通过、0 失败、0 跳过，Unity exit=0**。其中 61 项现有及新增包测试，3 项为独立真实 UDP/TCP/WebSocket 回显。三种传输分别验证连接、默认模式发送、显式 ID、主线程元数据、未协商 ID 拒绝、断开后新 Session、再次回显及断开后的发送拒绝；TCP/WS 另验证不可靠模式拒绝。最终 fixture 共 9 条回显，三 Runtime Stop/Dispose 后 exit=0，`server-result.json` 的失败集合均为空。

回显 fixture 与测试位于 `Validation~/`，不会被 UPM 自动导入，不向正式运行时增加测试服务器依赖。最终运行的源码/DLL与本分支对应文件逐项核对；输入锁、结果及文件 hash 见 `Evidence/netclient-u2-local.json`。

过程中保留以下非成功结果，不以最终绿灯覆盖：

1. 最初 EditMode 停滞，无最终 XML；只终止了准确核对 PID/命令行的隔离 Unity 测试进程。旧 RPC 测试同步等待需要主线程完成的异步操作，存在自等待路径；未采集线程栈，不宣称已确证所有停滞因果，也不将其归因于 Native Worker 或硬件。
2. 初改为 `[Test] async Task` 后，Unity Test Framework1.1.33报告 51通过/10失败，均为不支持的测试签名。包括原有7项异步测试；统一改为 `[UnityTest] IEnumerator`，用测试侧桥接在真实 Unity 主线程 yield 等待原 Task。保留原操作期限与断言，不改 RPC 生产实现。
3. 协程输入得到60通过/1失败：恢复测试将“准备尝试”的通知当作“已执行连接”。同步观察点移到连接委托设置目标之后，不增加等待时间。
4. 修正上述观察点时一次多写括号导致编译失败，原日志保留；随后仅修正语法。
5. 首次64项全绿后，为最终开发包版本、锁文件及 TCP/WS 拒绝语义补做一次小规模包装层检查点，仍64项全绿；没有重跑 Native2987项、CI、长稳或20轮重复仪式。未增加新的生产生命周期竞态，不将非竞态配置检查机械重复20次。

Unity 日志中的启动许可握手警告、缺失可选 VisionOS native 模块及无图形光照提示原样保留；最终脚本编译和 XML/退出码成功，不将这些提示当作网络框架修复。未修改许可、系统或 SDK。

## 不在本批的保证与下一步

妈祖、XR、Native 内旧 Unity 镜像、业务服务器依赖未切换；没有推送/发布。正式业务服务器仍须独立迁移到 wire3，不能拿2.x服务器验证此包。

多连接、NetClientHost adapter、有界字节预算/准备中计费、来源代际、公平性、可靠溢出终态、完整 Shutdown/Pause/Resume/Destroy 属于 U3—U6，**仍未完成**。本批保留现有单连接 owner，不新增第二个状态机；当前动作队列与可靠溢出处理不因单连接测试通过就变为完整资源保护。RPC/Messaging/Discovery 仅保持现有功能，不拆包、不改协议。保留重绑入口暂不向 Unity 暴露。

下一有限批次应按 U1 合同联合设计薄多客户端 owner 与主线程队列移交，不堆叠第二套后台队列；先锁定来源句柄/派发代际、数量与字节预算、所有权及停止条件再实现。本批到 U2 结束，不自动进入下一阶段或扩大平台支持声明。
