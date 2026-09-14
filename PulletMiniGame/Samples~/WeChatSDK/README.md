# WeChat SDK Bridge

Install the official WeChat mini-game SDK first, then import this sample from Package Manager.
Select WeChat and click `应用平台` in `Pullets/Mini Game/Build` to enable its assembly.

The registration also installs `WeChatSafeAreaProvider`. It converts the platform window's
logical-pixel, top-left-origin safe area to Unity screen pixels. Devices that do not return
`safeArea` fall back to `Screen.safeArea`; the top-right menu capsule is not part of this layout.

```csharp
PlatformResult result = await MiniGameBootstrap.InitializeAsync(cancellationToken);
```

`Login` returns a temporary WeChat code. Send it to the game server and exchange it for the
game's own account/session; do not treat the code as a persistent user id.

The factory registers before scene load. Initialize from your first scene's Awake or later.
Editor uses the simulator; a WeChat player uses this bridge and waits for WX.InitSDK.
For explicit injection use `await PulletMiniGames.InitializeAsync(new WeChatPlatformAdapter(
new WeChatSdkBridge()), cancellationToken)`.

Load the actual ad unit id before Show. Reward only when ShouldGrantReward is true.
Share success only means the request was issued; it does not confirm a completed share.
