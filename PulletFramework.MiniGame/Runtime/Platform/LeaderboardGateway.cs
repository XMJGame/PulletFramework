using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulletFramework.MiniGame.Platform
{
    public enum ELeaderboardProvider
    {
        Auto,
        Platform,
        Backend
    }

    /// <summary>
    /// 游戏自建排行榜后端。实现可以基于 Pullet HTTP，也可以接入已有账号和服务器体系。
    /// </summary>
    public interface ILeaderboardBackend : ILeaderboardScoreService, ILeaderboardQueryService { }

    /// <summary>统一选择平台排行榜或游戏自建排行榜，不在业务 UI 中散落平台判断。</summary>
    public sealed class LeaderboardGateway
    {
        private readonly ILeaderboardBackend _backend;

        public bool CanSubmitToPlatform => PulletPlatform.TryGet(out ILeaderboardScoreService _);
        public bool CanQueryPlatform => PulletPlatform.TryGet(out ILeaderboardQueryService _);
        public bool CanOpenPlatformView => PulletPlatform.TryGet(out ILeaderboardViewService _);
        public bool HasBackend => _backend != null;

        /// <summary>
        /// 平台是否能完成“上报 + 数据查询”的闭环。Auto 模式只在闭环成立时选平台，
        /// 避免把成绩写入平台云存储、随后却从业务服务器读取另一份榜单。
        /// </summary>
        public bool CanUsePlatformDataLeaderboard => CanSubmitToPlatform && CanQueryPlatform;

        public LeaderboardGateway(ILeaderboardBackend backend = null)
        {
            _backend = backend;
        }

        public Task<PlatformResult> SubmitScoreAsync(LeaderboardScore score,
            ELeaderboardProvider provider = ELeaderboardProvider.Auto,
            CancellationToken cancellationToken = default)
        {
            ILeaderboardScoreService service = ResolveScoreService(provider);
            return service == null
                ? PlatformTask.FromResult(PlatformResult.Failure(
                    MissingProviderError(provider, "score submission")), cancellationToken)
                : service.SubmitScoreAsync(score, cancellationToken);
        }

        public Task<PlatformResult<LeaderboardPage>> GetRanksAsync(LeaderboardQuery query,
            ELeaderboardProvider provider = ELeaderboardProvider.Auto,
            CancellationToken cancellationToken = default)
        {
            ILeaderboardQueryService service = ResolveQueryService(provider);
            return service == null
                ? PlatformTask.FromResult(PlatformResult<LeaderboardPage>.Failure(
                    MissingProviderError(provider, "rank query")), cancellationToken)
                : service.GetRanksAsync(query, cancellationToken);
        }

        public Task<PlatformResult> OpenPlatformLeaderboardAsync(LeaderboardViewRequest request,
            CancellationToken cancellationToken = default)
        {
            return PulletPlatform.TryGet(out ILeaderboardViewService service)
                ? service.OpenLeaderboardAsync(request, cancellationToken)
                : PlatformTask.FromResult(PlatformResult.Failure(
                    "The current platform does not provide a native leaderboard view."), cancellationToken);
        }

        private ILeaderboardScoreService ResolveScoreService(ELeaderboardProvider provider)
        {
            if (provider == ELeaderboardProvider.Backend)
                return _backend;
            if (provider == ELeaderboardProvider.Platform)
                return PulletPlatform.TryGet(out ILeaderboardScoreService requestedPlatform)
                    ? requestedPlatform : null;

            if (CanUsePlatformDataLeaderboard
                && PulletPlatform.TryGet(out ILeaderboardScoreService completePlatform))
                return completePlatform;
            if (_backend != null)
                return _backend;
            return PulletPlatform.TryGet(out ILeaderboardScoreService partialPlatform)
                ? partialPlatform : null;
        }

        private ILeaderboardQueryService ResolveQueryService(ELeaderboardProvider provider)
        {
            if (provider == ELeaderboardProvider.Backend)
                return _backend;
            if (provider == ELeaderboardProvider.Platform)
                return PulletPlatform.TryGet(out ILeaderboardQueryService requestedPlatform)
                    ? requestedPlatform : null;

            return PulletPlatform.TryGet(out ILeaderboardQueryService platform)
                ? platform : _backend;
        }

        private static string MissingProviderError(ELeaderboardProvider provider, string operation)
        {
            return provider == ELeaderboardProvider.Backend
                ? $"No leaderboard backend is installed for {operation}."
                : provider == ELeaderboardProvider.Platform
                    ? $"The current platform does not support leaderboard {operation}."
                    : $"Neither the current platform nor the configured backend supports {operation}.";
        }
    }
}
