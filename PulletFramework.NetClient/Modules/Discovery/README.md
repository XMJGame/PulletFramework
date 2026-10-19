# PulletFramework.Discovery

Optional module inside the unified NetClient UPM package; do not install separately.
Assembly/namespace remain PulletFramework.Discovery; see the root README for migration.
Unity main-thread handoff for the independent PulletNet.Discovery module.
It does not reference NetClient, NetCore, Hosting, Messaging or PulletFramework.NetClient.
Json.NET is supplied by com.unity.nuget.newtonsoft-json. WebGL is excluded.
The package is a development checkpoint, not a published or device-certified release.

```csharp
// Construct on the Unity thread; call Update from the application's frame owner.
var discovery = new PulletFramework.Discovery.DiscoveryRuntime();
discovery.Completed += outcome => ShowOnUnityThread(outcome);
var result = await discovery.DiscoverAsync(new PulletNet.Discovery.DiscoveryOptions());
// result is an immutable snapshot; IO has exited, but queued UI facts may remain.
// In Update: discovery.Update();
// Before owner destruction, outside a callback:
await discovery.CloseAsync();
```

One active or undelivered round per runtime. A new round is rejected until the
previous terminal fact is pumped, so an application that stops pumping cannot
accumulate arbitrary result snapshots. Started, each native attempt and Completed
are delivered in order with a per-Update limit; the frame owner calls Update once
per Unity frame. RoundId is local to this
runtime, not a wire identity or globally unique ID. There is no static singleton.

The native owner provides sockets, discovery-v1 codec, deadlines, datagram/result/
retained-string limits, source-address fallback and the join of outstanding IO.
The adapter keeps at most 34 control notices (start + up to 32 attempts + terminal),
not another network Payload queue. DTOs are converted once to immutable UI views;
strings are shared, the native RetainedBytes metric is not total managed capacity
or RSS, and caller-retained snapshots are outside runtime queue accounting.

StopAsync cancels and joins the current native task without requiring Update;
its UI facts remain queued. CloseAsync permanently closes admission, discards
undelivered facts and joins native work and already-entered callbacks. Native
failures remain observable, cancellation does not stand in for physical exit.
Close is a stable shared Task. Callbacks may request Close, but must never block
or synchronously wait for their own exit. No new round may be started from a
terminal callback before it has returned. Discover and Update require the thread
that constructed the runtime; Close/Stop can be requested from other threads.

The operation Task preserves native cancellation/failure and completes without
frame pumping. Completed is a separate historical UI fact with Result, Error or
IsCanceled. Subscriber failures are isolated, counted and optionally sent to
CallbackError; a failing diagnostic does not abort cleanup. Invalid options are
rejected by the native module, not by a duplicated Unity validation algorithm.

NativeInput locks the actual 3.0.0 DLL from clean source 2f0cfc3, aligned with the
other native inputs. Discovery-v1 itself is unchanged. This local package gate
does not claim new remote CI or device acceptance; see Docs/Release-3.0.0.md.
Unity2021.3 in package.json is a declared floor, not a tested-platform statement.

U9-B has retired the legacy NetClient discovery loop/APIs. Raw NetClient can be
used without constructing this module; NetClient.Discovery owns selection policy.
See Docs/Unity/Discovery-U9B-Migration.zh-CN.md in the development repository.
U11 validates controlled IPv4 loopback discovery, raw/typed composition and
explicit cleanup in Unity2022.3.62f3 Windows PlayMode and x64 Mono Development
Low-stripping Player. This is not cross-machine broadcast/NAT, Android/PICO,
OS-background or arbitrary-platform acceptance. No downstream install is automatic.
