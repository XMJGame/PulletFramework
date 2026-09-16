using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace PulletFramework.MiniGame.Platform
{
    public enum ELeaderboardPeriod
    {
        Day,
        Week,
        Month,
        All
    }

    public enum ELeaderboardScope
    {
        Friends,
        Global
    }

    public enum ELeaderboardValueType
    {
        Number,
        Tier
    }

    /// <summary>提交给平台榜单的一条成绩。BoardId 在抖音对应 zoneId，在微信对应云存储 key。</summary>
    public readonly struct LeaderboardScore
    {
        public string BoardId { get; }
        public ELeaderboardValueType ValueType { get; }
        public string Value { get; }
        public int Priority { get; }
        public string Extra { get; }

        public LeaderboardScore(string boardId, long score, string extra = null)
            : this(boardId, ELeaderboardValueType.Number,
                score.ToString(CultureInfo.InvariantCulture), 0, extra) { }

        public LeaderboardScore(string boardId, string tier, int priority, string extra = null)
            : this(boardId, ELeaderboardValueType.Tier, tier, priority, extra) { }

        private LeaderboardScore(string boardId, ELeaderboardValueType valueType,
            string value, int priority, string extra)
        {
            BoardId = boardId;
            ValueType = valueType;
            Value = value;
            Priority = priority;
            Extra = extra;
        }
    }

    /// <summary>排行榜查询条件。页码从 1 开始；平台可能进一步限制 PageSize。</summary>
    public readonly struct LeaderboardQuery
    {
        public string BoardId { get; }
        public ELeaderboardPeriod Period { get; }
        public ELeaderboardScope Scope { get; }
        public ELeaderboardValueType ValueType { get; }
        public int PageNumber { get; }
        public int PageSize { get; }

        public LeaderboardQuery(string boardId,
            ELeaderboardPeriod period = ELeaderboardPeriod.All,
            ELeaderboardScope scope = ELeaderboardScope.Global,
            ELeaderboardValueType valueType = ELeaderboardValueType.Number,
            int pageNumber = 1, int pageSize = 20)
        {
            BoardId = boardId;
            Period = period;
            Scope = scope;
            ValueType = valueType;
            PageNumber = pageNumber;
            PageSize = pageSize;
        }
    }

    public readonly struct LeaderboardEntry
    {
        public int Rank { get; }
        public PlatformUser User { get; }
        public string Value { get; }
        public int Priority { get; }
        public string Extra { get; }
        public long UpdatedAtUnixSeconds { get; }
        public bool IsSelf { get; }

        public LeaderboardEntry(int rank, PlatformUser user, string value,
            int priority = 0, string extra = null, long updatedAtUnixSeconds = 0,
            bool isSelf = false)
        {
            Rank = rank;
            User = user;
            Value = value;
            Priority = priority;
            Extra = extra;
            UpdatedAtUnixSeconds = updatedAtUnixSeconds;
            IsSelf = isSelf;
        }
    }

    public readonly struct LeaderboardPage
    {
        public LeaderboardEntry[] Entries { get; }
        public LeaderboardEntry? Self { get; }
        public int PageNumber { get; }
        public int TotalCount { get; }

        public LeaderboardPage(LeaderboardEntry[] entries, LeaderboardEntry? self,
            int pageNumber, int totalCount)
        {
            Entries = entries ?? Array.Empty<LeaderboardEntry>();
            Self = self;
            PageNumber = pageNumber;
            TotalCount = totalCount;
        }
    }

    public readonly struct LeaderboardViewRequest
    {
        public LeaderboardQuery Query { get; }
        public string Title { get; }
        public string Suffix { get; }

        public LeaderboardViewRequest(LeaderboardQuery query, string title = null,
            string suffix = null)
        {
            Query = query;
            Title = title;
            Suffix = suffix;
        }
    }

    public interface ILeaderboardScoreService
    {
        /// <summary>上报当前玩家成绩。调用前应先完成平台登录。</summary>
        Task<PlatformResult> SubmitScoreAsync(LeaderboardScore score,
            CancellationToken cancellationToken = default);
    }

    public interface ILeaderboardQueryService
    {
        /// <summary>获取平台返回的排行榜数据，由游戏自己的 UI 渲染。</summary>
        Task<PlatformResult<LeaderboardPage>> GetRanksAsync(LeaderboardQuery query,
            CancellationToken cancellationToken = default);
    }

    public interface ILeaderboardViewService
    {
        /// <summary>打开平台托管的排行榜界面。</summary>
        Task<PlatformResult> OpenLeaderboardAsync(LeaderboardViewRequest request,
            CancellationToken cancellationToken = default);
    }
}
