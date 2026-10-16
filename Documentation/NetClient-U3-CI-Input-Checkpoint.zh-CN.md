# U3/U4：准确CI输入与完整Runtime交付检查点

2026-10-16，Framework基线f19ec20，延续[Runtime合同](NetClient-U3-Runtime-Contract.zh-CN.md)与[本地检查点](NetClient-U3-Runtime-Local-Checkpoint.zh-CN.md)，不另开架构路线。

## 输入选择

使用Native准确提交 `2b046d5e55efb04c768d4b7127c3a6fdbc548c22`、[Windows CI38887773146](https://github.com/XMJGame/PulletNet/actions/runs/38887773146) attempt1实际上传的RC1四包。其3004项、四包15步及既定7项20轮通过；包及包内DLL hash、nuspec source、2019 API已核验。选择已有CI产物，不重新打包或升号，也不借旧fa0145c CI认证新SDK。NativeInput保留source/run/attempt/platform/package/DLL hash，明确不是全平台或发布验收。

Unity只导入Protocol netstandard2.0和ClientSDK netstandard2.1 DLL；独立.NET8 echo fixture使用同次包的NetCore/Protocol。没有把NetCore/Hosting DLL导入Unity。包装层版本保持3.0.0-pre.1，没有发布或正式消费者安装。

## 完整职责及验证

PulletClientRuntime、公开合同、25项测试与ClientDispatchQueue接入一起交付。SDK NetClientHost仍创建/移除/Dispose实际客户端；Runtime只拥有来源、派发代际、有界Payload/生命周期交付、固定故障槽和自身关闭完成。无新增RPC/业务MessageId、第二套Payload队列或连接状态权威，旧Manager/Prefab/Inspector未迁移。

这次没有改变已验证的Runtime行为。原检查点七份源码/元数据hash全部相同；从旧RC2和本次CI包提取的SDK/Protocol，按有序类型/字段/方法签名、IL、局部变量及异常表比较零差异（45985/5239行）。程序集属性/MVID/PDB不在比较内，DLL hash确实不同；原12场景20进程240次仅作为相同实现的行为证据复用，不标成新包重复20次。

新证据目录 `G:/XuMingJun/2026/PICO GPS XR/PulletNet/artifacts/unity-u3-ci-input-2b046d5`，新项目独立导入准确CI DLL；Unity2022.3.62f3 / Windows Editor EditMode **121/121、0失败/跳过、exit0**，含旧包61、派发内核24、Runtime25及真实连接11。SDK三传输/取消/保留重绑及Runtime双源移除/同名替代/可靠溢出由真实echo场景覆盖。fixture Release零warning/error，20次回显、发送/清理失败为空，真实Stop/Dispose exit0。

通过后更新Framework开发NativeInput及两份DLL，44份原.meta hash/GUID保持不变；旧NativeInput/DLL/meta备份于新证据目录previous-input，Git原输入也可追溯。最终源码/DLL/锁与已验隔离包逐项核对，完整Runtime同批提交，不再留下引用未交付实现的半成品测试。

[机器收据](Evidence/netclient-u3-ci-input.json)保存准确输入、实现比较、源码/meta hash、Unity命令/XML/退出码及fixture结果。旧U1未提交核对文档不混入本批，不覆盖旧RC2或启动失败原件。

## 停止及剩余边界

U3/U4薄Runtime的开发职责与准确输入门槛在本检查点收口；这不是Unity包装整体完成。后续U5/U6才处理旧门面、MonoBehaviour和应用退出owner，U7 Player/Android IL2CPP/PICO另行验收；WebGL/KCP延期。旧Editor启动AV及Native七项历史现场原因不追溯关闭。

本批没有新Native代码/全量/CI/长压，没有推送Framework或切换妈祖/XR、Native旧Unity镜像、公开发布及其他消费者。后续实现和推送按用户安排，不因新开发包可用而自动安装到业务工程。
