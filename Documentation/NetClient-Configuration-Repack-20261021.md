# NetClient 3.0.0 本地配置文档重封包

2026-10-21 用户明确授权保留 3.0.0 更新固定镜像。原生来源为
`10f9619f670d97853e170e668e83db43833986c6`，四份 DLL 与 NativeInput 均已更新。
包内 Configuration.md 提供完整参数选择指南，ClientSDK.xml 提供原生参数中文注释。
包 ID、namespace、asmdef 与旧资产 GUID、默认参数和运行逻辑不变。

镜像 176 文件逐 SHA256 与 PulletNet 的完整导出一致；原始导出、九包收据和回退副本位于
PulletNet/artifacts/configuration-repack-20261021。准确包/来源锁见 PulletFramework.NetClient
内 NativeInput.json 及 Modules 中各自的锁；同为 3.0.0 不代表字节与原 10-19 封包相同。

对应妈祖固定副本已刷新/编译，实际加载新 DLL，连接/场景专项 21 项通过；服务端四包
采用同一新输入，应用测试 120 项通过。该结果不外推 Framework 全模块、Player 或真机。
没有新远端 CI，不覆盖原发布证据，也未修改用户 NetClient-3.0-Alignment.zh-CN.md。
