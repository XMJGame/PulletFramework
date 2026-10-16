# 单连接隔离回显验证

## U7独立Windows Player

在开发仓库执行 `Tools/Test-PulletNetUnityPlayer.ps1 -OutputDirectory <新的证据目录> -ServerDll <准确NativeInput对应的EchoServer.dll>`。使用已安装Windows Standalone支持的Unity 2022.3.62f3，脚本只读导出完整包，创建全新隔离工程，将包嵌入自身Packages并将Player/和ConnectionSample复制到Assets，构建Windows x64 Mono Development Player（Low裁剪）。不打开或安装到业务工程，不重新构建Native DLL。

脚本保存输入hash、构建/Player原始日志和退出码；构建完成才启动受控服务器。7份场景完成收据、Player结果、10次服务回显、显式Shutdown后Quit hook以及双方exit0必须同时成立。Player进程120秒看门狗到期属于失败，终止测试进程不等于框架成功清理；不重试求绿。日常不是长压或容量测试。

Player/脚本是只用公开API的外部验证消费者，不是正式组件、内部生命周期协调层或业务示例。场景包括三传输断开/重连和显式关闭后卸载、UDP直接卸载的Destroy回退、持久对象跨场景、真实握手中销毁及退出前显式await Shutdown。应用退出hook只记录已完成清理事实，不在hook里执行异步清理。未覆盖OS真实后台、强杀、设备休眠或Android IL2CPP/PICO。

嵌入包在没有Test Framework的最小工程必须能编译；安装Test Framework并启用包测试时也必须实际执行现有测试，两侧不能通过删测试或偷偷添加生产测试依赖规避。原首轮失败和最终证据见开发仓库Docs/Unity的U7记录。

## U6实际PlayMode应用验收

`UnityApplicationPlayModeTests.cs`同样只在隔离项目中复制到包的Tests/Editor；将完整ConnectionSample（含其asmdef/meta）复制到隔离项目Assets，使其作为非friend的外部运行时程序集编译。运行Editor测试中的本类，每个测试自己通过EnterPlayMode进入实际运行态，Unity负责Awake/Update/Destroy和场景卸载。不得手动调用Manager.Update冒充实际帧。

启动准确NativeInput的raw echo fixture，设置PULLETNET_U2_PORTS；另设置PULLETNET_U6_RECEIPTS为新的已有空目录。最终必须同时满足8项Runner通过、8份实际场景完成收据（playing=true）、服务12次回显及Stop/Dispose exit0，缺一不签收。只看NUnit绿色可能掩盖domain reload/测试驱动未等待任务的问题。原首次8绿但只有1次回显已保留为不可签收结果；测试驱动改为顶层显式Task join后完成三方门槛，不据此追溯否定无PlayMode转换的既有U5证据。

覆盖：Relay实际enable/disable/destroy与主线程交付，显式关闭后卸载场景、卸载触发真实OnDestroy、DontDestroyOnLoad跨场景、重复Manager隔离、真实握手中销毁、导入示例公开Task API。Pause/Focus通过PlayMode内Unity SendMessage注入，证明组合策略与真实帧/IO，不等于OS真实后台或头显休眠测试。全部正常退出后再停止服务fixture；不自动操作业务工程。

## 既有单连接检查点

本目录不会被 UPM 自动导入。`SingleConnectionEchoTests.cs` 仅在新的验证工程中复制到本包的 `Tests/Editor/`，与现有 Editor 测试使用同一程序集；不向正式包添加 NetCore DLL。

输入必须记录 Native 源码、原始 CI、nupkg/DLL SHA256、包装层源码、Unity 版本和 `.meta` GUID。启动同一 Native 输入的独立本机 raw echo server，生成 `{"udp":端口,"tcp":端口,"ws":端口}`（WebSocket 路径 `/ws`），以 `PULLETNET_U2_PORTS` 向 Unity 子进程传入该文件的绝对路径。不要连接真实业务服务器或共用消费者的正式依赖。

在隔离副本中导入准确的 Protocol/ClientSDK DLL，并在项目 manifest 的 `testables` 中加入 `com.xmjgame.pullet-framework.netclient`，通过 Unity Test Runner 执行 `SingleConnectionEchoTests`。三项分别验证正常连接、模式发送、ID 发送、主线程 Payload 元数据、不允许未协商 ID 降级、断开、新 Session 及再次回显。10 秒仅为测试观察看门狗，不改 SDK 自身握手期限；失败时释放实际 SDK owner。

测试不覆盖多连接、交付队列字节预算、所有生命周期竞态或 IL2CPP/PICO。服务器必须在最后真实 Stop/Dispose 并检查失败与回显计数，不能以 Unity 测试退出代替服务端收尾。原始 XML、日志、进程退出码及输入锁应保存在新的证据目录，不覆盖旧结果。

`Server/` 提供本次实际使用的独立 .NET8 raw echo fixture。将准确候选的 NetCore/Protocol DLL 提取到其新建验证副本中（不提交 DLL 到此目录），执行 `dotnet run --project <副本>/EchoServer.csproj -- <新证据目录>`；程序写出 `ports.json`。测试结束后在该证据目录创建 `stop-server` 文件，等待进程退出，核对 `server-result.json` 的 `failures`/`sendFailures` 为空及 `echoes=9`。八分钟只是 fixture 看门狗，不是长稳测试；正常三种传输每种三条回显。
