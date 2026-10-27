# PulletFramework.NetClient.Discovery

统一netclient包内的可选连接选择模块，不再单独安装UPM包。
依赖NetClient与Discovery两个程序集，不让两者反向依赖；版本与迁移步骤见根README。

在已有PulletNetworkManager的GameObject上添加PulletDiscoveryConnector。
关闭Manager.connectOnStart，选择Connector.autoConnectMode（Direct=0、
DiscoverFirst=1、DirectThenDiscover=2），由界面await AutoConnectAsync，
或显式开启Connector.connectOnStart。不会默认自动启动无限发现。

只观察发现时await DiscoverAsync；会返回不可变DiscoverySnapshot。
取消是取消，不是成功空列表。帧Update交付Started/Attempted/成功或失败/Stopped，
同一round终态交付后才能重用。可选PulletDiscoveryEventRelay供Inspector监听。
禁用Relay只退订；禁用Connector请求停止本轮，重新启用不自动重启。

发现选择后交Manager.ConnectAsync准确端点；不做广告端口缺失回退。
手动连接/断开的新命令会使旧自动选择取消。Framework不选择登录、战斗等业务角色，
发现不是鉴权，不信任广告身份作为业务登录凭证。

退出：先await connector.CloseAsync，再await manager.ShutdownAsync，然后销毁对象。
Close只等待本组件的策略/发现IO及已进入发现UI回调，不拥有/关闭Manager。
Unity的OnDisable/OnDestroy不会等待异步任务，等待关闭完成时必须显式await；
回调中可以请求Close，但不能同步等待自己返回。

旧Manager/Relay字段与持久事件必须显式迁移，见开发仓库
Docs/Unity/Discovery-U9B-Migration.zh-CN.md；保留旧资产备份后再升级。
核心迁移标记不会执行旧发现或自动复制已丢失字段；确认前连接会拒绝。
本包不实现Socket/JSON解析，不增加Payload缓存，不改Native DLL或wire。

U11已通过Unity2022.3.62f3 Windows实际PlayMode及x64 Mono Development Low裁剪Player的
受控回环发现→raw连接/收发/显式退出；独立Discovery→typed也在同轮有限验证。
测试环境见开发仓库Docs/Unity/U11-PC-Acceptance.zh-CN.md；跨机广播与设备情况见支持矩阵。
