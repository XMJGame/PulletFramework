# NetClient 3.0.0配置更新

2026-10-21，底层DLL更新至PulletNet提交`10f9619f670d97853e170e668e83db43833986c6`。

- Configuration.md补充参数选择、默认值与适用条件。
- ClientSDK.xml补充原生参数中文注释。
- 包ID、程序集、命名空间与资源GUID保持不变。
- 默认参数和运行逻辑未改。

NativeInput记录四份DLL的来源与校验值。176个导出文件已逐项比对；对应日志、原始包和备份保存在开发源artifacts/configuration-repack-20261021。

妈祖Unity连接/场景专项21项、服务端应用测试120项通过。此次未运行新的Player、设备或远端CI测试。
