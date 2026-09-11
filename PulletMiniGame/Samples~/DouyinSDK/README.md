# Douyin SDK Bridge

Validated against TTSDK 6.9.3. Install TTSDK using BGDT before importing this sample.
Apply the Douyin platform in Pullets/Mini Game/Build and wait for compilation.
The sample registers before scene load. Await `MiniGameBootstrap.InitializeAsync(cancellationToken)`
from the first scene.

Configure share defaults and business ad placements in the build window's runtime settings.
Load("revive") before Show("revive"). Only ShouldGrantReward grants a reward.
Login returns a temporary code and anonymousCode for exchange by your server.
Shutdown removes lifecycle/ad handlers and completes pending login/share/ad callbacks.

Compilation and factory/configuration tests passed; real device SDK behavior remains to be validated.

The bridge also implements the required sidebar revisit flow with TT.CheckScene and
TT.NavigateToScene. A valid return requires launch_from=homepage and location=sidebar_card.
Use SidebarRevisitTask only as local UI/offline state; validate valuable rewards on a server.
