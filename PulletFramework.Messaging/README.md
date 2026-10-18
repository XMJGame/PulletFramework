# PulletFramework.Messaging

可选Unity线程消息会话，开发版本3.0.0-pre.1。
包ID com.xmjgame.pullet-framework.messaging，namespace PulletFramework.Messaging。
U10-A只提供MessageSession，不创建/接管网络客户端、不拥有Socket、不引用NetClient或UnityEngine，
不增加Payload队列，不重写RPC计时、额度或订阅算法。

NativeInput锁定2205c8d / CI39058109182 attempt1的PulletNet.Messaging netstandard2.1实际DLL。
只包含这一份无外部依赖的DLL；不包含Messaging.ClientSDK（它自己拥有NetClient）或默认JSON/Protobuf库。

## 使用边界

由应用在Unity主线程构造MessageSession，明确传入MessageProtocolOptions、IMessageCodec和MessageSender。
Sender必须绑定准确来源的可靠有序lane，接收完整借用Payload并转译本地接纳结果；
应传递取消、等待真实发送退出，不能将旧Session的发送路由到“当前客户端”。
应用提供的codec不得保留输入/输出内存，编码值不是编解码实现本身。
此包不替应用选择Magic、业务ID、认证方式或序列化库。

CallAsync、SendNotificationAsync、SendResponseAsync、Subscribe和Receive在构造线程调用。
Receive(source, payload)同步借用，不复制Body；响应和通知只进入各自Native owner。
MessageInputResult.Kind标识路由/入口拒绝，具体是否处理成功必须检查Response或Notification结果；
路由为Response/Notification本身不表示已接纳，更不代表远端业务处理。
Request帧明确拒绝为UnexpectedKind，本层没有业务请求handler。

Source是本地不可混淆的消息来源对象，不是线协议身份，也不是鉴权证明。
必须在输入被应用接纳时捕获准确来源，不能消费时改贴成“当前Source”。
NewSession才新建本对象；PreservedSession不能重置registry/RpcId，否则迟到响应可能撞上新调用。
它不自动观察网络状态、不承诺透明重连/重绑；这些归U10-B显式绑定验收。

## 额度与关闭

RpcSnapshot/NotificationSnapshot直接暴露Native owner的只读事实。
单个调用超时或取消可以先于物理发送完成，准备/发送/字节预算保留到真实退出；
本地Accepted不等于远端ACK、响应或业务已处理。

RequestClose可以在codec/通知/诊断回调里请求；CloseAsync返回共享Completion，
等待RPC和通知真正退出，不需要Update推进。不能在回调里同步等待或await自己的退出。
预取消Close不开始关闭；一个等待者取消不取消共享清理。
Completion不包含外部网络IO处置：若发送不响应取消，网络owner可能必须先停旧IO才能解除等待。
不得等消息完成后才停止阻塞它的IO，也不得提前建立新Native Session。

## 可选网络绑定与迁移

可选com.xmjgame.pullet-framework.netclient-messaging把本会话接到既有Runtime的准确客户端来源，
不接管Manager的默认来源或自动恢复。每条绑定只对应一次Connect/NewSession，具体关闭合同见该包README。
旧Manager RPC/JSON已经退役，raw不再需要JSON依赖；旧接口与测试去向见开发仓库
Docs/Unity/Messaging-U10-Migration.zh-CN.md。属性推断与默认JSON明确不再作为框架能力。

本包与可选网络绑定已通过隔离Unity2022.3.62f3 Windows实际PlayMode及x64 Mono Development
Low裁剪Player的有限收发/生命周期验收；详情见开发仓库Docs/Unity/U11-PC-Acceptance.zh-CN.md。
不认证Unity2021.3、Android IL2CPP/PICO、跨平台或真实Protobuf实现。
测试的4字节标量codec是测试夹具，不是Protobuf编解码器。
完整证据见开发仓库Docs/Unity，不同步Framework或业务工程。
