# Unity五包合一：只整合交付，不合并职责

2026-10-19，用户确认后仅整合PulletNet/UnityPackages开发源。当前只保留 **PulletFramework.NetClient / com.xmjgame.pullet-framework.netclient / 3.0.0-pre.2** 一个UPM包。

## 保持什么，改变什么

| 路径/程序集 | 责任 |
| --- | --- |
| Runtime / PulletFramework.NetClient | 原始连接、主线程交付、Manager及关闭 |
| Modules/Discovery / PulletFramework.Discovery | 独立发现薄适配 |
| Modules/Messaging / PulletFramework.Messaging | 独立消息会话，显式codec |
| Modules/NetClient.Discovery / PulletFramework.NetClient.Discovery | 发现选择、Inspector组合 |
| Modules/NetClient.Messaging / PulletFramework.NetClient.Messaging | 既有Runtime上的显式typed来源绑定 |

五个asmdef仍独立，程序集名/命名空间/依赖方向不变；没有新的网络owner、默认服务、消息队列或业务协议。安装包含全部模块，功能仍按需实例化，raw程序集不反向引用发现/消息。

以前四个附加包的Runtime/Tests/Validation~/NativeInput移入主包Modules相应目录，已有47个C#文件、83个.meta和其余保留文件按映射核对未改变；旧四份package.json及已有三份package.json.meta移除，不删测试或运行时能力。包root原GUID保留，已有模块root.meta随目录迁移，新Modules容器新增独立GUID。

167个旧源文件中152个非README/manifest文件逐项字节保留，5份README和主包manifest更新，7份旧包清单/其meta移除，新增1份Modules.meta；主包导出160文件（另有根目录.meta不在包内）。完整映射见[机器收据](Evidence/single-unity-package-20261019.json)。旧布局备份在artifacts/unity-single-package-20261019/Before，也可从Git恢复；不是不可恢复删除。

包版本pre.1→pre.2表示布局升级，不改变wire或原生程序集版本。当前4份DLL与3份NativeInput锁全部保留原始身份：raw/Messaging仍2205c8d实际CI输入，Discovery仍d05c59c独立输入。**这不是29d923e原生候选升级。** 服务端/NuGet拆分保持不变。

代价也明确：主包统一声明Newtonsoft3.2.1，raw-only消费者也会解析该依赖；不再提供无消息/发现依赖的最小raw UPM包。不承诺包体更小或所有未使用程序集都能裁剪掉。

## 导出与验证

现有Export工具改为校验3份NativeInput/4份DLL、拒绝额外DLL/嵌套清单、重复asmdef名/asset GUID；原路径/reparse保护及逐文件只读快照规则保留。原就地重建入口继续拒绝覆盖锁定DLL，不建立新的发包系统。

- 最终9项导出保护检查通过，包括错误原生/模块DLL锁、额外DLL、重复程序集、重复GUID负例；独立新目录导出160文件全部字节一致。
- Unity2022.3.62f3 Windows隔离单包Editor：原有五个测试程序集 **177执行/通过，0失败/Skip**，未删用例。
- 实际EnterPlayMode：复用U11公开消费者，16个独立场景/22次真实响应回显，主线程和帧推进、owner关闭、Unity/服务端exit0通过。
- 无Test Framework的Windows x64 Mono Development Low裁剪Player：构建成功0error、1warning，实际16场景/22回显、退出前join及Quit收据通过；Player/服务端exit0。
- 两个新隔离项目的UPM只解析1个Pullet包，source=embedded，Newtonsoft3.2.1由注册源解析。没有工程外file绝对路径包引用，不打开妈祖工程。

Player唯一构建Warning仍为 `GfxDevice renderer is null` 的headless场景环境光/反射探针提示，不声称零Warning。Unity日志另含授权更新/Token错误，但未阻止编译/运行/正常退出；没有修改授权环境或隐藏日志。原件保留。

PlayMode快照形成后README措辞有调整；验证全体非README文件与PlayMode/Player输入都相符，最终导出按最终README字节签收。执行器记录的source是父提交e535404，实际包是工作区布局快照，不伪称父提交干净包；逐文件hash及移动映射才标识本批输入。

原始证据/隔离工程/完整副本在 `artifacts/unity-single-package-20261019/`，最终可安装导出在 `release-export/Package`。本次仅导出，没有安装或同步任何下游。

## 以后迁移安装副本时

1. 先备份目标Packages/manifest.json、packages-lock.json及原五包，确认没有用户定制。
2. 用完整pre.2固定导出替换netclient嵌入副本，并移除四个旧模块包目录及它们的manifest/testables登记，不能让新旧同时存在。否则会重复DLL、asmdef和GUID。
3. root包ID、程序集、namespace及脚本GUID保持；它们不代表以前已经退役的API/Inspector持久事件突然兼容。旧发现/消息迁移表仍用于对应历史API迁移。
4. 验证UPM只有一个Pullet包、实际功能和资产引用及关闭，再安排下游提交。回退必须恢复整套旧五包及目标manifest/lock，不只回退一个DLL。不要删Library或清空全局缓存掩盖重复安装。

这四步是后续说明，本次未执行。先开发源，之后另获用户授权才同步PulletFramework，最后才升级妈祖；不直接在安装副本维护第二套实现。

## 停止边界

仅单包合并与当前Windows有限验收完成。无原生全量/CI/压力/长稳、DLL升级、正式发布/许可证选择、Framework/妈祖/XR写入；Android IL2CPP/PICO、真实Protobuf/AOT、其他Unity版本/平台、OS后台、跨机广播/NAT仍未由本批覆盖。typed透明恢复/保留重绑限制不变。合包不是新增平台支持，也不撤销内部模块边界。
