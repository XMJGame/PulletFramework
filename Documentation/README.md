# 文档索引

## 使用与设计

- [项目首页](../README.md)：模块选择、安装与使用入口
- [架构说明](Architecture.zh-CN.md)
- [模块状态与路线图](Roadmap.zh-CN.md)
- [验证范围与记录](Validation.zh-CN.md)
- [Core事件与通用TCP的行为与兼容说明](Core-Event-Tcp.zh-CN.md)
- [维护约定](Maintenance.zh-CN.md)

## 模块手册

- [Core](../PulletFramework/README.md)
- [YooAsset](../PulletFramework.YooAsset/README.md)
- [AssetPublishing](../PulletFramework.AssetPublishing/README.md)
- [MiniGame](../PulletFramework.MiniGame/README.md)
- [HybridCLR](../PulletFramework.HybridCLR/README.md)
- [NetClient](../PulletFramework.NetClient/README.md)

NetClient的发现、消息及组合适配已放入同一个安装包，分别见：
[发现](../PulletFramework.NetClient/Modules/Discovery/README.md)、
[消息](../PulletFramework.NetClient/Modules/Messaging/README.md)、
[发现连接组合](../PulletFramework.NetClient/Modules/NetClient.Discovery/README.md)、
[消息连接组合](../PulletFramework.NetClient/Modules/NetClient.Messaging/README.md)。

## 历史与追溯

带阶段号或日期的旧设计、迁移及验证文档保留用于解释当时决策，不作为当前安装入口。先读模块手册，核对[当前网络封包说明](NetClient-Configuration-Repack-20261021.md)，需要定位版本变化时再阅读[验证索引](Validation.zh-CN.md)和[Evidence](Evidence/)。

[DevelopmentStatus](DevelopmentStatus.zh-CN.md)保留旧链接入口，具体状态转由路线图维护。
