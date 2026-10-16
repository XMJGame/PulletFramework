# 连接示例

1. 在同一个 GameObject 上添加 `PulletNetworkManager` 和 `PulletNetConnectionSample`。
2. 直接在 `PulletNetworkManager` Inspector 中填写端口和自动发现策略。
3. 在示例组件中指定 Manager 引用。
4. 先启动兼容的 PulletNet 服务端，再通过组件的右键菜单连接、发送一条 UTF-8 消息或断开连接。

本示例随包导入的独立 `PulletFramework.NetClient.Samples.asmdef` 显式引用包装层和两份SDK DLL；不依赖测试程序集、InternalsVisibleTo或NetCore DLL。代码只使用公开API。Inspector引用应在组件启用前配置；修改引用后先禁用再启用，以重新订阅。

代码调用可等待 `ConnectAsync`、`SendAsync`、`DisconnectAsync`、`ShutdownAsync`；Inspector的ContextMenu命令只负责触发并报告结果，异常在UI边界观察。

`Disconnect`允许以后重新连接，`Shutdown (Permanent)`不可逆。切换/退出场景前需要真实退出收据时，由场景流程显式 `await sample.ShutdownAsync()`，随后再销毁Manager或卸载场景。不能同步Wait/Result堵住Unity主线程，也不能把OnDestroy/OnApplicationQuit返回当作清理完成。

示例和可选Relay只订阅事件，不拥有网络。OnDisable只取消自己的订阅，不擅自断开别人的Manager。`dontDestroyOnLoad=true`的Manager跨场景保留，仍需明确的应用退出owner；不要为了多连接创建多个单例Manager，多连接使用独立 `PulletClientRuntime`。

Unity 2022.3.62f3 Windows Editor已在独立PlayMode工程验证示例公开Task API、Relay启停、场景卸载和组合通知，证据在开发仓库Docs/Unity。该结果不是Android/IL2CPP/PICO、操作系统切后台或实际进程退出的验收。

此示例只演示客户端接入流程。业务消息格式和序列化由具体项目负责，不应放进通用网络包。
