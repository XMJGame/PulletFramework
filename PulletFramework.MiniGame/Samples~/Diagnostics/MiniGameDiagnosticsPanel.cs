using System;
using System.Threading;
using System.Threading.Tasks;
using PulletFramework.MiniGame.Platform;
using UnityEngine;

namespace PulletFramework.MiniGame.Diagnostics
{
    [DisallowMultipleComponent]
    public sealed class MiniGameDiagnosticsPanel : MonoBehaviour
    {
        [SerializeField] private bool initializePlatform = true;
        [SerializeField] private string rewardedPlacement = "revive";
        [SerializeField] private string leaderboardBoardId = "default";
        [SerializeField] private int diagnosticScore = 1000;
        [SerializeField] private Rect panelRect = new Rect(24f, 24f, 620f, 500f);

        private string _status = "Waiting for platform initialization.";
        private bool _lifecycleAttached;
        private Vector2 _logScroll;
        private IPlatformLifecycleService _lifecycle;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private async void Start()
        {
            if (!initializePlatform)
                return;

            try
            {
                PlatformResult result = await MiniGameBootstrap.InitializeAsync(_lifetime.Token);
                SetStatus(result.Succeeded ? "Platform initialized." : "Initialize failed: " + result.Error);
            }
            catch (OperationCanceledException) { }
        }

        private void Update()
        {
            if (!_lifecycleAttached && PulletPlatform.IsInitialized
                && PulletPlatform.TryGet(out IPlatformLifecycleService lifecycle))
            {
                _lifecycle = lifecycle;
                _lifecycle.Shown += OnPlatformShown;
                _lifecycle.Hidden += OnPlatformHidden;
                _lifecycleAttached = true;
            }
        }

        private void OnDisable()
        {
            if (_lifecycle == null)
                return;

            _lifecycle.Shown -= OnPlatformShown;
            _lifecycle.Hidden -= OnPlatformHidden;
            _lifecycle = null;
            _lifecycleAttached = false;
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        private void OnGUI()
        {
            const float margin = 12f;
            panelRect.width = Mathf.Min(620f, Mathf.Max(280f, Screen.width - margin * 2f));
            panelRect.height = Mathf.Min(620f, Mathf.Max(420f, Screen.height - margin * 2f));
            panelRect.x = Mathf.Clamp(panelRect.x, margin, Mathf.Max(margin, Screen.width - panelRect.width - margin));
            panelRect.y = Mathf.Clamp(panelRect.y, margin, Mathf.Max(margin, Screen.height - panelRect.height - margin));
            panelRect = GUILayout.Window(GetInstanceID(), panelRect, DrawWindow, "Pullet Mini Game Diagnostics");
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.Label($"Platform: {PulletPlatform.Id}");
            GUILayout.Label($"Initialized: {PulletPlatform.IsInitialized}");
            GUILayout.Label($"Capabilities: {PulletPlatform.Capabilities}");

            if (_lifecycle != null)
            {
                PlatformLaunchContext context = _lifecycle.LastLaunchContext;
                GUILayout.Label($"Launch: scene={context.Scene}, from={context.LaunchFrom}, location={context.Location}");
            }

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Login", GUILayout.Height(34f))) Login();
            if (GUILayout.Button("Share", GUILayout.Height(34f))) Share();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load Ad", GUILayout.Height(34f))) LoadAd();
            if (GUILayout.Button("Show Ad", GUILayout.Height(34f))) ShowAd();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Check Sidebar", GUILayout.Height(34f))) CheckSidebar();
            if (GUILayout.Button("Open Sidebar", GUILayout.Height(34f))) OpenSidebar();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Check Daily Claim", GUILayout.Height(34f))) CheckDailyClaim();
            if (GUILayout.Button("Mark Claimed", GUILayout.Height(34f))) MarkClaimed();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Submit Score", GUILayout.Height(34f))) SubmitScore();
            if (GUILayout.Button("Query Ranks", GUILayout.Height(34f))) QueryRanks();
            if (GUILayout.Button("Native Board", GUILayout.Height(34f))) OpenNativeLeaderboard();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            _logScroll = GUILayout.BeginScrollView(_logScroll, GUI.skin.box, GUILayout.ExpandHeight(true));
            GUILayout.Label(_status, GUI.skin.label);
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, panelRect.width, 28f));
        }

        private void Login()
        {
            if (!TryService(out IPlayerService service, "Login")) return;
            SetStatus("Login pending...");
            Execute(async token =>
            {
                PlatformResult<PlatformLoginCredential> result = await service.LoginAsync(token);
                return result.Succeeded
                    ? $"Login OK\nprovider={result.Value.Provider}\ncode={result.Value.Code}\nanonymous={result.Value.AnonymousCode}"
                    : "Login failed: " + result.Error;
            });
        }

        private void Share()
        {
            if (!TryService(out IShareService service, "Share")) return;
            SetStatus("Share pending...");
            Execute(async token =>
            {
                PlatformResult result = await service.ShareAsync(default, token);
                return result.Succeeded ? "Share request OK." : "Share failed: " + result.Error;
            });
        }

        private void LoadAd()
        {
            if (!TryService(out IRewardedAdService service, "Rewarded ad")) return;
            SetStatus($"Loading ad '{rewardedPlacement}'...");
            Execute(async token =>
            {
                PlatformResult result = await service.LoadAsync(rewardedPlacement, token);
                return result.Succeeded ? $"Ad '{rewardedPlacement}' loaded." : "Ad load failed: " + result.Error;
            });
        }

        private void ShowAd()
        {
            if (!TryService(out IRewardedAdService service, "Rewarded ad")) return;
            SetStatus($"Showing ad '{rewardedPlacement}'...");
            Execute(async token =>
            {
                RewardedAdResult result = await service.ShowAsync(rewardedPlacement, token);
                return $"Ad result: {result.Status}\nGrant reward: {result.ShouldGrantReward}\nError: {result.Error}";
            });
        }

        private void CheckSidebar()
        {
            if (!TryService(out ISidebarRevisitService service, "Sidebar")) return;
            SetStatus("Checking sidebar support...");
            Execute(async token =>
            {
                PlatformResult<bool> result = await service.CheckAvailableAsync(token);
                return result.Succeeded
                    ? $"Sidebar supported: {result.Value}\nReturned from sidebar: {service.EnteredFromSidebar}"
                    : "Sidebar check failed: " + result.Error;
            });
        }

        private void OpenSidebar()
        {
            if (!TryService(out ISidebarRevisitService service, "Sidebar")) return;
            MiniGameRuntimeSettings settings = MiniGameRuntimeSettings.Load(PulletPlatform.Id);
            SetStatus("Opening sidebar...");
            Execute(async token =>
            {
                PlatformResult result = await service.NavigateToSidebarAsync(settings?.sidebarActivityId, token);
                return result.Succeeded
                    ? "Sidebar opened. Return through its game card, then inspect the onShow context."
                    : "Open sidebar failed: " + result.Error;
            });
        }

        private void CheckDailyClaim()
        {
            if (!TryCreateSidebarTask(out SidebarRevisitTask task)) return;
            string period = SidebarRevisitTask.ChinaDailyPeriodKey(DateTimeOffset.UtcNow);
            SetStatus($"Daily period: {period}\nEligible return: {task.CanClaim(period)}");
        }

        private void MarkClaimed()
        {
            if (!TryCreateSidebarTask(out SidebarRevisitTask task)) return;
            string period = SidebarRevisitTask.ChinaDailyPeriodKey(DateTimeOffset.UtcNow);
            bool marked = task.TryMarkClaimed(period);
            SetStatus(marked
                ? $"Marked '{period}' claimed for diagnostics."
                : $"Cannot claim '{period}'. Return from the sidebar or clear platform storage first.");
        }

        private void SubmitScore()
        {
            if (!TryService(out ILeaderboardScoreService service, "Leaderboard submission")) return;
            SetStatus($"Submitting {diagnosticScore} to '{leaderboardBoardId}'...");
            Execute(async token =>
            {
                PlatformResult result = await service.SubmitScoreAsync(
                    new LeaderboardScore(leaderboardBoardId, diagnosticScore), token);
                return result.Succeeded ? "Score submitted." : "Score submission failed: " + result.Error;
            });
        }

        private void QueryRanks()
        {
            if (!TryService(out ILeaderboardQueryService service, "Leaderboard query")) return;
            SetStatus($"Querying '{leaderboardBoardId}'...");
            Execute(async token =>
            {
                PlatformResult<LeaderboardPage> result = await service.GetRanksAsync(
                    new LeaderboardQuery(leaderboardBoardId), token);
                if (!result.Succeeded) return "Rank query failed: " + result.Error;

                LeaderboardPage page = result.Value;
                string lines = $"Ranks: {page.Entries.Length}/{page.TotalCount}";
                foreach (LeaderboardEntry entry in page.Entries)
                    lines += $"\n#{entry.Rank} {entry.User.Nickname}: {entry.Value}";
                return lines;
            });
        }

        private void OpenNativeLeaderboard()
        {
            if (!TryService(out ILeaderboardViewService service, "Native leaderboard")) return;
            SetStatus($"Opening native leaderboard '{leaderboardBoardId}'...");
            Execute(async token =>
            {
                PlatformResult result = await service.OpenLeaderboardAsync(
                    new LeaderboardViewRequest(new LeaderboardQuery(leaderboardBoardId)), token);
                return result.Succeeded ? "Native leaderboard opened." : "Open leaderboard failed: " + result.Error;
            });
        }

        private bool TryCreateSidebarTask(out SidebarRevisitTask task)
        {
            task = null;
            if (!TryService(out ISidebarRevisitService sidebar, "Sidebar")) return false;
            if (!TryService(out IPlatformStorageService storage, "Storage")) return false;
            task = new SidebarRevisitTask(sidebar, storage);
            return true;
        }

        private bool TryService<T>(out T service, string displayName) where T : class
        {
            if (PulletPlatform.TryGet(out service)) return true;
            SetStatus(displayName + " is unavailable on the current platform.");
            return false;
        }

        private void OnPlatformShown(PlatformLaunchContext context)
        {
            SetStatus($"onShow\nscene={context.Scene}\nlaunch_from={context.LaunchFrom}\nlocation={context.Location}");
        }

        private void OnPlatformHidden() => SetStatus("onHide");

        private async void Execute(Func<CancellationToken, Task<string>> request)
        {
            try { SetStatus(await request(_lifetime.Token)); }
            catch (OperationCanceledException) { SetStatus("Request cancelled."); }
            catch (Exception exception) { SetStatus("Unexpected error: " + exception.Message); }
        }

        private void SetStatus(string value)
        {
            _status = $"[{DateTime.Now:HH:mm:ss}] {value}";
            PulletFramework.PLogger.DebugLog("[PulletFramework.MiniGame Diagnostics] " + value);
        }
    }
}
