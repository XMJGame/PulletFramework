# PulletAssetPublishing

编辑器专用的资源发布模块。它保存供应商、对象存储位置和上传凭据，并通过
`Pullet Workspace/资源发布` 提供 UXML 配置页面。

当前内置腾讯云 COS 配置模型；后续供应商通过独立 Provider 注册，不修改 YooAsset 或小游戏业务代码。
配置资产位于：

```text
Assets/Settings/Pullets/Publishing/PulletAssetPublishingSettings.asset
```

该资产不在 `Resources`，运行时代码不会加载它。真实密钥仍不应提交到公共仓库。
