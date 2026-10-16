# U5-A：持续恢复策略的执行与退出所有权

2026-10-16。基线为已提交的U3/U4 Runtime检查点 `b4f319f`，本批是旧Manager迁移的有限前置，不是新的网络架构、SDK重连器或独立监督层。

## 确证问题

原 `PulletActiveServerRecovery.Start` 先设置running/CTS，再执行启动日志，最后才启动被丢弃的RunAsync。若日志抛异常，running永久为true，没有执行任务负责退出；Dispose只Cancel，无法补上该终态。隔离Unity的 `original-owner-probe` 用未修改的原策略和一项受控日志反例，1执行/1失败、exit2，原XML保留；不是靠等待偶发CI失败推断。

正常运行也没有可供上层加入关闭的Task：非协作Connect收到取消不等于退出，旧OnDestroy无法证明策略已停止。Run异常只再打一条日志，诊断失败可让原因与终态一同丢失。

## 本批合同

只由原策略类持有每轮执行和一个永久关闭任务，不新增Lifecycle/任务协调模块，也不替SDK连接状态做仲裁。Start在同一gate中发布执行owner、取消源、intent代际和Completion，才允许日志/应用委托运行；并发Cancel/Stop不会落在“running已设置、owner尚未发布”的窗口。

- `Completion`：最近一次已接纳执行的真实终态；日志/策略委托的未处理异常可观察。非成功ConnectionResult仍按原策略进行有限间隔尝试，不自动当成Runtime fatal。
- `Cancel`：失效当前intent并请求取消，可在原执行真实退出后再Start；原执行未退，不接纳新轮。旧结果不能触发已失效目标的失败清理或下一次连接。
- `StopAsync`：永久关闭接纳并返回同一共享任务，取消和等待真实delay/Connect退出。调用者token只取消自己的等待，不取消该清理事实。预取消调用不启动关闭；一个取消回调抛异常也不能跳过原执行的join。
- 同步`Dispose`：请求永久关闭，不同步阻塞回调，不表示退出完成。`StopAsync`或`DisposeAsync`才是可等待的退出收据。应用不得在需要自己退出的回调中同步等待。
- 正常取消只识别本owner已取消且携带本token的OCE；非本方OCE不能借Cancel被吞为成功。错误原件和诊断错误保留在完成任务中；失败执行不能无声被下一轮Start覆盖。

等待间隔的默认仍是原Task.Delay和最短0.5秒，测试只注入可控delay屏障，不改生产频率或放宽观察期限。Run继续使用原调用方SynchronizationContext，不悄悄将Unity应用委托移到后台线程；关闭等待本身不需要调用Runtime.Update，但应用必须保持其原调度器可前进，不能在Unity主线程同步堵住join。后续Manager退出要显式组合策略取消、网络中断和真实join，不能只增加一个Stop调用就声称整个应用退出已收口。

## 验证与剩余边界

本批受影响策略及测试在独立Unity2022.3.62f3 / Windows Editor项目验证；准确NativeInput仍为2b046d5 RC1 / CI38887773146，不重打包、不改DLL或GUID。原3项策略用例保留，清理改为等待真实退出；原先取消前置用例不再用0.6秒睡眠冒充完成，而等待准确Completion。

新增启动诊断失败、非协作Connect/取消单等待者、Cancel后重启、attempt回调重入Stop、非协作delay关闭、外来OCE、取消回调抛异常、预取消Stop/Dispose及并发Start/Stop。具体运行次数、原XML/进程退出码、包检查点与源码hash见[机器收据](Evidence/netclient-u5-recovery-owner.json)。旧失败不覆盖，新失败即停止，不无限重跑。

最终策略12/12；9个新关键场景20个独立Unity进程，180/180、零跳过，全部exit0。最后完整包检查点130/130（在上个121项基础上增加9项），同准确CI输入的真实fixture20次回显、failures/sendFailures为空，Stop/Dispose exit0。重复的过滤器始终只选择这9项，策略源码不变；验证专用echo测试副本的准备不计为重复场景，完整包准确源码另由最终检查点记录。未运行新的原生全量、CI或长压。

**未迁移Manager、发现入口或旧EventPump**，它们仍使用旧owner/动作队列；不把本类新增可等待能力说成旧OnDestroy已经等待了它。下一批是default来源门面和整个Shutdown的真实组合，随后再验Pause/Resume/Focus/Quit/场景退出；Player/IL2CPP/PICO独立待验。U3/U4 Runtime、妈祖/XR安装、Native旧Unity镜像、Messaging/Discovery拆包、Native生产代码均不动，不推送或发布。
