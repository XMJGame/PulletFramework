# NetClient 开发源与安装副本同步

## 当前：3.0.0编辑器展示增量（d60a0ac）

完整170个包源文件已从PulletNet d60a0ac固定导出同步；新增两个仅Editor程序集及2项测试，Manager按传输隐藏端口/折叠预算，发现广播/重试/限额折叠。五个运行时程序集、全部运行时字段/默认值和四DLL/NativeInput不变，包版号仍3.0.0。这是显式编辑器增量，不是原48eb791正式快照或新原生封包；旧收据保留。

上游Unity2022.3.62f3隔离35项、导出9项和妈祖正式Editor20项通过；没有新Player/真机或业务联调。只同步本Framework和妈祖，不修改XRVehicle。当前收据见[编辑器增量导出](Evidence/netclient-inspector-d60a0ac-export.json)，原3.0.0说明见下段历史。

## 最新：3.0.0固定镜像

当前160个包源文件来自PulletNet Unity源48eb791；四个原生DLL来自准确2f0cfc3的3.0.0产物，wire3/discovery1不变。原始c7bee1c双平台CI run39221865861/attempt1全通过后同步；妈祖Editor20及真实CharacterFoundation与原2b046d5服务器的兼容和退出验证通过。当前步骤见[3.0.0安装说明](NetClient-3.0.0-Installation.zh-CN.md)及[完整导出收据](Evidence/netclient-3.0.0-export.json)。下面pre.3等段落仅为历史时点，不再作为当前输入；其它模块不升级，不公开发包或打标签。

## 2026-10-19 最新：统一包pre.3原生输入升级

开发源bce839b已通过隔离验收后同步160个源文件：raw/Messaging DLL取自准确29d923e原始Windows CI，Discovery仍原d05c59c锁。未改包装代码/全部meta/其它基础模块。包内NativeInput与[导出收据](Evidence/netclient-native-input-sync.json)固定实际哈希；完整pre.2副本保存在PulletNet的artifacts/unity-native-input-20261019-29d923e/BeforeSync，可成套回退。使用自身Packages副本，不自动追随开发目录。

上游导出9项、Editor177项、PlayMode与Mono Player各16场景22回显通过；再同步妈祖，正式Editor20及真实业务流程与原2b046d5服务器通过，资源归零/正常退出。未重建妈祖Standalone，Android/IL2CPP/PICO和跨机等仍待验。本地提交，不推送/新CI/发布；以下pre.2/五包段落保持历史身份，不作为当前安装步骤。

## 2026-10-19 当前：统一网络包pre.2本地镜像

本地同步源为PulletNet的6e9591a，PulletFramework.NetClient固定160文件与开发源导出逐字节一致。原Discovery/Messaging及两个组合包已移入主包Modules，删除的是旧包安装入口，不是内部职责/测试；只保留com.xmjgame.pullet-framework.netclient一个UPM包。五asmdef、namespace、原GUID/DLL锁保持；统一携带Newtonsoft3.2.1。

下列五包安装表是旧布局的历史记录，不再是当前安装步骤。升级目标时用完整pre.2副本替换netclient，并移出旧四模块包和manifest/testables登记，不能让新旧同时存在。现行说明见[单包整合](Single-Package-Consolidation.zh-CN.md)和[安装清单](NetClient-Installation-and-Player-Checklist.zh-CN.md)。原生DLL仍原U8/U9，不混称最新29d923e候选。

旧包/说明备份在PulletNet本地artifacts/unity-single-package-sync-20261019/Before/Framework，用户未跟踪的对齐文档及其它模块保持。本批同步仅本地，不推送/发包；妈祖验证与安装结果按另行收据记录，不能仅凭复制就声称业务接入通过。

## 当前主分支整合

2026-10-18按用户确认，本地main已从0712e99快进到已验证的b687018，再归档本段说明；五包代码树和163个实际包文件保持与同步检查点一致，未做内容冲突合并或重编译。用户未提交的对齐文档保留，不纳入提交。下面“本地同步分支/未合入main/未推送”描述此前同步时点，不是另一套执行指令；远端状态以实际推送及main提交核对为准。合并不等于正式发布或妈祖/XR升级，后续业务迁移仍需单独安排。

## 当前：2026-10-18 五个可选包同步完成

唯一开发源为 PulletNet 的 UnityPackages；本次固定来源为其本地 main `be8864b`，包运行时代码和 DLL 与 U11 保存的五包快照一致。本仓库沿用既有本地 `codex/unity-single-connection` 分支，不新建分支、切换 main 或推送。包版本均为3.0.0-pre.1，不是正式发布；妈祖/XR与其他模块不变。

五包共163文件逐项核验源、固定导出和本仓库镜像 SHA256；raw从90文件变为73文件，旧内置Discovery/RPC/JSON及对应旧测试不叠加保留。它们已在开发源迁出或按合同退役，本次仅镜像同步，不另做测试删减。其余四包按需安装，不要求简单raw应用安装全部模块。

原始回退包及被替换的说明/收据保存在本机 PulletNet `artifacts/framework-five-package-sync-20261018/Before`；旧raw实际目录另保留在同处Retired。原Manager/Relay脚本、Prefab及包根目录GUID保持。旧包移出当前安装目录但未永久删除，不能只退回一份DLL代替完整回退。

### 按需安装

| 需求 | 同时复制到业务工程自身Packages内的包ID |
| --- | --- |
| 原始连接/收发 | com.xmjgame.pullet-framework.netclient |
| 独立发现 | com.xmjgame.pullet-framework.discovery（另需com.unity.nuget.newtonsoft-json 3.2.1） |
| Manager发现后连接 | raw + discovery + com.xmjgame.pullet-framework.netclient-discovery |
| 独立消息会话 | com.xmjgame.pullet-framework.messaging；应用提供IMessageCodec和Sender |
| 既有Runtime typed连接 | raw + messaging + com.xmjgame.pullet-framework.netclient-messaging |
| 独立发现后typed连接 | discovery + 上述typed三包；不用Manager发现组合 |

从本检出中复制完整固定包，不使用工程外绝对路径、软链接或自动跟随源码。先备份旧工程、包、manifest/lock和场景，再在隔离业务副本验证后切换；本次未执行任何业务安装。上游源码中的Tools命令相对PulletNet仓库，不是本仓库已经提供同名发布工具。

### 必须先处理的迁移

- [发现资产/API迁移](Discovery-U9B-Migration.zh-CN.md)：旧Manager自动发现配置会明确拒绝，Inspector/持久事件须人工核对后确认；GUID保留不等于旧方法仍可调用。
- [消息API/测试行为迁移](Messaging-U10-Migration.zh-CN.md)：旧ConfigureMessaging、Subscribe、CallAsync、CancelPendingMessages及默认JSON/属性推断已退役，不提供临时转发层。typed连接为单次来源，显式禁用自动重连，不支持透明恢复或UDP保留重绑。
- [安装/回退/平台清单](NetClient-Installation-and-Player-Checklist.zh-CN.md)：保留完整包和对应服务器输入，失败时成套回退，不靠删除Library掩盖兼容问题。

### 验证与准确输入

本次从同步后的Framework五包再取固定副本，在Unity2022.3.62f3隔离工程运行既有U11公开消费者：实际PlayMode16场景、22次真实响应/回显、主线程帧推进、真实关闭与Unity/对端退出均通过；不是只把复制成功或单个NUnit Runner绿色当成验收。无Player重编、原生全量、长压或新CI。

raw的Protocol/ClientSDK及独立Messaging DLL仍是2205c8d / CI39058109182 attempt1的准确输入；独立Discovery仍沿用自身NativeInput，不因为原生98eede1的新CI而替换DLL。[U11原PC验收](U11-PC-Acceptance.zh-CN.md)保留原输入，Windows Mono Player证据来自U11，并非本次重跑。两份原Player构建各一条已归属headless环境光Warning，不改写零警告。Android/PICO、IL2CPP/AOT、真实Protobuf、真实后台、跨机广播、弱网和容量仍待专门验收。

当前机器同步/运行收据见[五包收据](Evidence/netclient-modules-source-sync.json)。旧[单包收据](Evidence/netclient-source-sync.json)和以下记录保留各自历史身份，不覆盖为本次新结果。用户未提交的NetClient-3.0-Alignment文档保持原样，不作为当前开发指令。

复制来的迁移、安装和U11说明保留上游检查点当时的语境，其中“本批未同步/下一批”等文字不形成新的本地计划；本仓库当前状态以本节和五包收据为准。文件hash针对此次实际工作区和固定验证快照，后续Git检出可能转换文本换行，安装时仍应重新核对，不把提交号或相同版本字符串当成字节一致保证。

## 历史：2026-10-16 单包同步

2026-10-16。本地安装副本最新同步自 PulletNet 本地main的 `40bf84fb2bfe3d1a699ef099325f505aca362b2d`，来源路径 `UnityPackages/PulletFramework.NetClient`。旧临时工作区已合入主目录并移除，不再从PulletNet-Unity开发。

该来源是唯一开发源，本仓库同名目录只保存验证后的同步副本。妈祖等业务工程使用各自Packages内固定版本，不通过外部开发路径或自动同步追随源码；本次未升级妈祖的2.0.2或XR安装，也未推送/发布。

本次通过只读导出的90个包文件逐项SHA256与开发源/本地同步副本一致核对，GUID/DLL保留。准确NativeInput仍为2b046d5的RC1，两份DLL/hash不变。当前是3.0.0-pre.1开发检查点，不是正式发布。

此次同步补齐U6示例/实际PlayMode和U7 Windows独立Player验证消费者。U6有8场景/12回显；U7在2022.3.62f3 Windows x64 Mono Development/Low裁剪下7场景/10回显、显式Shutdown及双方exit0，构建0错误/1警告（详情未归属）。嵌入包无Test Framework时能编译，安装该框架的相反条件执行124项、零失败/跳过。原失败保留；不把复制包说成再次执行测试。OS真实后台、IL2CPP/PICO和其他组合待验，妈祖仍2.0.2。

机器同步收据见[netclient-source-sync.json](Evidence/netclient-source-sync.json)；U6/U7合同及原始失败见本目录对应检查点和Evidence。收据描述本机此刻导出/复制的字节；不同Git checkout的换行转换可能改变文本hash，之后安装仍应重新校验导出收据，不能只凭版本号或提交声称字节相同。

此前c0d8fe8的83文件同步和U5的135项/16场景20进程320次/20回显仍作为历史检查点，不冒称本批重新跑过所有竞态。

后续只读原始BuildReport已查明上述唯一警告：-nographics下无图形设备，无法生成场景环境光/反射探针；不是PulletNet网络代码故障，仍保留原1条警告，不重编求零。最新U7说明及Evidence/netclient-u7-warning-and-device-readiness.json记录原报告未改的hash和Unity读取退出码；90文件包体未改，不重复同步或测试。Android工具文件存在但adb无连接设备，尚非构建/真机验收。

当前合同见 [Manager/Shutdown](NetClient-U5-Manager-Contract.zh-CN.md)、[Runtime](NetClient-U3-Runtime-Contract.zh-CN.md)；完整验证和首轮失败见 [U5检查点](NetClient-U5-Manager-Checkpoint.zh-CN.md)及[机器记录](Evidence/netclient-u5-manager.json)。旧EventPump/BoundedPayloadQueue与12项测试随来源退役，保障映射保留在Manager合同，源码可从Git恢复。

原在Framework先行实现的b4f319f/355556a及更早文档仍按历史事实保留；其中旧的“Framework是开发源”描述不是当前维护指令。今后只在PulletNet UnityPackages开发、验证，再明确同步本仓库，不在两处独立演进。
