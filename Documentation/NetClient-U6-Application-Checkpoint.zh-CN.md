# U6：实际PlayMode应用接入与只读导出

2026-10-16。Native基线c0d8fe8，沿用相同Manager/Runtime/RC1 DLL，不重开Native审计、全量或长压。本批只完善可导入示例和交付工具、增加应用验收；Manager、Runtime、Protocol、ClientSDK和Hosting实现均未改。

## 公开接入

ConnectionSample提供可等待的Connect/Send/Disconnect/Shutdown方法，Inspector ContextMenu只触发并观察异常。禁用示例只解除自己持有的准确Manager订阅，不擅自关闭网络。新Samples asmdef显式引用公开包装层/SDK库，导入隔离工程Assets后作为独立运行时程序集编译，不借测试程序集或friend内部实现。

实际PlayMode测试由Unity执行Awake、Start、Update、OnEnable/Disable、OnDestroy与场景卸载，不手动调用Manager.Update。最终八个场景：Relay启停/销毁及主线程事件；显式关闭后卸载；卸载触发真实Destroy并加入发现/连接退出；DontDestroyOnLoad保持连接；重复Manager隔离；组合Pause/Focus通知；UDP握手中销毁；外部示例公开Task API。

Pause/Focus在PlayMode通过Unity消息注入，是组合策略+真实帧/IO验证，不是OS真实切后台或PICO休眠。退出PlayMode前仍显式加入本轮清理；不把Unity退出Hook返回当作异步清理完成。

## 原始异常与最终门槛

首轮playmode-checkpoint-01的Runner报告8/8、exit0，但服务只有1次回显，与预期12次不一致，不能签收。该运行的原日志/XML、fixture记录和original-harness源码保留。原fixture在UnitySetUp/TearDown跨PlayMode/域重载和嵌套异步枚举器之间依赖了不可靠的驱动结构；未将Unity内部精确机制宣布为已确定，也未据此重写网络产品。

改为顶层枚举器内进入PlayMode并显式等待Task和清理，每场景在实际playing=true时写完成收据。新增验证门槛必须同时核对Runner8项、8份场景完成收据、Native fixture12次回显与真实Stop/Dispose。

最终playmode-checkpoint-02：**8/8、零跳过、exit0；8份completed/playing收据；12次Native回显，failures/sendFailures为空，服务exit0**。原错误“绿色”运行不重写为成功，不仅重跑同一验证求绿。准确编译的测试源码及Assets示例源码hash与当前canonical文件一致。

本批不重复未改动的Manager/Runtime竞态；U5的16项20进程320次保留为同一实现的既有证据，不称实际Unity场景也重复20次。未运行旧135项或3004项全量，也未新建远端CI。

## 安装/导出保护

新增Export-PulletNetUnityPackage，只读校验NativeInput及两个DLL、逐文件快照后导出到新目录，既有输出/错误输入/源内输出拒绝。旧Build脚本存在NativeInput锁时在构建前拒绝，防止原地重编译覆盖准确DLL。仅导出，不安装或认证发布；[安装、回退及Player清单](NetClient-Installation-and-Player-Checklist.zh-CN.md)明确剩余范围。

5项导出guard在PowerShell7.6.5和Windows PowerShell5.1各自进程通过，原生进程exit0：正确快照、保留已存在输出、拒绝源内输出、错误DLL锁不创建输出、旧构建入口不覆盖锁。另一次外层驱动错误使用未由Native进程产生的LASTEXITCODE，造成命令exit1；内部5项已通过，记录保留，不归为工具/产品缺陷。最终用独立原生PowerShell进程读取准确退出码，没有修改guard去求绿。

机读记录见 [Evidence/netclient-u6-application.json](Evidence/netclient-u6-application.json)，原件在本机PulletNet-Unity/artifacts/unity-u6-application-20261016。当前仍pre.1，未推送/发布/升级妈祖或XR。Windows原生Player、Android IL2CPP/PICO及OS实际后台/进程退出尚待验证，不能由本地Editor签收。
