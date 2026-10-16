# NetClient 开发源与安装副本同步

2026-10-16。本地安装副本同步自 PulletNet `codex/unity-runtime-manager` 的 `c0d8fe8adf4515b065cc94b9d532f3fdcb025284`，来源路径 `UnityPackages/PulletFramework.NetClient`。

该来源是唯一开发源，本仓库同名目录只保存验证后的同步副本。妈祖等业务工程使用各自Packages内固定版本，不通过外部开发路径或自动同步追随源码；本次未升级妈祖的2.0.2或XR安装，也未推送/发布。

本次83个包文件逐字节一致核对，通过后才同步。验证复用来源的135项Unity Windows Editor包检查点、16场景20进程320次和20次真实回显/Stop/Dispose，不把相同文件同步当作重新执行了一套测试。准确NativeInput仍为2b046d5的RC1，两份DLL/hash不变。当前是3.0.0-pre.1开发检查点，不是正式发布或Player/IL2CPP/PICO验收。

当前合同见 [Manager/Shutdown](NetClient-U5-Manager-Contract.zh-CN.md)、[Runtime](NetClient-U3-Runtime-Contract.zh-CN.md)；完整验证和首轮失败见 [U5检查点](NetClient-U5-Manager-Checkpoint.zh-CN.md)及[机器记录](Evidence/netclient-u5-manager.json)。旧EventPump/BoundedPayloadQueue与12项测试随来源退役，保障映射保留在Manager合同，源码可从Git恢复。

原在Framework先行实现的b4f319f/355556a及更早文档仍按历史事实保留；其中旧的“Framework是开发源”描述不是当前维护指令。今后只在PulletNet UnityPackages开发、验证，再明确同步本仓库，不在两处独立演进。
