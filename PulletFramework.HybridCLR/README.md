# PulletFramework.HybridCLR

`PulletFramework.HybridCLR` 是可选的 Editor-only 工作流包，用于调用 HybridCLR 官方生成/编译命令，并把 AOT 补充元数据程序集与热更新程序集复制到项目资源目录。基础框架、YooAsset 和小游戏均不依赖它。

## 适用场景

- App 项目确实需要代码热更新，并已确定审核、合规和版本治理方案。
- 不适用于仅做 YooAsset 资源更新的项目。
- 当前小游戏验证工程不启用本模块，避免引入不需要的构建复杂度。

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
| 生成、编译并复制全部程序集 | 顺序执行上述步骤，并复制两类 DLL |
| 仅复制 AOT 程序集 | 从 HybridCLR 裁剪输出复制配置中的补充元数据 DLL |
| 仅复制热更新程序集 | 从 HybridCLR 编译输出复制热更新 DLL |

默认复制目录位于：

```text
Assets/Art/Assembly/AOT
Assets/Art/Assembly/HotUpdate
```

复制后的 DLL 使用 `.bytes` 扩展名，后续应由项目自己的 YooAsset Collector 或其他资源系统决定如何打包与加载。

## 推荐流程

1. 切换到最终目标平台并完成 HybridCLR Installer 与程序集配置。
2. 点击“生成、编译并复制全部程序集”。生成 AOT DLL 可能触发临时裁剪构建。
3. 检查复制目录，将这些文件纳入对应资源 Package。
4. 按 HybridCLR 官方流程实现元数据加载、热更新程序集加载和入口调用。
5. 构建目标 Player，并在与发布一致的后端和裁剪配置下测试。

本模块不会自动修改 HybridCLR 配置、不会生成业务热更新入口、不会替代 Player 构建，也不会保证不同平台能共用同一批 DLL。切换 BuildTarget 后必须重新生成对应产物。

## 验证状态

已完成 Unity `2022.3.62f3` + HybridCLR `8.14.1` 的 API 兼容性评估。本轮小游戏工程未安装 HybridCLR，因此尚未执行完整 UPM 重装、DLL 生成、AOT 元数据加载和热更新入口运行验收；在这些步骤完成前，本包保持可选实验状态。
