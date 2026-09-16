# Platform Diagnostics

Add `MiniGameDiagnosticsPanel` to an object in a dedicated development scene. The panel can initialize
the selected platform and exercise login, sharing, rewarded ads, lifecycle callbacks, and Douyin sidebar
revisit behavior.

Keep this sample out of production scenes. `Mark Claimed` only writes the framework's local diagnostics
claim marker; it does not grant a gameplay reward or replace server validation.
