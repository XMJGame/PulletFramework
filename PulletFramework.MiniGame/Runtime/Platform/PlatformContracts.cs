using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulletFramework.MiniGame.Platform
{
    public static class PulletPlatformIds
    {
        public const string Editor = "editor";
        public const string WeChat = "wechat";
        public const string Douyin = "douyin";
    }

    [Flags]
    public enum EPlatformCapability
    {
        None = 0,
        Player = 1 << 0,
        RewardedAd = 1 << 1,
        Share = 1 << 2,
        Storage = 1 << 3,
        Analytics = 1 << 4,
        Lifecycle = 1 << 5,
        SidebarRevisit = 1 << 6,
        LeaderboardSubmit = 1 << 7,
        LeaderboardQuery = 1 << 8,
        LeaderboardView = 1 << 9
    }

    /// <summary>无返回值平台操作的统一结果；可预期失败通过 Error 返回，不使用异常控制流程。</summary>
    public readonly struct PlatformResult
    {
        public bool Succeeded { get; }
        public string Error { get; }

        private PlatformResult(bool succeeded, string error)
        {
            Succeeded = succeeded;
            Error = error;
        }

        public static PlatformResult Success() => new PlatformResult(true, null);
        public static PlatformResult Failure(string error) => new PlatformResult(false, error);
    }

    /// <summary>带返回值平台操作的统一结果。</summary>
    public readonly struct PlatformResult<T>
    {
        public bool Succeeded { get; }
        public T Value { get; }
        public string Error { get; }

        private PlatformResult(bool succeeded, T value, string error)
        {
            Succeeded = succeeded;
            Value = value;
            Error = error;
        }

        public static PlatformResult<T> Success(T value) => new PlatformResult<T>(true, value, null);
        public static PlatformResult<T> Failure(string error) => new PlatformResult<T>(false, default, error);
    }

    public readonly struct PlatformUser
    {
        public string UserId { get; }
        public string Nickname { get; }
        public string AvatarUrl { get; }

        public PlatformUser(string userId, string nickname, string avatarUrl = null)
        {
            UserId = userId;
            Nickname = nickname;
            AvatarUrl = avatarUrl;
        }
    }

    /// <summary>平台临时登录凭证。Code 应发送给业务服务器换取正式账号会话。</summary>
    public readonly struct PlatformLoginCredential
    {
        public string Provider { get; }
        public string Code { get; }
        public string AnonymousCode { get; }

        public PlatformLoginCredential(string provider, string code, string anonymousCode = null)
        {
            Provider = provider;
            Code = code;
            AnonymousCode = anonymousCode;
        }
    }

    public readonly struct PlatformLaunchContext
    {
        public string Scene { get; }
        public string LaunchFrom { get; }
        public string Location { get; }
        public IReadOnlyDictionary<string, string> Query { get; }

        public PlatformLaunchContext(
            string scene,
            string launchFrom,
            string location,
            IReadOnlyDictionary<string, string> query = null)
        {
            Scene = scene;
            LaunchFrom = launchFrom;
            Location = location;
            Query = query;
        }
    }

    public readonly struct PlatformShareRequest
    {
        public string Title { get; }
        public string ImageUrl { get; }
        public string Query { get; }

        public PlatformShareRequest(string title, string imageUrl = null, string query = null)
        {
            Title = title;
            ImageUrl = imageUrl;
            Query = query;
        }
    }

    public enum ERewardedAdStatus
    {
        Completed,
        Skipped,
        Failed
    }

    /// <summary>激励视频播放结果，业务只能依据 ShouldGrantReward 决定是否发奖。</summary>
    public readonly struct RewardedAdResult
    {
        public ERewardedAdStatus Status { get; }
        public string Error { get; }
        public bool ShouldGrantReward => Status == ERewardedAdStatus.Completed;

        public RewardedAdResult(ERewardedAdStatus status, string error = null)
        {
            Status = status;
            Error = error;
        }
    }

    public interface IPlatformAdapter
    {
        string Id { get; }
        EPlatformCapability Capabilities { get; }
        bool IsInitialized { get; }
        /// <summary>初始化当前平台。并发调用会共享同一次初始化。</summary>
        Task<PlatformResult> InitializeAsync(CancellationToken cancellationToken = default);
        void Update(float deltaTime, float unscaledDeltaTime);
        void Shutdown();
        bool TryGetService(Type serviceType, out object service);
    }

    public interface IPlayerService
    {
        /// <summary>获取平台临时登录凭证，正式会话应由业务服务器换取。</summary>
        Task<PlatformResult<PlatformLoginCredential>> LoginAsync(
            CancellationToken cancellationToken = default);
    }

    public interface IRewardedAdService
    {
        bool IsReady(string placementId);
        /// <summary>预加载指定业务广告位。</summary>
        Task<PlatformResult> LoadAsync(string placementId,
            CancellationToken cancellationToken = default);

        /// <summary>展示已加载的激励视频；仅 ShouldGrantReward 为 true 时发奖。</summary>
        Task<RewardedAdResult> ShowAsync(string placementId,
            CancellationToken cancellationToken = default);
    }

    public interface IShareService
    {
        /// <summary>发起平台分享。成功只代表分享请求已被平台接受。</summary>
        Task<PlatformResult> ShareAsync(PlatformShareRequest request,
            CancellationToken cancellationToken = default);
    }

    public interface IPlatformStorageService
    {
        bool HasKey(string key);
        string GetString(string key, string defaultValue = "");
        void SetString(string key, string value);
        void DeleteKey(string key);
        void Save();
    }

    public interface IAnalyticsService
    {
        void Track(string eventName, IReadOnlyDictionary<string, object> parameters = null);
    }

    public interface IPlatformLifecycleService
    {
        /// <summary>最近一次启动或回到前台时的平台上下文。</summary>
        PlatformLaunchContext LastLaunchContext { get; }
        event Action<PlatformLaunchContext> Shown;
        event Action Hidden;
    }

    public interface ISidebarRevisitService
    {
        bool EnteredFromSidebar { get; }

        /// <summary>检查当前宿主是否支持侧边栏能力。</summary>
        Task<PlatformResult<bool>> CheckAvailableAsync(
            CancellationToken cancellationToken = default);

        /// <summary>调起平台侧边栏；调起成功不能直接作为发奖依据。</summary>
        Task<PlatformResult> NavigateToSidebarAsync(string activityId,
            CancellationToken cancellationToken = default);
    }

    public interface IMiniGameSdkBridge
    {
        string PlatformId { get; }
        EPlatformCapability Capabilities { get; }
        bool IsAvailable { get; }
        // SDK 桥接层保留回调，便于直接映射微信、抖音原生接口。
        void Initialize(Action<PlatformResult> completed);
        void Update(float deltaTime, float unscaledDeltaTime);
        void Shutdown();
    }
}
