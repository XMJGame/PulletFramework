# U11：Unity当前开发源的PC有限验收

2026-10-18。输入为9779ee7的五个Unity开发包；本批只新增公开验收消费者/工具和记录，
没有修改网络运行时代码、Native源码、DLL、协议、Prefab或已安装下游。
唯一主计划仍在Architecture/PulletNet-3.0-Plan.md。
五包README在运行后更新为当前支持状态；收据逐项区分文档变化与原测试输入，
全部非README包文件与测试快照一致，当前五包导出不冒充每个README字节都参与过编译。

## 实际结果

Unity2022.3.62f3 / Windows，StandaloneWindows64、Mono、Development、Low裁剪、headless。
机器收据见[Evidence/unity-u11-pc-acceptance.json](Evidence/unity-u11-pc-acceptance.json)，
原始件及完整隔离工程/可执行产物在artifacts/unity-u11-20261018。

| 输入 | 实际执行 | 完成场景 | 真实响应/回显 | 退出 |
| --- | --- | --- | --- | --- |
| raw-play-01 | 既有8个用例真实EnterPlayMode，只有raw包及Test Framework | 8 | 12 | Unity/服务均0 |
| modules-play-02 | 1个Runner执行共享消费者，实际Unity Update，不是手调Update | 16 | 22 | Unity/服务均0 |
| raw-player-01 | 无Messaging/Discovery/Json.NET/Test Framework的最小EXE | 7 | 10 | Player/服务均0，Quit已join |
| modules-player-02 | 五包共存、无Test Framework的EXE，同一公开消费者 | 16 | 22 | Player/服务均0，Quit已join |

不是把1个Runner绿色当成16场景通过：16份独立完成收据、Report、主线程、帧推进、
服务端精确22次响应/回显、原生Stop/Dispose与进程退出都一致才签收。
两种Player都实际构建成功、零构建错误，各一条引擎构建警告，详情见下。

共享消费者只使用公开API，支持四类场景各三传输UDP/TCP/WebSocket：

- raw Manager连接和借用Payload；关闭完成后才卸载场景。
- 可选DiscoveryConnector真实Awake/Update发现、选择准确端点，再经raw连接。
- 可选typed绑定实际RPC响应不进入通知，通知借用主线程交付，消息/Runtime最后无资源积压。
- 独立DiscoveryRuntime取得端点后，应用显式创建typed绑定，不把raw自动恢复策略强接到typed来源。

另覆盖typed通知中请求关闭且不等待自身、抑制后续typed/raw回调；
在途UDP发现的Close真实join；Runtime关闭后pending RPC退役而不等待业务期限；
同Runtime两个准确来源各自RpcId1互不匹配错误，关闭一个不影响另一条RPC。
原raw应用验收复用场景卸载/真实Destroy、持久对象、重复Manager、Relay启停/销毁、
握手中销毁、公开Sample及注入Pause/Focus的既有场景，没有重写另一套测试内核。

## 原始失败与警告

modules-play-01原Runtime消费者16场景/22回显通过；首次modules-player-01在执行Build方法前，
验收代码Editor/Tests程序集报CS0246 NUnit/UnityTest缺失。没有开始网络场景或构建出Player。
确证原因是测试asmdef只检查UNITY_INCLUDE_TESTS，未检查实际Test Framework包是否存在；
修正验收程序集的versionDefines/defineConstraints，未给最小Player补测试依赖。
modules-player-02成功；modules-play-02以修正后的条件再验一次，Runtime消费者及UPM代码未改。
旧工程、命令、exit1和完整日志保留，不按“重跑成功”删除原始问题。

两份成功Player原始Library/LastBuild.buildreport经独立只读工程解码，
SHA256前后相同，没有重构建Player。各唯一Warning均为：
`GfxDevice renderer is null`，说明-nographics无法更新场景Ambient Probe/Reflection Probe。
步骤分别是Building scene Assets/PlayerAcceptance.unity和Assets/ModuleAcceptance.unity。
这是headless图形构建提示，不是脚本/网络故障；不回写为零warning、不隐藏原日志，也不认证画面质量。

本批没有Native全量/新CI/重复20轮/长压；仅新增有限消费者验收，未引入运行时并发owner。
此前Native/Unity EditMode和B1竞态证据保持其准确输入，不改写成此次所有平台/Player均已验。

## 当前支持矩阵与停止边界

| 能力/环境 | 本轮结论 |
| --- | --- |
| Unity2022.3.62f3 Windows Editor实际PlayMode | raw、发现、typed、组合有限场景通过 |
| 同版本Windows x64 Mono Development Low裁剪 | 最小raw与五包组合EXE实际收发、场景关闭、Quit/进程退出通过 |
| UDP、TCP、ws，受控本机回环 | 通过；不是业务容量SLO、弱网长稳或互联网验收 |
| 原生源码/协议与DLL | 不变；沿用既有准确Native输入证据 |
| Unity2021.3最低清单版本、其他Unity版本 | 未实测，不因manifest最低版本自动签收 |
| Android IL2CPP/PICO、Unity Linux/macOS Player | 未验，后续另行协作 |
| 实际Protobuf codec、AOT裁剪、wss/TLS、真实OS后台/休眠 | 未由本批覆盖 |
| 跨机器广播、NAT、Unity端弱网/容量/长期运行 | 未由受控发现/回环收发证明 |
| typed透明重连、UDP保留重绑 | 本版明确不支持，不由PC通过扩张其合同 |

U8—U11这轮Unity开发源与PC有限验收到此收口，不因此宣称所有环境或正式发布完成。
不自动推送、发布、同步PulletFramework/妈祖或修改业务；Android/PICO继续待验，
后续阶段由用户安排，不再自发增加包装层、并发模块或新的全量CI循环。
