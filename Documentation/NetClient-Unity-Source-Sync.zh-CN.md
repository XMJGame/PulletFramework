# NetClient 开发源与安装副本同步

2026-10-16。本地安装副本最新同步自 PulletNet 本地main的 `40bf84fb2bfe3d1a699ef099325f505aca362b2d`，来源路径 `UnityPackages/PulletFramework.NetClient`。旧临时工作区已合入主目录并移除，不再从PulletNet-Unity开发。

该来源是唯一开发源，本仓库同名目录只保存验证后的同步副本。妈祖等业务工程使用各自Packages内固定版本，不通过外部开发路径或自动同步追随源码；本次未升级妈祖的2.0.2或XR安装，也未推送/发布。

本次通过只读导出的90个包文件逐项SHA256与开发源/本地同步副本一致核对，GUID/DLL保留。准确NativeInput仍为2b046d5的RC1，两份DLL/hash不变。当前是3.0.0-pre.1开发检查点，不是正式发布。

此次同步补齐U6示例/实际PlayMode和U7 Windows独立Player验证消费者。U6有8场景/12回显；U7在2022.3.62f3 Windows x64 Mono Development/Low裁剪下7场景/10回显、显式Shutdown及双方exit0，构建0错误/1警告（详情未归属）。嵌入包无Test Framework时能编译，安装该框架的相反条件执行124项、零失败/跳过。原失败保留；不把复制包说成再次执行测试。OS真实后台、IL2CPP/PICO和其他组合待验，妈祖仍2.0.2。

机器同步收据见[netclient-source-sync.json](Evidence/netclient-source-sync.json)；U6/U7合同及原始失败见本目录对应检查点和Evidence。收据描述本机此刻导出/复制的字节；不同Git checkout的换行转换可能改变文本hash，之后安装仍应重新校验导出收据，不能只凭版本号或提交声称字节相同。

此前c0d8fe8的83文件同步和U5的135项/16场景20进程320次/20回显仍作为历史检查点，不冒称本批重新跑过所有竞态。

当前合同见 [Manager/Shutdown](NetClient-U5-Manager-Contract.zh-CN.md)、[Runtime](NetClient-U3-Runtime-Contract.zh-CN.md)；完整验证和首轮失败见 [U5检查点](NetClient-U5-Manager-Checkpoint.zh-CN.md)及[机器记录](Evidence/netclient-u5-manager.json)。旧EventPump/BoundedPayloadQueue与12项测试随来源退役，保障映射保留在Manager合同，源码可从Git恢复。

原在Framework先行实现的b4f319f/355556a及更早文档仍按历史事实保留；其中旧的“Framework是开发源”描述不是当前维护指令。今后只在PulletNet UnityPackages开发、验证，再明确同步本仓库，不在两处独立演进。
