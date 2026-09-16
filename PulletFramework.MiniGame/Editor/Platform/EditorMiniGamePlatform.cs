using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;
using PulletFramework.MiniGame.Platform;
using UnityEngine;

namespace PulletFramework.MiniGame.Editor
{
    public sealed class EditorMiniGamePlatform : IPlatformAdapter, IPlayerService,
        IRewardedAdService, IShareService, IPlatformStorageService, IAnalyticsService,
        IPlatformLifecycleService, ISidebarRevisitService, ILeaderboardScoreService,
        ILeaderboardQueryService, ILeaderboardViewService
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private readonly Dictionary<string, List<LeaderboardEntry>> _leaderboards =
            new Dictionary<string, List<LeaderboardEntry>>(StringComparer.Ordinal);

        public string Id => PulletPlatformIds.Editor;
        public EPlatformCapability Capabilities => EPlatformCapability.Player
            | EPlatformCapability.RewardedAd
            | EPlatformCapability.Share
            | EPlatformCapability.Storage
            | EPlatformCapability.Analytics
            | EPlatformCapability.Lifecycle
            | EPlatformCapability.SidebarRevisit
            | EPlatformCapability.LeaderboardSubmit
            | EPlatformCapability.LeaderboardQuery
            | EPlatformCapability.LeaderboardView;
        public bool IsInitialized { get; private set; }
        public bool GrantRewardedAds { get; set; } = true;
        public PlatformLaunchContext LastLaunchContext { get; private set; }
        public bool EnteredFromSidebar { get; private set; }

        public event Action<PlatformLaunchContext> Shown;
        public event Action Hidden;

        public EditorMiniGamePlatform()
        {
            Register<IPlayerService>(this);
            Register<IRewardedAdService>(this);
            Register<IShareService>(this);
            Register<IPlatformStorageService>(this);
            Register<IAnalyticsService>(this);
            Register<IPlatformLifecycleService>(this);
            Register<ISidebarRevisitService>(this);
            Register<ILeaderboardScoreService>(this);
            Register<ILeaderboardQueryService>(this);
            Register<ILeaderboardViewService>(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void RegisterFactory()
        {
            MiniGameBootstrap.Register(PulletPlatformIds.Editor, () =>
                new ConfiguredPlatformAdapter(new EditorMiniGamePlatform(),
                    MiniGameRuntimeSettingsEditor.GetOrCreate(MiniGameBuildSettingsData.Common.selectedPlatformId)));
        }

        public Task<PlatformResult> InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<PlatformResult>(cancellationToken);
            IsInitialized = true;
            return Task.FromResult(PlatformResult.Success());
        }

        public void Update(float deltaTime, float unscaledDeltaTime) { }

        public void Shutdown()
        {
            IsInitialized = false;
        }

        public bool TryGetService(Type serviceType, out object service)
        {
            return _services.TryGetValue(serviceType, out service);
        }

        public Task<PlatformResult<PlatformLoginCredential>> LoginAsync(
            CancellationToken cancellationToken = default) =>
            CancelledOr(PlatformResult<PlatformLoginCredential>.Success(
                new PlatformLoginCredential(PulletPlatformIds.Editor, "editor-login-code")), cancellationToken);

        public bool IsReady(string placementId) => !string.IsNullOrWhiteSpace(placementId);

        public Task<PlatformResult> LoadAsync(string placementId,
            CancellationToken cancellationToken = default) =>
            CancelledOr(string.IsNullOrWhiteSpace(placementId)
                ? PlatformResult.Failure("Rewarded ad placement id is required.")
                : PlatformResult.Success(), cancellationToken);

        public Task<RewardedAdResult> ShowAsync(string placementId,
            CancellationToken cancellationToken = default)
        {
            if (!IsReady(placementId))
                return CancelledOr(new RewardedAdResult(
                    ERewardedAdStatus.Failed, "Rewarded ad placement id is required."), cancellationToken);

            return CancelledOr(new RewardedAdResult(
                GrantRewardedAds ? ERewardedAdStatus.Completed : ERewardedAdStatus.Skipped), cancellationToken);
        }

        public Task<PlatformResult> ShareAsync(PlatformShareRequest request,
            CancellationToken cancellationToken = default)
        {
            PulletFramework.PLogger.EditorInfo($"[PulletPlatform] Simulated share: {request.Title}");
            return CancelledOr(PlatformResult.Success(), cancellationToken);
        }

        public bool HasKey(string key) => UnityEngine.PlayerPrefs.HasKey(key);
        public string GetString(string key, string defaultValue = "") => UnityEngine.PlayerPrefs.GetString(key, defaultValue);
        public void SetString(string key, string value) => UnityEngine.PlayerPrefs.SetString(key, value);
        public void DeleteKey(string key) => UnityEngine.PlayerPrefs.DeleteKey(key);
        public void Save() => UnityEngine.PlayerPrefs.Save();

        public void Track(string eventName, IReadOnlyDictionary<string, object> parameters = null)
        {
            PulletFramework.PLogger.EditorInfo($"[PulletPlatform] Analytics: {eventName}");
        }

        public void SimulateShow(PlatformLaunchContext context)
        {
            LastLaunchContext = context;
            EnteredFromSidebar = IsSidebarContext(context);
            Shown?.Invoke(context);
        }

        public Task<PlatformResult<bool>> CheckAvailableAsync(
            CancellationToken cancellationToken = default) =>
            CancelledOr(PlatformResult<bool>.Success(true), cancellationToken);

        public Task<PlatformResult> NavigateToSidebarAsync(string activityId,
            CancellationToken cancellationToken = default)
        {
            PulletFramework.PLogger.EditorInfo("[PulletPlatform] Simulated navigate to sidebar.");
            return CancelledOr(PlatformResult.Success(), cancellationToken);
        }

        public void SimulateHide()
        {
            Hidden?.Invoke();
        }

        public Task<PlatformResult> SubmitScoreAsync(LeaderboardScore score,
            CancellationToken cancellationToken = default)
        {
            if (!ValidateBoard(score.BoardId, out PlatformResult failure))
                return CancelledOr(failure, cancellationToken);
            if (score.ValueType == ELeaderboardValueType.Number
                && (!long.TryParse(score.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
                    || number < 0))
                return CancelledOr(PlatformResult.Failure("Numeric leaderboard scores must be non-negative integers."),
                    cancellationToken);

            List<LeaderboardEntry> entries = GetOrSeedBoard(score.BoardId);
            entries.RemoveAll(item => item.IsSelf);
            entries.Add(new LeaderboardEntry(0,
                new PlatformUser("editor-self", "Current Player"), score.Value,
                score.Priority, score.Extra, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), true));
            return CancelledOr(PlatformResult.Success(), cancellationToken);
        }

        public Task<PlatformResult<LeaderboardPage>> GetRanksAsync(LeaderboardQuery query,
            CancellationToken cancellationToken = default)
        {
            if (!ValidateQuery(query, out string error))
                return CancelledOr(PlatformResult<LeaderboardPage>.Failure(error), cancellationToken);

            IEnumerable<LeaderboardEntry> source = GetOrSeedBoard(query.BoardId);
            if (query.Scope == ELeaderboardScope.Friends)
                source = source.Where((_, index) => index % 2 == 0);
            source = query.ValueType == ELeaderboardValueType.Number
                ? source.OrderByDescending(item => ParseScore(item.Value))
                : source.OrderByDescending(item => item.Priority);
            LeaderboardEntry[] ranked = source.Select((item, index) => new LeaderboardEntry(
                index + 1, item.User, item.Value, item.Priority, item.Extra,
                item.UpdatedAtUnixSeconds, item.IsSelf)).ToArray();
            int skip = (query.PageNumber - 1) * query.PageSize;
            LeaderboardEntry[] pageEntries = ranked.Skip(skip).Take(query.PageSize).ToArray();
            LeaderboardEntry? self = ranked.FirstOrDefault(item => item.IsSelf);
            if (self.Value.User.UserId == null) self = null;
            return CancelledOr(PlatformResult<LeaderboardPage>.Success(
                new LeaderboardPage(pageEntries, self, query.PageNumber, ranked.Length)), cancellationToken);
        }

        public Task<PlatformResult> OpenLeaderboardAsync(LeaderboardViewRequest request,
            CancellationToken cancellationToken = default)
        {
            PulletFramework.PLogger.EditorInfo(
                $"[PulletPlatform] Simulated leaderboard: {request.Query.BoardId}");
            return CancelledOr(ValidateQuery(request.Query, out string error)
                ? PlatformResult.Success()
                : PlatformResult.Failure(error), cancellationToken);
        }

        private List<LeaderboardEntry> GetOrSeedBoard(string boardId)
        {
            if (_leaderboards.TryGetValue(boardId, out List<LeaderboardEntry> entries))
                return entries;
            entries = new List<LeaderboardEntry>
            {
                new LeaderboardEntry(0, new PlatformUser("editor-1", "Player One"), "3200"),
                new LeaderboardEntry(0, new PlatformUser("editor-2", "Player Two"), "1800"),
                new LeaderboardEntry(0, new PlatformUser("editor-3", "Player Three"), "900")
            };
            _leaderboards.Add(boardId, entries);
            return entries;
        }

        private static bool ValidateBoard(string boardId, out PlatformResult failure)
        {
            bool valid = !string.IsNullOrWhiteSpace(boardId);
            failure = valid ? PlatformResult.Success() : PlatformResult.Failure("Leaderboard board id is required.");
            return valid;
        }

        private static bool ValidateQuery(LeaderboardQuery query, out string error)
        {
            if (string.IsNullOrWhiteSpace(query.BoardId)) error = "Leaderboard board id is required.";
            else if (query.PageNumber < 1) error = "Leaderboard page number must start at 1.";
            else if (query.PageSize < 1 || query.PageSize > 40) error = "Leaderboard page size must be between 1 and 40.";
            else { error = null; return true; }
            return false;
        }

        private static long ParseScore(string value) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long score)
                ? score : long.MinValue;

        private void Register<TService>(TService service) where TService : class
        {
            _services[typeof(TService)] = service;
        }

        private static bool IsSidebarContext(PlatformLaunchContext context) =>
            string.Equals(context.LaunchFrom, "homepage", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.Location, "sidebar_card", StringComparison.OrdinalIgnoreCase);

        private static Task<T> CancelledOr<T>(T result, CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<T>(cancellationToken)
                : Task.FromResult(result);
    }
}
