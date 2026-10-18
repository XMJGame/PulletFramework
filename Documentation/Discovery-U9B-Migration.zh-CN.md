# U9-B：可选发现组合与旧资产迁移

本批开发源仅UnityPackages，不修改Framework/妈祖。原生wire3、Discovery v1、DLL均不改。

## 三个职责，而非三份发现实现

- NetClient：原始连接/收发/恢复/Shutdown，独占默认客户端；不引用任何Discovery程序集。
- Discovery：Native发现IO/严格校验/预算的有界主线程适配，可单独安装，不引用NetClient/UnityEngine。
- NetClient.Discovery：可选选择策略/Inspector/事件桥接，依赖前两者；无Socket/JSON解析或Payload队列。

组合包ID是com.xmjgame.pullet-framework.netclient-discovery，程序集/namespace是
PulletFramework.NetClient.Discovery。不是在核心增加反向依赖或有条件双跑旧循环。

## API、线程和退出

PulletDiscoveryConnector提供DiscoverAsync、AutoConnectAsync、StopAsync、CloseAsync。
DiscoverAsync返回DiscoverySnapshot，取消明确抛出，不再伪装成功的空列表。
Round完成与通知交付分开；Update消费终态前不能开始下一轮。
AutoConnectMode保留Direct=0、DiscoverFirst=1、DirectThenDiscover=2。
发现选择沿用按名称/实例ID的稳定顺序；发现广告缺少所选传输端口直接失败，不再偷偷回退Inspector端口。

创建/配置/Discover/AutoConnect/Update属于Unity主线程。
选择流程记录Manager.ConnectionRevision（本地应用命令版本，不是wire身份）；
新的手动连接/断开使旧策略取消，不让旧发现结果抢回连接。
连接及其意图仍只由Manager控制；组合不调用借用NetClient的Dispose。
RetireDisconnectedClientAsync是Manager的显式来源回收入口：原操作gate/接纳/版本检查，
只回收断开来源，等待真实Remove；不能以它断开活跃来源。失败连接回收完成才能进入下一轮发现。

StopAsync取消并等待本次策略/IO，保留未交付UI事实，OnDisable请求Stop、不自动重启；
重新启用后先Update消费终态，才能显式开始下一轮。OnDestroy/退出请求永久Close；
Unity不等待这些回调。Close只拥有组合策略及Discovery工作，**不关闭借用Manager**。
需要收据时先await connector.CloseAsync，再await manager.ShutdownAsync，最后销毁对象。
应用不得在回调里同步等待自身退出。Manager单独Shutdown不代表组合已经Close。

事件由DiscoveryRuntime.Update转发，逐订阅者隔离异常；回调内Close后不再进入后续订阅者。
可选PulletDiscoveryEventRelay只负责Inspector监听，禁用它不停止网络。
没有第二Payload缓存或无主自动重启工作。

## 迁移必须显式进行

升级前保存旧场景/Prefab及Inspector配置/持久事件备份；不要先打开并重存旧资产再找丢失字段。
旧Manager自动策略字段通过FormerlySerializedAs进入隐藏的迁移检测标记：
未确认迁移时Validate/Connect拒绝执行，包括旧Direct值0；不会默认解释成新固定连接。
这是资产兼容拒绝，不执行旧发现或转发。旧公开C#成员会编译报错，须同步改调用方。

| 原位置 | 新位置/处理 |
| --- | --- |
| Manager.autoConnectMode (0/1/2) | Connector.autoConnectMode，数值不变 |
| Manager.enableDiscovery | Connector.enableDiscovery |
| discoveryPort/TimeoutSeconds/MaxAttempts/SendIntervalSeconds | Connector同名字段，Native上限：60秒、32次 |
| discoveryRetrySeconds/retryDiscoveryUntilConnected/discoveryServiceType | Connector同名字段 |
| Manager.connectOnStart | 要自动发现则迁Connector.connectOnStart，并关闭Manager.connectOnStart；不得同时启用 |
| DiscoverAsync/StartDiscovery/StopDiscovery | Connector.DiscoverAsync / 显式观察Task / await StopAsync |
| IsDiscovering/LastDiscoveredServers/发现事件 | Connector对应成员，服务器视图为不可变DiscoveredServer |
| ServersDiscovered | 改订阅Connector.DiscoverySucceeded，不保留重复事件别名 |
| Relay五个onDiscovery事件 | PulletDiscoveryEventRelay同名字段，重新绑定持久监听 |
| ServerListEvent/PulletServerInfo[] | ServersEvent/DiscoveredServer[]；字段改只读属性，持久监听签名须改 |
| ConnectAsync(PulletServerInfo) | new ConnectionEndpoint(server.Host, TcpPort, UdpPort, WebPort)，交Manager连接 |
| ActiveServer | ActiveEndpoint，仅端点；发现元数据由消费者保留 |

核对旧值/监听迁移完成后，显式调用Manager.ConfirmDiscoveryMigration（也有组件ContextMenu），
保存资产并验证；该动作只是用户确认，不猜测或复制已丢失字段。
不用发现时同样明确决定固定连接策略后确认。不要把确认当成自动修复按钮。
核心Manager/Relay脚本GUID与原Prefab GUID不变，但通用Prefab现在默认不自动连接；
需显式开启固定连接或挂组合组件。旧场景不会由此自动安装新组件。

Native资源上限额外暴露maximumResults/maximumReceivedDatagrams/maximumRetainedBytes，
默认256/4096/1MiB，统计有效保留预算不等于RSS。
旧超出Native限额配置须显式修正，不静默截断。

## 旧验证映射和范围

原七项PulletServerDiscoveryTests迁到DiscoveryCompatibilityTests，覆盖回复地址替换、过滤、
重复更新、取消、实际广播后取消、非法端口及超时。新取消令牌属于round的linked owner，
不保证与调用方token同值，但仍需实际退出且不能返回成功空列表。
原六项策略测试迁到组合包，保留选择、禁用发现、Direct/DiscoverFirst决策及失败回收/取消断言。
原Manager的发现配置拒绝迁到组合验证；原Manager相应测试改为旧资产显式拒绝/确认。
原PlayMode场景卸载中的连接断言保留，Discovery销毁责任转到组合owner的验证，最终实际PlayMode仍属U11。

本批不迁Messaging；核心暂留Json.NET依赖来自旧Messaging，不假称已经无JSON依赖。
Windows Editor局部验证不代表Player、广播跨机、IL2CPP/PICO或下游升级验收。
