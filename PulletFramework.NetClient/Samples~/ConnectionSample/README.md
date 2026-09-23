# 连接示例

1. 在同一个 GameObject 上添加 `PulletNetworkManager` 和 `PulletNetConnectionSample`。
2. 直接在 `PulletNetworkManager` Inspector 中填写端口和自动发现策略。
3. 在示例组件中指定 Manager 引用。
4. 先启动兼容的 PulletNet 服务端，再通过组件的右键菜单连接、发送一条 UTF-8 消息或断开连接。

此示例只演示客户端接入流程。业务消息格式和序列化由具体项目负责，不应放进通用网络包。
