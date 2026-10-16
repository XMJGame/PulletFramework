# 单连接隔离回显验证

本目录不会被 UPM 自动导入。`SingleConnectionEchoTests.cs` 仅在新的验证工程中复制到本包的 `Tests/Editor/`，与现有 Editor 测试使用同一程序集；不向正式包添加 NetCore DLL。

输入必须记录 Native 源码、原始 CI、nupkg/DLL SHA256、包装层源码、Unity 版本和 `.meta` GUID。启动同一 Native 输入的独立本机 raw echo server，生成 `{"udp":端口,"tcp":端口,"ws":端口}`（WebSocket 路径 `/ws`），以 `PULLETNET_U2_PORTS` 向 Unity 子进程传入该文件的绝对路径。不要连接真实业务服务器或共用消费者的正式依赖。

在隔离副本中导入准确的 Protocol/ClientSDK DLL，并在项目 manifest 的 `testables` 中加入 `com.xmjgame.pullet-framework.netclient`，通过 Unity Test Runner 执行 `SingleConnectionEchoTests`。三项分别验证正常连接、模式发送、ID 发送、主线程 Payload 元数据、不允许未协商 ID 降级、断开、新 Session 及再次回显。10 秒仅为测试观察看门狗，不改 SDK 自身握手期限；失败时释放实际 SDK owner。

测试不覆盖多连接、交付队列字节预算、所有生命周期竞态或 IL2CPP/PICO。服务器必须在最后真实 Stop/Dispose 并检查失败与回显计数，不能以 Unity 测试退出代替服务端收尾。原始 XML、日志、进程退出码及输入锁应保存在新的证据目录，不覆盖旧结果。

`Server/` 提供本次实际使用的独立 .NET8 raw echo fixture。将准确候选的 NetCore/Protocol DLL 提取到其新建验证副本中（不提交 DLL 到此目录），执行 `dotnet run --project <副本>/EchoServer.csproj -- <新证据目录>`；程序写出 `ports.json`。测试结束后在该证据目录创建 `stop-server` 文件，等待进程退出，核对 `server-result.json` 的 `failures`/`sendFailures` 为空及 `echoes=9`。八分钟只是 fixture 看门狗，不是长稳测试；正常三种传输每种三条回显。
