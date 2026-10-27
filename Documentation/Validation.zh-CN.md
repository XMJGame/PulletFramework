# 验证范围与记录

这里区分“已有实现”和“实际运行过的组合”。测试数量、源提交、DLL校验值用于追溯，不作为稳定性或容量的宣传指标。

## 阅读方式

- 单元或模块测试验证相应契约，不自动覆盖Unity Player、IL2CPP和真机。
- 开发者工具验证不等同于手机运行。
- 后续说明或注释更新，不会自动让所有历史结果成为新版本验收。
- 同版本号的重新封包需核对准确输入，不能只看3.0.0等版本字符串。

## 主要记录

| 范围 | 结果与边界 | 记录 |
| --- | --- | --- |
| Core / 资源 / 小游戏平台基础 | 模块回归、三组UPM组合编译、Windows核心Player及两平台开发者工具；部分设备与线上能力未验 | [2026-09基础验证](History/Foundation-Validation-2026-09.zh-CN.md) |
| 基础模块生命周期 | 窗口、音频、初始化、配置读取与池化的早期设计及验证 | [加固记录](FoundationHardeningPlan.2026-09-15.zh-CN.md)、[原始评审](FoundationReview.2026-09-15.zh-CN.md) |
| 网络Unity消费者 | 原始五包输入的Editor及Windows Mono真实连接/关闭；不是当前所有安装字节的重新测试 | [PC验证](U11-PC-Acceptance.zh-CN.md) |
| NetClient统一安装包 | 一个UPM入口，内部程序集保持分离 | [合包记录](Single-Package-Consolidation.zh-CN.md) |
| NetClient Inspector兼容性 | 2021.3/2022.3中文标签、Tooltip与绘制路径等，见对应输入记录 | [安装历史](NetClient-3.0.0-Installation.zh-CN.md) |
| 当前网络配置封包 | 2026-10-21配置整理来源及安装验证；不等于全模块、Player或真机重新验收 | [配置封包记录](NetClient-Configuration-Repack-20261021.md) |
| Core事件与通用TCP维护 | Unity PlayMode37项、EditMode8项；源码诊断21项重复20个进程；不是PulletNet链路或设备验收 | [行为与兼容说明](Core-Event-Tcp.zh-CN.md) |
| 平台问题 | 微信/抖音SDK、启动与资源流程的问题及处理 | [故障排查](../PulletFramework.MiniGame/Documentation/PlatformTroubleshooting.zh-CN.md) |

## 原始数据

[Evidence目录](Evidence/)保存已提交的结构化记录。文档中提及但未提交的本地产物，不等于公开仓库可直接下载的测试附件；需要复现时先核对输入和环境。

历史记录里的旧提交号按记录发生时的身份保留。部分提交说明后来整理过，不能因此把旧引用擅自替换成不同来源。

## 当前限制

- 没有一份覆盖所有包、所有目标平台的统一验收。
- MiniGame广告、排行榜与真机缓存仍需要实际平台条件。
- 核心池化的Editor合成查找数据不是业务吞吐能力，也不是零GC证明。
- NetClient不支持WebGL/微信/抖音小游戏；不同设备、AOT与网络环境分别验收。
- 历史失败与复测结果共同保留；不能只保留最后一次成功结果。

新增验证应记录版本/输入、环境、命令或步骤、实际结果和未覆盖范围；使用者需要这些信息判断结果能否适用于自己的项目。
