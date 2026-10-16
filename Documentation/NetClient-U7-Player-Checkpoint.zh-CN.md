# U7：独立Windows Player有限收口

2026-10-16，起点d054db26dc3340e4faadee8538c872257ba283f4。只新增外部验证消费者/工具，并修正Editor测试程序集的可选依赖门槛；Manager、Runtime、SDK、Protocol、Hosting及两份锁定DLL均未改。Native输入仍2b046d5 / RC1 / wire3，不新跑Native全量、CI或长压。

## 实际结果

Unity2022.3.62f3，Windows x64 Mono，Development Player，Low裁剪；完整包嵌入全新工程自身Packages，不引用外部包路径。独立示例程序集也参与Player构建，只用公开API，不引入NetCore或friend访问。

- 三传输各自连接/回显、显式断开/重新握手和再次回显，显式Shutdown完成后才卸载owner场景。
- UDP直接卸载的Destroy回退、DontDestroyOnLoad对象跨原场景卸载、被动UDP socket确证握手发出后的销毁收尾。
- 应用退出前显式await Shutdown，Quit hook只记录已清理事实；Unity实际驱动Update/Destroy/场景，不手动调用Hook冒充。

最终7份completed/playing场景收据、客户端10次字节一致回显（主线程交付）、Native服务10次回显、双方exit0；退出hook已观察且cleanupJoinedBeforeQuit=true。构建成功0错误，BuildReport记录**1条警告**，现有文本日志未给出对应可归属的warning详情，不能宣称零警告或擅自判断原因。此为有限接入验收，不是容量/长稳认证，也不覆盖所有逻辑通道或应用退出方式。

## 首次失败及责任修正

原`unity-u7-player-20261016-01`在编译前置失败，Player/服务器未启动：未安装Test Framework的嵌入工程仍编译包内Tests，缺少NUnit/TestTools；另新验证构建收据把int字段写成uint造成两项编译错误。原日志/退出码保留。

测试程序集保留UNITY_INCLUDE_TESTS，并增加依赖`com.unity.test-framework >= 1.1.0`的version define门槛；运行时没有新增测试依赖。第二份全新最小工程未安装Test Framework，Player构建和实际运行通过。相反条件的独立工程安装Test Framework1.1.33并启用testables，**现有124项包测试全部实际执行、零失败/跳过、exit0**，不是删用例/Skip或放宽期限求绿。没有改生命周期实现或竞态，不重复U5同一实现已完成的320次。

## 证据及停止边界

本机原件：`artifacts/unity-u7-player-20261016-01`（首次失败）、`artifacts/unity-u7-player-20261016-02`（90文件输入快照、构建、7场景、Player/服务日志及退出）、`artifacts/unity-u7-editor-20261016`（正向124项XML/日志）。包README/验证README的收口文字随后更新，不冒充它们在上述输入收据中具有相同hash；生产C#/DLL和Player harness按机读记录核对。

使用可重复入口`Tools/Test-PulletNetUnityPlayer.ps1`，新输出目录、禁止覆盖既有证据；服务器必须与NativeInput准确对应。详情与hash见[机器记录](Evidence/netclient-u7-player.json)。

Windows本地Player批次到此停止。Android IL2CPP/PICO、OS真实后台/设备休眠和Release/其他版本组合待协作，不以本结果追溯关闭历史启动AV。仍3.0.0-pre.1开发包；未推送、发布或升级妈祖/XR。后续选择设备验收或单独业务迁移，不自动展开拆包/KCP/新核心重构。

## 后续只读核验：唯一构建警告已归属

同日用户继续授权后，用独立Unity Editor通过序列化接口读取**原始**LastBuild.buildreport，而不是重新构建求零警告。报告确认成功、0错误、唯一一条Warning，发生在Building scene Assets/PlayerAcceptance.unity：无GfxDevice renderer，SkyManager无法更新Ambient Probe/Reflection Probes；它建议不使用-nographics或为场景生成光照。此为无图形验证环境的场景光照/探针警告，不是PulletNet收发、线程或资源释放异常；也不代表真实业务场景的光照已验证。网络回显验证不需要这些探针，本批不为消除报告数字改动验证场景或网络代码。

原报告SHA256读取前后同为7ff52a2602096409ccc1d2c70a5cb9065fc29cf389feea4659f4ab915d273a44，读取进程exit0。**仍是1条已解释警告，不回写为0条**；上文和原U7机器记录保留当时“详情未归属”的事实。最新[核验收据](Evidence/netclient-u7-warning-and-device-readiness.json)，原件artifacts/unity-u7-warning-20261016。

Android前置只核查安装文件：2022.3.62f3的Android/IL2CPP、NDK23.1、JDK11（java命令成功）、Gradle7.5.1和SDK文件存在；adb命令成功但设备列表为空。未运行Android Gradle/IL2CPP构建、安装或设备测试，不能把文件存在说成可用平台通过。下一阶段需选择Android手机/PICO并连接、允许USB调试，或另行明确妈祖迁移范围；本轮未安装软件、改系统设置或升级业务。
