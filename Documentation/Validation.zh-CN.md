# 测试记录

按模块列出测试环境与结果。具体版本、步骤和文件校验值保存在对应记录中。

| 模块 | 环境与内容 | 记录 |
| --- | --- | --- |
| Android / iOS | 构建与运行验证通过 | 作者实测确认 |
| Core / 资源 / 小游戏 | 模块回归、三组UPM安装、Windows Player及微信/抖音开发者工具 | [基础测试](History/Foundation-Validation-2026-09.zh-CN.md) |
| Core生命周期 | 窗口、音频、初始化、配置读取与对象池 | [维护计划](FoundationHardeningPlan.2026-09-15.zh-CN.md)、[代码评审](FoundationReview.2026-09-15.zh-CN.md) |
| NetClient连接 | Editor和Windows Mono的连接、收发与关闭 | [PC测试](U11-PC-Acceptance.zh-CN.md) |
| NetClient安装 | 多个模块合并为一个UPM包 | [安装包整理](Single-Package-Consolidation.zh-CN.md) |
| NetClient Inspector | Unity2021.3/2022.3的中文标签、Tooltip及绘制 | [版本记录](NetClient-3.0.0-Installation.zh-CN.md) |
| NetClient配置 | 2026-10-21配置更新与安装 | [配置更新](NetClient-Configuration-Repack-20261021.md) |
| Core事件与TCP | Unity PlayMode37项、EditMode8项；21项源码诊断运行20个独立进程 | [修复说明](Core-Event-Tcp.zh-CN.md) |
| 微信/抖音小游戏 | 已测试，尚未提交上线 | 作者实测确认；[故障排查](../PulletFramework.MiniGame/Documentation/PlatformTroubleshooting.zh-CN.md) |

[Evidence](Evidence/)保存已提交的结构化结果；体积较大的本地日志和构建产物未随仓库上传。历史记录中的提交号对应当时源码，部分说明整理前的提交可通过保留的历史引用查找。

Android、iOS 及微信、抖音小游戏的测试结果由作者于 2026-10-28 确认，小游戏尚未提交上线。各模块的专项测试仍按对应记录查看。

平台状态见[路线图](Roadmap.zh-CN.md)。广告位、账号和排行榜数据按接入项目的实际配置检查。

新增测试记录写清源码版本、环境、运行方式和结果即可。Editor、开发者工具、Player与真机分别记录。
