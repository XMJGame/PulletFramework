# U5 Manager：本地检查点与维护源纠正

2026-10-16。开发分支 `codex/unity-runtime-manager`，Native基线d752e33，迁入Framework已提交检查点355556a的完整包和证据，再完成Manager职责迁移。源码在PulletNet的UnityPackages；Framework只在验证后同步，妈祖仍是自身Packages内的2.0.2，不依赖任一开发目录。

## 本批结果

- Manager仅借用Runtime创建的default来源，移除/释放归SDK Host；无直接new/dispose客户端或第二套Payload队列。
- 新增共享、不可逆、单等待者可取消的ShutdownAsync，等待已接纳应用操作、Recovery、发现IO、Runtime与当前回调，再释放Messaging。
- Pause/Focus采用组合应用意图，复用端点命令gate；OnDestroy/Quit只请求关闭，不伪装成Unity可等待的退出屏障。
- 发现流程持有独立轮次，不误停静态发现；失败清理真正await；可靠交付终态进入失败Shutdown，不自动重连掩盖状态缺口。
- 已退役2个内部旧队列及12项旧测试，对应保障逐项映射；新增17项，另迁移原Manager生命周期测试。旧文件和原始失败可从Git/归档恢复，不因删旧机制删除证据。

详细行为及兼容变化见 [当前合同](NetClient-U5-Manager-Contract.zh-CN.md)。原始根目录为本机 `PulletNet-Unity/artifacts/unity-manager-20261016`；[机读记录](Evidence/netclient-u5-manager.json)含输入锁、源码hash、每轮退出码及失败清单。

## 验证

初轮定向48项，47通过/1失败：发现广播Send期间本方取消释放socket，原包装层误报普通ObjectDisposedException。修正发现owner的取消分类和真实Receive退出，扩展定向54/54通过，未放宽期限。

新包检查点首次exit1，原因是本批新增测试缺Messaging命名空间导入，没有执行测试；第二次135项中134通过，新增日志测试的预期格式与Unity解包AggregateException首因格式不符。补齐导入并修正诊断断言后，最终 `package-checkpoint-03` **135/135、零跳过、exit0**。三个原始失败的日志/执行参数以及已运行测试产生的XML均保留，不称首次全绿或环境问题。

最终包包含11项真实连接场景：UDP/TCP/WebSocket默认Manager，Runtime多来源隔离/重用、可靠溢出、SDK UDP清理/重绑、握手取消与超时退出。准确2b046d5输入的服务fixture完成20次回显，failures/sendFailures为空，真实Stop/Dispose、exit0。

16项新关闭/重入/取消场景，各20个独立Unity进程，总**320/320**、零跳过、全部exit0。每轮检查C#实现及测试输入hash未变，首次失败即停。未重复旧Runtime240次或Recovery180次，不拼接成同一次运行。测试开始后工作源码仅调整README说明，全部C#/DLL/Prefab/NativeInput与被测候选一致。

Native Protocol/NetCore/ClientSDK/Hosting未改；DLL hash仍匹配准确NativeInput。原44份Framework.meta中4份随退役文件删除，剩40份逐字节不变，另为新增Manager测试添加独立GUID。保留框架MonoBehaviour/Prefab/DLL原GUID；不把Native旧2.x整包升级说成只有文案变化。

## 停止边界和下一步

本批仅本地验证/提交及已验证包同步；不推送、不发包、不做新远端CI、不更新妈祖/XR、不重跑Native全量/长压，不追溯关闭历史AV。当前仍是3.0.0-pre.1开发包，尚非正式发布。

下一有限任务是U6应用接入验收：公开示例/Inspector Relay、真实PlayMode场景切换与组合Pause/Focus的应用owner退出、安装升级/回退入口及Player操作清单。Editor里直接调用Hook的单元测试不是Unity场景/Player生命周期实测；Android IL2CPP/PICO、移动系统休眠/进程退出及其他平台仍待协作验收，不继续重开Native架构审计。
