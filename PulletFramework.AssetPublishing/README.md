# PulletFramework.AssetPublishing

`PulletFramework.AssetPublishing` 是 Editor-only 的资源发布基础包，负责对象存储供应商发现、配置、上传会话、公开 URL 拼接、并发发布保护和小游戏下载 CORS。它不负责 YooAsset 清单规则，也不包含任何 Player 运行时代码。

## 安装

```text
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.AssetPublishing
```

安装后打开 `Pullets/Workspace -> 资源发布`。当前内置腾讯云 COS；其他供应商可以通过实现 `IPulletObjectStorageProvider` 独立注册，无需修改 YooAsset 或 MiniGame 模块。

## 配置

配置资产默认位于：

```text
Assets/Settings/Pullets/Publishing/PulletAssetPublishingSettings.asset
```

| 字段 | 含义 |
| --- | --- |
| AccessKey ID / SecretId | 对象存储访问身份 |
| AccessKey Secret / SecretKey | 对象存储访问密钥 |
| Bucket | 存储桶名称 |
| Region | 地域，例如 `ap-guangzhou` |
| 自定义 Endpoint | 可选的 API 端点；使用供应商默认端点时留空 |
| 远端根目录 | 所有项目对象键的公共前缀，例如 `pullet_minigame` |
| 公开下载域名 | 客户端实际访问的 HTTPS 域名，可使用 COS 默认域名或自定义 CDN 域名 |

“验证配置”检查字段和 Provider 规则；“保存”写入配置资产；“定位配置资产”在 Project 窗口中选中该文件。小游戏或 YooAsset 页面会创建配置快照后再发布，上传过程中修改界面不会混入当前会话。

## 安全边界

配置资产虽然不在 `Resources`、不会进入 Player，但其中的密钥仍是明文。包含真实凭据的资产必须作为本机私有配置，不得提交到 Git、公共包或构建产物；本模块不提供加密或服务端密钥托管。建议使用权限受限、可轮换的发布账号，并在凭据曾进入历史记录时立即轮换。

## 发布约定

- YooAsset 模块决定 Bundle、清单和版本指针的顺序；本模块只执行供应商上传。
- MiniGame 模块决定 Unity Data 首包的目录和导出参数；它与 YooAsset CDN 不是同一个资源路径。
- 同一供应商、同一目标目录的并发发布会被拒绝，避免两个任务交叉覆盖。
- CORS 配置会修改存储桶规则，执行前应确认账号权限和现有规则；不会在普通构建时自动修改。

## 扩展供应商

实现 `IPulletObjectStorageProvider`，提供稳定的 `Id`、配置校验、URL 拼接、上传和 CORS 能力。Editor 使用 `TypeCache` 自动发现实现，无需向核心框架添加供应商判断。

```csharp
public sealed class CustomStorageProvider : IPulletObjectStorageProvider
{
    // 实现 Id、DisplayName、Validate、BuildPublicUrl、UploadAsync 等成员。
}
```

发布代码应通过 `PulletAssetPublishingService.CreateSession()` 获取不可变会话，不要直接长期持有工作台中的可变配置对象。

## 验证状态

已在 Unity `2022.3.62f3` 的全新 UPM 组合工程中完成 Editor 程序集编译，并通过假供应商验证上传失败不前移版本指针、发布期间文件变化会终止流程。腾讯 COS 的公网资源、缓存响应头和 CORS 已在验证项目中检查；其他供应商尚未实现。
