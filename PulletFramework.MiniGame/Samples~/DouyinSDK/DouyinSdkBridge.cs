using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
using UnityEngine;

namespace PulletFramework.MiniGame.Platform.Douyin
{
    public sealed class DouyinSdkBridge : IDouyinSdkBridge, IPlayerService, IShareService,
        IRewardedAdService, IPlatformStorageService, IPlatformLifecycleService, ISidebarRevisitService,
        ILeaderboardScoreService, ILeaderboardQueryService, ILeaderboardViewService
    {
        private sealed class AdState
        {
            public TTRewardedVideoAd Ad;
            public bool Loading, Ready, Showing, Disposed;
            public TaskCompletionSource<PlatformResult> LoadSource;
            public TaskCompletionSource<RewardedAdResult> ShowSource;
            public AdLoadDelegate Loaded;
            public AdErrorDelegate Error;
            public RewardedAdClosedDelegate Closed;
        }

        private readonly Dictionary<string, AdState> _ads = new Dictionary<string, AdState>();
        private readonly List<Action> _pending = new List<Action>();
        private TTAppLifeCycle _lifecycle;
        private AdState _showing;
        private bool _initialized;
        private int _generation;
        public string PlatformId => PulletPlatformIds.Douyin;
        public bool IsAvailable => true;
        public EPlatformCapability Capabilities => EPlatformCapability.Player | EPlatformCapability.Share
            | EPlatformCapability.RewardedAd | EPlatformCapability.Storage | EPlatformCapability.Lifecycle
            | EPlatformCapability.SidebarRevisit | EPlatformCapability.LeaderboardSubmit
            | EPlatformCapability.LeaderboardQuery | EPlatformCapability.LeaderboardView;
        public PlatformLaunchContext LastLaunchContext { get; private set; }
        public bool EnteredFromSidebar { get; private set; }
        public event Action<PlatformLaunchContext> Shown;
        public event Action Hidden;

        public void Initialize(Action<PlatformResult> completed)
        {
            if (_initialized) { completed?.Invoke(PlatformResult.Success()); return; }
            int generation = ++_generation;
            try
            {
                TT.InitSDK((code, environment) =>
                {
                    if (generation != _generation || _initialized) return;
                    if (code != 0)
                    {
                        ++_generation;
                        completed?.Invoke(PlatformResult.Failure($"TT.InitSDK failed: {code}"));
                        return;
                    }
                    try
                    {
                        _lifecycle = TT.GetAppLifeCycle();
                        _lifecycle.OnShow += OnShow;
                        _lifecycle.OnHide += OnHide;
                        var launch = TT.GetLaunchOptionsSync();
                        LastLaunchContext = new PlatformLaunchContext(launch.Scene,
                            environment?.GetLaunchFrom() ?? ReadAny(launch.Extra, "launch_from", "launchFrom"),
                            environment?.GetLocation() ?? ReadAny(launch.Extra, "location"),
                            launch.Query == null ? null : new Dictionary<string, string>(launch.Query));
                        EnteredFromSidebar = IsSidebarContext(LastLaunchContext);
                        _initialized = true;
                    }
                    catch (Exception exception)
                    {
                        Shutdown();
                        completed?.Invoke(PlatformResult.Failure(exception.Message));
                        return;
                    }
                    completed?.Invoke(PlatformResult.Success());
                });
            }
            catch (Exception exception)
            {
                if (generation != _generation || _initialized) return;
                Shutdown();
                completed?.Invoke(PlatformResult.Failure(exception.Message));
            }
        }

        public void Update(float deltaTime, float unscaledDeltaTime) { }

        public void Shutdown()
        {
            ++_generation;
            _initialized = false;
            if (_lifecycle != null)
            {
                Safe(() => _lifecycle.OnShow -= OnShow);
                Safe(() => _lifecycle.OnHide -= OnHide);
                _lifecycle = null;
            }
            var states = new List<AdState>(_ads.Values);
            _ads.Clear();
            foreach (var state in states)
            {
                state.Disposed = true;
                Safe(() => state.Ad.OnLoad -= state.Loaded);
                Safe(() => state.Ad.OnError -= state.Error);
                Safe(() => state.Ad.OnClose -= state.Closed);
                Safe(state.Ad.Destroy);
                FinishLoad(state, PlatformResult.Failure("Platform shut down."));
                FinishShow(state, new RewardedAdResult(ERewardedAdStatus.Failed, "Platform shut down."));
            }
            var pending = _pending.ToArray();
            _pending.Clear();
            foreach (var cancel in pending) Safe(cancel);
            _showing = null;
            Shown = null;
            Hidden = null;
        }

        public Task<PlatformResult<PlatformLoginCredential>> LoginAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_initialized)
                return PlatformTask.FromResult(
                    PlatformResult<PlatformLoginCredential>.Failure("Platform is not initialized."), cancellationToken);

            return Request(cancellationToken,
                PlatformResult<PlatformLoginCredential>.Failure("Platform shut down."), finish =>
                TT.Login((code, anonymousCode, _) =>
                {
                    finish(string.IsNullOrEmpty(code) && string.IsNullOrEmpty(anonymousCode)
                        ? PlatformResult<PlatformLoginCredential>.Failure("Login returned no credential.")
                        : PlatformResult<PlatformLoginCredential>.Success(
                            new PlatformLoginCredential(PlatformId, code, anonymousCode)));
                }, error => finish(PlatformResult<PlatformLoginCredential>.Failure(error)), false),
                exception => PlatformResult<PlatformLoginCredential>.Failure(exception.Message));
        }

        public Task<PlatformResult> ShareAsync(PlatformShareRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!_initialized)
                return PlatformTask.FromResult(
                    PlatformResult.Failure("Platform is not initialized."), cancellationToken);

            return Request(cancellationToken, PlatformResult.Failure("Platform shut down."), finish =>
            {
                var data = new JsonData();
                data["title"] = request.Title ?? "";
                if (!string.IsNullOrEmpty(request.ImageUrl)) data["imageUrl"] = request.ImageUrl;
                if (!string.IsNullOrEmpty(request.Query)) data["query"] = request.Query;
                TT.ShareAppMessage(data, _ => finish(PlatformResult.Success()),
                    error => finish(PlatformResult.Failure(error)),
                    () => finish(PlatformResult.Failure("Share cancelled.")));
            }, exception => PlatformResult.Failure(exception.Message));
        }

        public Task<PlatformResult> SubmitScoreAsync(LeaderboardScore score,
            CancellationToken cancellationToken = default)
        {
            if (!_initialized)
                return PlatformTask.FromResult(
                    PlatformResult.Failure("Platform is not initialized."), cancellationToken);
            if (!ValidateScore(score, out string validationError))
                return PlatformTask.FromResult(PlatformResult.Failure(validationError), cancellationToken);

            return Request(cancellationToken, PlatformResult.Failure("Platform shut down."), finish =>
            {
                var data = new JsonData();
                data["dataType"] = (int)score.ValueType;
                data["value"] = score.Value;
                if (score.ValueType == ELeaderboardValueType.Tier)
                    data["priority"] = score.Priority;
                if (!string.IsNullOrEmpty(score.Extra)) data["extra"] = score.Extra;
                data["zoneId"] = score.BoardId;
                TT.SetImRankData(data, (succeeded, message) => finish(succeeded
                    ? PlatformResult.Success()
                    : PlatformResult.Failure(message)));
            }, exception => PlatformResult.Failure(exception.Message));
        }

        public Task<PlatformResult<LeaderboardPage>> GetRanksAsync(LeaderboardQuery query,
            CancellationToken cancellationToken = default)
        {
            if (!_initialized)
                return PlatformTask.FromResult(
                    PlatformResult<LeaderboardPage>.Failure("Platform is not initialized."), cancellationToken);
            if (!ValidateQuery(query, out string validationError))
                return PlatformTask.FromResult(
                    PlatformResult<LeaderboardPage>.Failure(validationError), cancellationToken);

            return Request(cancellationToken,
                PlatformResult<LeaderboardPage>.Failure("Platform shut down."), finish =>
            {
                JsonData data = CreateRankQuery(query);
                TTRank.OnGetRankDataSuccessCallback success = (ref TTRank.RankData rankData) =>
                    finish(PlatformResult<LeaderboardPage>.Success(ConvertRankData(rankData, query)));
                TT.GetImRankData(data, success,
                    message => finish(PlatformResult<LeaderboardPage>.Failure(message)));
            }, exception => PlatformResult<LeaderboardPage>.Failure(exception.Message));
        }

        public Task<PlatformResult> OpenLeaderboardAsync(LeaderboardViewRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!_initialized)
                return PlatformTask.FromResult(
                    PlatformResult.Failure("Platform is not initialized."), cancellationToken);
            if (!ValidateQuery(request.Query, out string validationError))
                return PlatformTask.FromResult(PlatformResult.Failure(validationError), cancellationToken);

            return Request(cancellationToken, PlatformResult.Failure("Platform shut down."), finish =>
            {
                JsonData data = CreateRankQuery(request.Query);
                data["suffix"] = request.Suffix ?? "";
                data["rankTitle"] = request.Title ?? "";
                TT.GetImRankList(data, (succeeded, message) => finish(succeeded
                    ? PlatformResult.Success()
                    : PlatformResult.Failure(message)));
            }, exception => PlatformResult.Failure(exception.Message));
        }

        public bool IsReady(string placementId) => _initialized && !string.IsNullOrEmpty(placementId)
            && _ads.TryGetValue(placementId, out var state) && state.Ready;

        public Task<PlatformResult> LoadAsync(string placementId,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<PlatformResult>(cancellationToken);
            if (!_initialized || string.IsNullOrWhiteSpace(placementId))
                return PlatformTask.FromResult(PlatformResult.Failure(
                    "Initialize platform and configure an ad unit id first."), cancellationToken);
            AdState state;
            try { state = GetAd(placementId); }
            catch (Exception exception)
            {
                return PlatformTask.FromResult(PlatformResult.Failure(exception.Message), cancellationToken);
            }
            if (state.Showing || state.Ready)
                return PlatformTask.FromResult(state.Ready
                    ? PlatformResult.Success()
                    : PlatformResult.Failure("Ad is showing."), cancellationToken);
            Task<PlatformResult> loadTask;
            if (!state.Loading)
            {
                state.Loading = true;
                state.LoadSource = new TaskCompletionSource<PlatformResult>();
                loadTask = state.LoadSource.Task;
                try { state.Ad.Load(); }
                catch (Exception exception) { FinishLoad(state, PlatformResult.Failure(exception.Message)); }
            }
            else
            {
                loadTask = state.LoadSource.Task;
            }
            return PlatformTask.WithCancellation(loadTask, cancellationToken);
        }

        public Task<RewardedAdResult> ShowAsync(string placementId,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<RewardedAdResult>(cancellationToken);
            if (_showing != null || !IsReady(placementId))
                return PlatformTask.FromResult(new RewardedAdResult(ERewardedAdStatus.Failed,
                    "Ad is not loaded or another ad is showing."), cancellationToken);
            var state = _ads[placementId];
            state.Ready = false;
            state.Showing = true;
            state.ShowSource = new TaskCompletionSource<RewardedAdResult>();
            Task<RewardedAdResult> showTask = state.ShowSource.Task;
            _showing = state;
            try { state.Ad.Show(); }
            catch (Exception exception) { FinishShow(state, new RewardedAdResult(ERewardedAdStatus.Failed, exception.Message)); }
            return PlatformTask.WithCancellation(showTask, cancellationToken);
        }

        private AdState GetAd(string id)
        {
            if (_ads.TryGetValue(id, out var state)) return state;
            state = new AdState { Ad = TT.CreateRewardedVideoAd(new CreateRewardedVideoAdParam { AdUnitId = id, Multiton = false }) };
            if (state.Ad == null) throw new InvalidOperationException("TTSDK did not create an ad.");
            state.Loaded = () => { if (!state.Disposed) FinishLoad(state, PlatformResult.Success()); };
            state.Error = (code, message) =>
            {
                if (state.Disposed) return;
                state.Ready = false;
                string error = $"[{code}] {message}";
                FinishLoad(state, PlatformResult.Failure(error));
                FinishShow(state, new RewardedAdResult(ERewardedAdStatus.Failed, error));
            };
            state.Closed = (ended, _) => FinishShow(state,
                new RewardedAdResult(ended ? ERewardedAdStatus.Completed : ERewardedAdStatus.Skipped));
            try
            {
                state.Ad.OnLoad += state.Loaded;
                state.Ad.OnError += state.Error;
                state.Ad.OnClose += state.Closed;
            }
            catch { Safe(state.Ad.Destroy); throw; }
            _ads.Add(id, state);
            return state;
        }

        private static void FinishLoad(AdState state, PlatformResult result)
        {
            if (!state.Loading) return;
            state.Loading = false;
            state.Ready = !state.Disposed && result.Succeeded;
            TaskCompletionSource<PlatformResult> source = state.LoadSource;
            state.LoadSource = null;
            source?.TrySetResult(result);
        }

        private void FinishShow(AdState state, RewardedAdResult result)
        {
            if (!state.Showing) return;
            state.Showing = false;
            if (ReferenceEquals(_showing, state)) _showing = null;
            TaskCompletionSource<RewardedAdResult> source = state.ShowSource;
            state.ShowSource = null;
            source?.TrySetResult(result);
        }

        private Task<T> Request<T>(CancellationToken cancellationToken, T shutdownResult,
            Action<Action<T>> begin, Func<Exception, T> exceptionResult)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<T>(cancellationToken);

            var source = new TaskCompletionSource<T>();
            Action cancel = null;
            Action<T> finish = result =>
            {
                if (!source.TrySetResult(result)) return;
                _pending.Remove(cancel);
            };
            cancel = () => finish(shutdownResult);
            _pending.Add(cancel);
            try { begin(finish); }
            catch (Exception exception) { finish(exceptionResult(exception)); }
            return PlatformTask.WithCancellation(source.Task, cancellationToken);
        }

        private static JsonData CreateRankQuery(LeaderboardQuery query)
        {
            var data = new JsonData();
            data["rankType"] = RankPeriod(query.Period);
            data["dataType"] = (int)query.ValueType;
            data["relationType"] = query.Scope == ELeaderboardScope.Friends ? "friend" : "all";
            data["pageNum"] = query.PageNumber;
            data["pageSize"] = query.PageSize;
            data["zoneId"] = query.BoardId;
            return data;
        }

        private static LeaderboardPage ConvertRankData(TTRank.RankData data, LeaderboardQuery query)
        {
            int startRank = (data.PageNum - 1) * query.PageSize + 1;
            int count = data.Items?.Count ?? 0;
            var entries = new LeaderboardEntry[count];
            string selfId = data.SelfUserInfo?.OpenId;
            for (int index = 0; index < count; index++)
            {
                TTRank.RankResItem item = data.Items[index];
                entries[index] = ConvertRankItem(item, startRank + index,
                    !string.IsNullOrEmpty(selfId) && selfId == item.OpenId);
            }

            LeaderboardEntry? self = null;
            if (data.SelfItem?.Item != null && data.SelfItem.Rank > 0)
                self = ConvertRankItem(data.SelfItem.Item, data.SelfItem.Rank, true);
            return new LeaderboardPage(entries, self, data.PageNum, data.TotalNum);
        }

        private static LeaderboardEntry ConvertRankItem(TTRank.RankResItem item, int rank, bool isSelf)
        {
            string userId = !string.IsNullOrEmpty(item.OpenId) ? item.OpenId : item.SecUid;
            return new LeaderboardEntry(rank,
                new PlatformUser(userId, item.Nickname, item.UserImg), item.Value,
                item.Priority, item.Extra, item.UTime, isSelf);
        }

        private static bool ValidateScore(LeaderboardScore score, out string error)
        {
            if (string.IsNullOrWhiteSpace(score.BoardId)) error = "Leaderboard board id is required.";
            else if (string.IsNullOrWhiteSpace(score.Value)) error = "Leaderboard value is required.";
            else if (score.ValueType == ELeaderboardValueType.Number
                     && (!long.TryParse(score.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                         || value < 0))
                error = "Douyin numeric leaderboard values must be non-negative integers.";
            else if (score.ValueType == ELeaderboardValueType.Tier && score.Priority < 0)
                error = "Douyin tier priority must be non-negative.";
            else { error = null; return true; }
            return false;
        }

        private static bool ValidateQuery(LeaderboardQuery query, out string error)
        {
            if (string.IsNullOrWhiteSpace(query.BoardId)) error = "Leaderboard board id is required.";
            else if (query.PageNumber < 1) error = "Leaderboard page number must start at 1.";
            else if (query.PageSize < 1 || query.PageSize > 40) error = "Douyin leaderboard page size must be between 1 and 40.";
            else { error = null; return true; }
            return false;
        }

        private static string RankPeriod(ELeaderboardPeriod period)
        {
            switch (period)
            {
                case ELeaderboardPeriod.Day: return "day";
                case ELeaderboardPeriod.Week: return "week";
                case ELeaderboardPeriod.Month: return "month";
                default: return "all";
            }
        }

        private void OnShow(Dictionary<string, object> data)
        {
            var query = new Dictionary<string, string>();
            if (data != null && data.TryGetValue("query", out var raw) && raw is IDictionary values)
                foreach (DictionaryEntry pair in values) query[Convert.ToString(pair.Key)] = Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
            LastLaunchContext = new PlatformLaunchContext(Read(data, "scene"),
                ReadAny(data, "launch_from", "launchFrom"), Read(data, "location"), query);
            EnteredFromSidebar = IsSidebarContext(LastLaunchContext);
            Shown?.Invoke(LastLaunchContext);
        }
        private void OnHide() => Hidden?.Invoke();
        private static string Read<T>(IDictionary<string, T> data, string key) =>
            data != null && data.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        private static string ReadAny<T>(IDictionary<string, T> data, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = Read(data, key);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return null;
        }
        private static bool IsSidebarContext(PlatformLaunchContext context) =>
            string.Equals(context.LaunchFrom, "homepage", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.Location, "sidebar_card", StringComparison.OrdinalIgnoreCase);

        public Task<PlatformResult<bool>> CheckAvailableAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_initialized)
                return PlatformTask.FromResult(
                    PlatformResult<bool>.Failure("Platform is not initialized."), cancellationToken);
            return Request(cancellationToken, PlatformResult<bool>.Failure("Platform shut down."), finish =>
                TT.CheckScene(TTSideBar.SceneEnum.SideBar,
                    supported => finish(PlatformResult<bool>.Success(supported)),
                    () => { },
                    (code, message) => finish(PlatformResult<bool>.Failure($"[{code}] {message}"))),
                exception => PlatformResult<bool>.Failure(exception.Message));
        }

        public Task<PlatformResult> NavigateToSidebarAsync(string activityId,
            CancellationToken cancellationToken = default)
        {
            if (!_initialized)
                return PlatformTask.FromResult(
                    PlatformResult.Failure("Platform is not initialized."), cancellationToken);
            return Request(cancellationToken, PlatformResult.Failure("Platform shut down."), finish =>
            {
                var data = new JsonData();
                data["scene"] = "sidebar";
                if (!string.IsNullOrWhiteSpace(activityId)) data["activityId"] = activityId;
                TT.NavigateToScene(data,
                    () => finish(PlatformResult.Success()),
                    () => { },
                    (code, message) => finish(PlatformResult.Failure($"[{code}] {message}")));
            }, exception => PlatformResult.Failure(exception.Message));
        }
        private static void Safe(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                PulletFramework.PLogger.Exception(
                    exception, "[PulletFramework.MiniGame] 抖音 SDK 回调执行失败。");
            }
        }
        public bool HasKey(string key) => TT.PlayerPrefs.HasKey(key);
        public string GetString(string key, string defaultValue = "") => TT.PlayerPrefs.GetString(key, defaultValue);
        public void SetString(string key, string value) => TT.PlayerPrefs.SetString(key, value);
        public void DeleteKey(string key) => TT.PlayerPrefs.DeleteKey(key);
        public void Save() => TT.PlayerPrefs.Save();
    }
}
