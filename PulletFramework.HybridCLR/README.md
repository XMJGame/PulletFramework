# PulletFramework.HybridCLR

`PulletFramework.HybridCLR` 是可选的代码热更新包，负责调用 HybridCLR 官方生成流程、生成 DLL 版本清单，并在运行时校验和加载程序集。基础框架、YooAsset 和小游戏均不依赖它。

## 适用场景

- App 项目确实需要代码热更新，并已确定审核、合规和版本治理方案。
- 不适用于仅做 YooAsset 资源更新的项目。
- 小游戏项目通常只需要资源更新，不应默认引入本模块；需要验证 WebGL 代码热更新时再单独启用。

## 安装

已验证组合：Unity `2022.3.62f3`、HybridCLR `8.14.1`。

```text
https://github.com/focus-creative-games/hybridclr_unity.git#v8.14.1
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework
https://github.com/XMJGame/PulletFramework.git?path=/PulletFramework.HybridCLR
```

先通过 HybridCLR Installer 完成官方安装，再按官方文档配置热更新程序集与 AOT 补充元数据程序集。安装本包后，`Pullets/Workspace` 会出现 `HybridCLR` 页面。

## 操作

| 操作 | 作用 |
| --- | --- |
| 生成 AOT 补充元数据程序集 | 为当前活动 BuildTarget 运行 HybridCLR 裁剪 AOT DLL 生成流程 |
| 编译热更新程序集 | 为当前活动 BuildTarget 编译已配置的热更新 DLL |
| 官方 Generate/All 并复制程序集 | 执行官方完整生成顺序，再校验并复制两类 DLL |
| 根据已复制 DLL 重新生成清单 | 记录 DLL 名称、程序集版本、类型、SHA-256、大小、热更新程序集依赖和适用 Player 版本 |
| 仅复制 AOT 程序集 | 从 HybridCLR 裁剪输出复制配置中的补充元数据 DLL |
| 仅复制热更新程序集 | 从 HybridCLR 编译输出复制热更新 DLL |

默认复制目录位于：

```text
Assets/Art/Assembly/AOT
Assets/Art/Assembly/HotUpdate
Assets/Art/Assembly/PulletHotUpdateManifest.json
```

复制后的 DLL 使用 `.bytes` 扩展名。运行时通过主框架的 `PulletResources` 加载清单与 DLL，因此项目只需安装对应资源适配器。`PulletHotUpdate.InitializeAsync` 负责读取清单、版本校验、AOT 补充元数据、依赖排序、热更新程序集加载和入口执行。

YooAsset 清单负责 Bundle/资源文件的下载、Hash 和依赖；本模块清单负责 DLL 身份、程序集版本、DLL 字节校验、程序集依赖和 Player 兼容性。内容版本由全部 DLL Hash 自动计算，同一批 DLL 重建仍得到同一版本。两者互补，不手工重复录入。

## 推荐流程

1. 切换到最终目标平台并完成 HybridCLR Installer 与程序集配置。
2. 在 Workspace 的 HybridCLR 页面配置入口程序集、完整类型名和无参数静态入口方法。
3. 点击“官方 Generate/All 并复制程序集”。该过程会编译热更新 DLL，并生成 Il2CppDef、link.xml、裁剪 AOT DLL、桥接函数、AOT 泛型引用和热更新清单。
4. 检查复制目录，将这些文件纳入对应资源 Package。
5. 确保项目资源系统已经通过适配器安装到 `PulletResources`，将业务侧的清单资源地址传给 `PulletHotUpdate.InitializeAsync`。
6. 构建目标 Player，并在与发布一致的后端和裁剪配置下测试。

最小运行时调用如下，资源系统只需提供字节来源：

```csharp
PulletHotUpdateOperation operation = PulletHotUpdate.InitializeAsync(
    "PulletHotUpdateManifest");
yield return operation;
if (!operation.Succeeded)
    throw new InvalidOperationException(operation.Error);
```

清单资源地址属于业务资源规划，不保存在 HybridCLR 项目配置中。入口配置仅用于生成清单，运行时以实际加载到的清单为准。

本模块不会自动修改 HybridCLR 配置、不会生成业务热更新入口、不会替代 Player 构建，也不会保证不同平台能共用同一批 DLL。切换 BuildTarget 后必须重新生成对应产物。

## 验证状态

已在 Unity `2022.3.62f3`、HybridCLR `8.14.1` 完成 Windows x64 IL2CPP Player 实测。资源版本 `1.0.5` 通过 YooAsset 加载热更新 DLL 与 `mscorlib` AOT 补充元数据，入口中的 AOT 泛型代码执行成功，运行日志无加载异常。小游戏项目默认只使用资源更新，不启用代码热更新；本包仍作为需要代码热更新的 App 项目的可选模块。
