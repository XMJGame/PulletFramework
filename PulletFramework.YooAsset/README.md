# PulletFramework.YooAsset

`PulletFramework.YooAsset` 是基础框架的可选资源适配模块。它负责 YooAsset 初始化、
运行时资源适配、资源构建设置、发布报告和 COS 上传工具；不包含微信或抖音平台能力。

## 安装顺序

```text
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework
https://github.com/tuyoogame/YooAsset.git?path=/Assets/YooAsset#3.0.5
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.YooAsset
```

安装后，`Pullets/Workspace` 会自动出现“YooAsset 资源”页面。移除本模块后，基础框架仍可使用，
但项目必须安装其他 `IResourceAdapter`，或不调用依赖外部资源的窗口、对象池和音频功能。

配置资产建议保存在：

```text
Assets/Settings/Pullets/YooAsset/Resources/PulletYooAssetSettings.asset
```

运行时由 `PulletYooAssetSettingsData` 统一加载该资产，业务入口只需调用
`PulletYooAssetRuntime.Initialize()`，不需要序列化或主动获取配置对象。

业务 AssetBundle CDN 与微信、抖音导出工具使用的 Unity 首包 CDN 是两套配置。
