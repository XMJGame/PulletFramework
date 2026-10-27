# NetClient 3.0.0 安装

安装完整的PulletFramework.NetClient包即可使用连接、消息和发现功能。安装地址、API示例与关闭顺序见[包手册](../PulletFramework.NetClient/README.md)。

## 当前内容

当前底层DLL来源为PulletNet提交10f9619，包内NativeInput记录各DLL校验值。参数用途与默认值见[配置指南](../PulletFramework.NetClient/Configuration.md)，该次更新见[配置更新记录](NetClient-Configuration-Repack-20261021.md)。

NetClient支持独立安装，不依赖核心PulletFramework、YooAsset或小游戏模块。包内使用五个运行时程序集，配置界面放在Editor程序集。Json.NET随UPM依赖解析。

升级旧五包布局时，移除原来的四个附加UPM包，保留一个NetClient包，避免重复DLL和程序集。消息与发现的旧接口迁移见PulletNet开发仓库Docs/Unity。

## 3.0.0更新记录

| 源码 | 内容 | 文件记录 |
| --- | --- | --- |
| 48eb791 | 初始统一安装包；原生DLL来自2f0cfc3 | [导出列表](Evidence/netclient-3.0.0-export.json) |
| d60a0ac | Inspector高级配置折叠 | [导出列表](Evidence/netclient-inspector-d60a0ac-export.json) |
| a472f82 | 中文提示与双语注释 | [导出列表](Evidence/netclient-chinese-a472f82-export.json) |
| 42d0dcb | Unity2021.3中文枚举绘制修复 | [导出列表](Evidence/netclient-inspector-42d0dcb-export.json) |
| 10f9619 | 配置说明、中文参数注释与DLL更新 | [更新记录](NetClient-Configuration-Repack-20261021.md) |

这些更新沿用3.0.0版本号，固定安装时请同时保存源码提交与NativeInput。

## 平台与测试

原始3.0.0包含Unity2022.3.62f3 Editor与Windows Mono Player的16个场景、22次回显测试。Inspector中文绘制另有Unity2021.3和2022.3测试。完整结果按对应源码记录查询，见[测试索引](Validation.zh-CN.md)。

当前不支持WebGL与小游戏。设备、IL2CPP、系统后台和跨机发现情况见PulletNet支持矩阵。
