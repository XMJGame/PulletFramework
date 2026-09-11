using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PulletMiniGame.Platform;
using UnityEngine;
using WeChatWASM;

namespace PulletMiniGame.Platform.WeChat
{
    public sealed class WeChatSdkBridge : IWeChatSdkBridge, IPlayerService,
        IRewardedAdService, IShareService, IPlatformStorageService, IPlatformLifecycleService,
        ILeaderboardScoreService
    {
        [Serializable]
        private sealed class WeChatCloudScore
        {
            public WeChatGameScore wxgame;
            public string detail;
        }

        [Serializable]
        private sealed class WeChatGameScore
        {
            public long score;
            public long update_time;
        }

        private sealed class RewardedAdState
        {
            public WXRewardedVideoAd Ad;
            public bool IsLoaded;
            public bool IsShowing;
            public bool IsLoading;
            public bool Disposed;
            public TaskCompletionSource<PlatformResult> LoadSource;
            public Action<WXRewardedVideoAdOnCloseResponse> CloseHandler;
            public Action<WXADErrorResponse> ErrorHandler;
            public TaskCompletionSource<RewardedAdResult> ShowSource;
        }

        private readonly Dictionary<string, RewardedAdState> _rewardedAds =
            new Dictionary<string, RewardedAdState>();
        private readonly List<Action> _pending = new List<Action>();
        private Action<OnShowListenerResult> _showHandler;
        private Action<GeneralCallbackResult> _hideHandler;
        private bool _initialized;
        private int _generation;
        private RewardedAdState _showing;

        public string PlatformId => PulletPlatformIds.WeChat;
        public EPlatformCapability Capabilities => EPlatformCapability.Player
            | EPlatformCapability.RewardedAd
            | EPlatformCapability.Share
            | EPlatformCapability.Storage
            | EPlatformCapability.Lifecycle
            | EPlatformCapability.LeaderboardSubmit;
        public bool IsAvailable => true;
        public PlatformLaunchContext LastLaunchContext { get; private set; }

        public event Action<PlatformLaunchContext> Shown;
        public event Action Hidden;

        public void Initialize(Action<PlatformResult> completed)
        {
            if (_initialized)
            {
                completed?.Invoke(PlatformResult.Success());
                return;
            }

            int generation = ++_generation;
            try
            {
                WX.InitSDK(_ =>
                {
                    if (generation == _generation && !_initialized)
                        OnSdkReady(completed);
                });
            }
            catch (Exception exception)
            {
                if (generation == _generation && !_initialized)
                {
                    Shutdown();
                    completed?.Invoke(PlatformResult.Failure(exception.Message));
                }
            }
        }

        private void OnSdkReady(Action<PlatformResult> completed)
        {
            try
            {
                _showHandler = HandleShow;
                _hideHandler = _ => Hidden?.Invoke();
                WX.OnShow(_showHandler);
                WX.OnHide(_hideHandler);
                LastLaunchContext = ConvertLaunchContext(WX.GetLaunchOptionsSync());
                _initialized = true;
            }
            catch (Exception exception)
            {
                Shutdown();
                completed?.Invoke(PlatformResult.Failure(exception.Message));
                return;
            }
            completed?.Invoke(PlatformResult.Success());
        }

        public void Update(float deltaTime, float unscaledDeltaTime) { }

        public void Shutdown()
        {
            ++_generation;
            _initialized = false;
            if (_showHandler != null)
                Cleanup(() => WX.OffShow(_showHandler));
            if (_hideHandler != null)
                Cleanup(() => WX.OffHide(_hideHandler));
            _showHandler = null;
            _hideHandler = null;

            var states = new List<RewardedAdState>(_rewardedAds.Values);
            _rewardedAds.Clear();
            foreach (RewardedAdState state in states)
            {
                state.Disposed = true;
                if (state.CloseHandler != null)
                    Cleanup(() => state.Ad.OffClose(state.CloseHandler));
                if (state.ErrorHandler != null)
                    Cleanup(() => state.Ad.OffError(state.ErrorHandler));
                Cleanup(state.Ad.Destroy);
                FinishLoad(state, PlatformResult.Failure("Platform shut down."));
                CompleteRewardedAd(state, new RewardedAdResult(ERewardedAdStatus.Failed, "Platform shut down."));
            }
            Action[] pending = _pending.ToArray();
            _pending.Clear();
            foreach (Action cancel in pending)
                Cleanup(cancel);
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
                WX.Login(new LoginOption
                {
                    success = result => finish(
                        PlatformResult<PlatformLoginCredential>.Success(
                            new PlatformLoginCredential(PulletPlatformIds.WeChat, result.code))),
                    fail = error => finish(
                        PlatformResult<PlatformLoginCredential>.Failure(error.errMsg))
                }), exception => PlatformResult<PlatformLoginCredential>.Failure(exception.Message));
        }

        public Task<PlatformResult> ShareAsync(PlatformShareRequest request,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<PlatformResult>(cancellationToken);
            if (!_initialized)
                return PlatformTask.FromResult(
                    PlatformResult.Failure("Platform is not initialized."), cancellationToken);
            try
            {
                WX.ShareAppMessage(new ShareAppMessageOption
                {
                    title = request.Title,
                    imageUrl = request.ImageUrl,
                    query = request.Query
                });
            }
            catch (Exception exception)
            {
                return PlatformTask.FromResult(
                    PlatformResult.Failure(exception.Message), cancellationToken);
            }
            // 微信没有可靠的分享完成结果，这里只表示平台已接受分享请求。
            return PlatformTask.FromResult(PlatformResult.Success(), cancellationToken);
        }

        public Task<PlatformResult> SubmitScoreAsync(LeaderboardScore score,
            CancellationToken cancellationToken = default)
        {
            if (!_initialized)
                return PlatformTask.FromResult(
                    PlatformResult.Failure("Platform is not initialized."), cancellationToken);
            if (string.IsNullOrWhiteSpace(score.BoardId))
                return PlatformTask.FromResult(
                    PlatformResult.Failure("Leaderboard board id is required."), cancellationToken);
            if (score.ValueType != ELeaderboardValueType.Number
                || !long.TryParse(score.Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long value) || value < 0)
                return PlatformTask.FromResult(PlatformResult.Failure(
                    "WeChat cloud leaderboards require a non-negative numeric score."), cancellationToken);

            var cloudValue = new WeChatCloudScore
            {
                wxgame = new WeChatGameScore
                {
                    score = value,
                    update_time = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                },
                detail = score.Extra
            };
            return Request(cancellationToken, PlatformResult.Failure("Platform shut down."), finish =>
                WX.SetUserCloudStorage(new SetUserCloudStorageOption
                {
                    KVDataList = new[]
                    {
                        new KVData { key = score.BoardId, value = JsonUtility.ToJson(cloudValue) }
                    },
                    success = _ => finish(PlatformResult.Success()),
                    fail = error => finish(PlatformResult.Failure(error.errMsg))
                }), exception => PlatformResult.Failure(exception.Message));
        }

        public bool IsReady(string placementId)
        {
            return _initialized && !string.IsNullOrWhiteSpace(placementId)
                && _rewardedAds.TryGetValue(placementId, out RewardedAdState state)
                && state.IsLoaded;
        }

        public Task<PlatformResult> LoadAsync(string placementId,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<PlatformResult>(cancellationToken);
            if (!_initialized)
                return PlatformTask.FromResult(
                    PlatformResult.Failure("Platform is not initialized."), cancellationToken);
            if (string.IsNullOrWhiteSpace(placementId))
                return PlatformTask.FromResult(
                    PlatformResult.Failure("Rewarded ad placement id is required."), cancellationToken);

            RewardedAdState state;
            try { state = GetOrCreateRewardedAd(placementId); }
            catch (Exception exception)
            {
                return PlatformTask.FromResult(PlatformResult.Failure(exception.Message), cancellationToken);
            }
            if (state.IsShowing || state.IsLoaded)
                return PlatformTask.FromResult(state.IsShowing
                    ? PlatformResult.Failure("Rewarded ad is showing.")
                    : PlatformResult.Success(), cancellationToken);
            Task<PlatformResult> loadTask;
            if (!state.IsLoading)
            {
                state.IsLoading = true;
                state.LoadSource = new TaskCompletionSource<PlatformResult>();
                loadTask = state.LoadSource.Task;
                try
                {
                    state.Ad.Load(
                        _ => { if (!state.Disposed) FinishLoad(state, PlatformResult.Success()); },
                        error => { if (!state.Disposed) FinishLoad(state, PlatformResult.Failure(FormatAdError(error))); });
                }
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
            if (_showing != null)
                return PlatformTask.FromResult(new RewardedAdResult(
                    ERewardedAdStatus.Failed, "Another rewarded ad is showing."), cancellationToken);
            if (!IsReady(placementId))
                return PlatformTask.FromResult(new RewardedAdResult(
                    ERewardedAdStatus.Failed, "Rewarded ad is not loaded."), cancellationToken);

            RewardedAdState state = _rewardedAds[placementId];
            if (state.IsShowing)
                return PlatformTask.FromResult(new RewardedAdResult(
                    ERewardedAdStatus.Failed, "Rewarded ad is already showing."), cancellationToken);

            state.IsShowing = true;
            state.IsLoaded = false;
            state.ShowSource = new TaskCompletionSource<RewardedAdResult>();
            Task<RewardedAdResult> showTask = state.ShowSource.Task;
            _showing = state;
            try
            {
                state.Ad.Show(
                    _ => { },
                    error => CompleteRewardedAd(state,
                        new RewardedAdResult(ERewardedAdStatus.Failed, error.errMsg)));
            }
            catch (Exception exception)
            {
                CompleteRewardedAd(state, new RewardedAdResult(ERewardedAdStatus.Failed, exception.Message));
            }
            return PlatformTask.WithCancellation(showTask, cancellationToken);
        }

        public bool HasKey(string key) => WXBase.StorageHasKeySync(key);
        public string GetString(string key, string defaultValue = "") =>
            WXBase.StorageGetStringSync(key, defaultValue);
        public void SetString(string key, string value) => WXBase.StorageSetStringSync(key, value);
        public void DeleteKey(string key) => WXBase.StorageDeleteKeySync(key);
        public void Save() { }

        private RewardedAdState GetOrCreateRewardedAd(string placementId)
        {
            if (_rewardedAds.TryGetValue(placementId, out RewardedAdState state))
                return state;

            state = new RewardedAdState
            {
                Ad = WXBase.CreateRewardedVideoAd(new WXCreateRewardedVideoAdParam
                {
                    adUnitId = placementId,
                    multiton = true
                })
            };
            state.CloseHandler = response => CompleteRewardedAd(state,
                new RewardedAdResult(response != null && response.isEnded
                    ? ERewardedAdStatus.Completed
                    : ERewardedAdStatus.Skipped));
            state.ErrorHandler = error =>
            {
                if (state.Disposed)
                    return;
                state.IsLoaded = false;
                FinishLoad(state, PlatformResult.Failure(FormatAdError(error)));
                CompleteRewardedAd(state, new RewardedAdResult(ERewardedAdStatus.Failed, FormatAdError(error)));
            };
            try
            {
                state.Ad.OnClose(state.CloseHandler);
                state.Ad.OnError(state.ErrorHandler);
            }
            catch
            {
                Cleanup(state.Ad.Destroy);
                throw;
            }
            _rewardedAds.Add(placementId, state);
            return state;
        }

        private void CompleteRewardedAd(RewardedAdState state, RewardedAdResult result)
        {
            if (!state.IsShowing)
                return;

            state.IsShowing = false;
            if (ReferenceEquals(_showing, state))
                _showing = null;
            TaskCompletionSource<RewardedAdResult> source = state.ShowSource;
            state.ShowSource = null;
            source?.TrySetResult(result);
        }

        private static void FinishLoad(RewardedAdState state, PlatformResult result)
        {
            if (!state.IsLoading)
                return;
            state.IsLoading = false;
            state.IsLoaded = !state.Disposed && result.Succeeded;
            TaskCompletionSource<PlatformResult> source = state.LoadSource;
            state.LoadSource = null;
            source?.TrySetResult(result);
        }

        private static void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception exception) { Debug.LogException(exception); }
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

        private void HandleShow(OnShowListenerResult result)
        {
            LastLaunchContext = new PlatformLaunchContext(
                result.scene.ToString("0"), null, null, result.query);
            Shown?.Invoke(LastLaunchContext);
        }

        private static PlatformLaunchContext ConvertLaunchContext(LaunchOptionsGame result)
        {
            return new PlatformLaunchContext(
                result.scene.ToString("0"), null, null, result.query);
        }

        private static string FormatAdError(WXADErrorResponse error)
        {
            return $"[{error.errCode}] {error.errMsg}";
        }
    }
}
