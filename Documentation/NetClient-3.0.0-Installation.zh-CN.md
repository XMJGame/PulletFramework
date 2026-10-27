# NetClient 3.0.0 安装与版本边界

> 当前安装内容以[2026-10-21配置封包说明](NetClient-Configuration-Repack-20261021.md)和包内NativeInput为准。下文保留早期3.0.0及Inspector增量的验证历史，各节中的“当前”仅指记录发生时的输入。首次接入请先读[NetClient手册](../PulletFramework.NetClient/README.md)。

本仓库的PulletFramework.NetClient是PulletNet开发源的固定安装镜像，不在两处独立开发。只安装一个完整包com.xmjgame.pullet-framework.netclient；五个内部程序集仍独立，不另装旧四个模块UPM。

## 当前Unity Inspector版本兼容增量

当前镜像为PulletNet 42d0dcb完整172文件快照，显式绘制稳定中文模式，增加独立标签/Tooltip/地址和实际绘制检查；Unity2021.3/2022.3各51项、导出9项、妈祖Editor20项通过。四DLL、Runtime、默认值/策略不变；源快照见[当前导出](Evidence/netclient-inspector-42d0dcb-export.json)。原正式3.0.0及下面增量均保留当时身份，不覆盖历史收据，不声称新Player/设备验收。

## 历史a472f82中文展示与双语注释增量

当前镜像为PulletNet a472f82的完整170文件快照，包含三种连接模式的中文选项/日志、高级参数Tooltip、单层折叠和关键类型中英summary。网络策略和序列化字段值不变，原生四DLL保持原2f0cfc3字节；Runtime注释/属性/日志文本已更新。当前固定来源见[导出收据](Evidence/netclient-chinese-a472f82-export.json)，同步及限制见[记录](NetClient-Unity-Source-Sync.zh-CN.md)。这是3.0.0上的显式源码增量，不覆盖原正式封包或旧d60a0ac收据。

## 历史d60a0ac编辑器增量

当前镜像已加入PulletNet d60a0ac的Inspector增量，完整170源文件。两个Editor-only程序集不进入Player，五个运行时程序集和四DLL仍原3.0.0字节；详细来源与验证见[同步记录](NetClient-Unity-Source-Sync.zh-CN.md)。包版号未变化，安装必须按准确收据/源提交区分，不把本增量当作重新发布的同名nupkg或原160文件封包。旧[正式快照收据](Evidence/netclient-3.0.0-export.json)不覆盖；当前见[增量收据](Evidence/netclient-inspector-d60a0ac-export.json)。

## 原始3.0.0固定输入（增量前）

- Unity源48eb7918863ce77364a7267db7a3c7cabbe342a9，完整160源文件逐字节复制；UPM版本3.0.0。
- Protocol/ClientSDK/Messaging/Discovery四DLL均来自2f0cfc33575cc6e10398d499b2d7898c9f0a716c干净导出的实际3.0.0包；wire3与discovery1不变。包内三份NativeInput锁定原nupkg和DLL哈希。
- 推送门槛为c7bee1c / 原始run39221865861 attempt1，包含私有TCP测试入口修正；生产与版本配置和原生封包输入一致，实际DLL仍保留2f0cfc3身份，不冒称CI构建副本。
- 精确包快照见[镜像收据](Evidence/netclient-3.0.0-export.json)。旧pre.3及更早收据保留历史身份，不作为当前安装输入。

## 安装与使用

Unity业务工程复制完整包到自身Packages/com.xmjgame.pullet-framework.netclient，不通过工程外绝对路径/软链接跟随开发仓库。保留所有meta，移出旧独立Discovery/Messaging/组合包及manifest/testables登记，避免重复DLL/程序集/GUID。

简单连接用现有Manager/Prefab；多连接用PulletClientRuntime和准确来源。按需构造消息或发现，不默认多建网络Host/队列。默认不自动连接，由应用选择唯一连接入口并每帧推进；Payload仅回调借用，跨异步/帧持有需owned副本并Dispose。

退出显式await各owner的关闭，不在回调中Wait/Result或等待自身退出；OnDestroy/OnApplicationQuit本身不会等待异步清理。通用typed绑定一次连接代际，不支持透明恢复/UDP保留重绑。发现不是鉴权，发送接纳不是对端ACK或业务执行成功。

完整功能/原生接入/配置/运维/未来路线图在PulletNet仓库Docs/README.md；旧发现资产和RPC/JSON接口需按该仓库Docs/Unity迁移合同处理。脚本GUID保留不等于已退役API可继续使用。

## 验证、回退与限制

上游本地原生3432、九包/2358结构签名与消费者通过；Unity2022.3.62f3包Editor177、实际PlayMode/Mono Player各16场景22回显通过。对应原始双平台CI门槛及本次妈祖验证另由PulletNet的同步记录固定，不用历史pre.3结果补签。

本次只更新NetClient镜像与说明，不升级基础Framework/其它模块、不修改用户未跟踪对齐文档。旧pre.3包及说明完整备份在PulletNet artifacts/release-3.0.0-sync-20261019-fixed/Before。关闭Unity后可成套恢复原包、项目manifest/lock；不只覆盖旧DLL。业务服务器独立选择版本，本次不自动升级。

没有公开发包、v3.0.0标签、GitHub Release或新许可证授权。Android/IL2CPP/PICO、OS真实后台、跨机广播/NAT及容量仍按各自证据限制声明；妈祖没有新Standalone构建。其它基础Framework仍保持自身开发状态，不因NetClient3.0.0而称整体稳定发布。
