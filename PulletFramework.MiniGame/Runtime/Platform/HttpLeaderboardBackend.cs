using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PulletFramework.Network;
using UnityEngine;

namespace PulletFramework.MiniGame.Platform
{
    /// <summary>自建排行榜 HTTP 接口配置。Token 提供器应返回业务服务器签发的会话令牌。</summary>
    public sealed class HttpLeaderboardBackendOptions
    {
        public string BaseUrl { get; set; }
        public string SubmitPath { get; set; } = "/v1/leaderboards/score";
        public string QueryPath { get; set; } = "/v1/leaderboards/query";
        public Func<string> AccessTokenProvider { get; set; }
        public int TimeoutSeconds { get; set; } = 10;
        public int MaxRetries { get; set; } = 1;
    }

    /// <summary>
    /// 基于 PulletHttp 的自建排行榜客户端。服务器必须根据登录会话识别玩家，不能信任客户端传入的身份。
    /// </summary>
    public sealed class HttpLeaderboardBackend : ILeaderboardBackend
    {
        [Serializable]
        private sealed class ScoreRequest
        {
            public string boardId;
            public int valueType;
            public string value;
            public int priority;
            public string extra;
        }

        [Serializable]
        private sealed class QueryRequest
        {
            public string boardId;
            public int period;
            public int scope;
            public int valueType;
            public int pageNumber;
            public int pageSize;
        }

        [Serializable]
        private sealed class BasicResponse
        {
            public bool success;
            public string error;
        }

        [Serializable]
        private sealed class QueryResponse
        {
            public bool success;
            public string error;
            public PageData data;
        }

        [Serializable]
        private sealed class PageData
        {
            public EntryData[] entries;
            public EntryData self;
            public int pageNumber;
            public int totalCount;
        }

        [Serializable]
        private sealed class EntryData
        {
            public int rank;
            public string userId;
            public string nickname;
            public string avatarUrl;
            public string value;
            public int priority;
            public string extra;
            public long updatedAtUnixSeconds;
            public bool isSelf;
        }

        private readonly HttpLeaderboardBackendOptions _options;

        public HttpLeaderboardBackend(HttpLeaderboardBackendOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
                throw new ArgumentException("Leaderboard backend BaseUrl is required.", nameof(options));
        }

        public async Task<PlatformResult> SubmitScoreAsync(LeaderboardScore score,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(score.BoardId))
                return PlatformResult.Failure("Leaderboard board id is required.");

            var body = new ScoreRequest
            {
                boardId = score.BoardId,
                valueType = (int)score.ValueType,
                value = score.Value,
                priority = score.Priority,
                extra = score.Extra
            };
            HttpResponse response = await SendAsync(_options.SubmitPath,
                JsonUtility.ToJson(body), cancellationToken);
            if (!response.IsSuccess)
                return PlatformResult.Failure(FormatHttpError(response));

            BasicResponse result = Parse<BasicResponse>(response.Text);
            return result != null && result.success
                ? PlatformResult.Success()
                : PlatformResult.Failure(result?.error ?? "Leaderboard server returned an invalid response.");
        }

        public async Task<PlatformResult<LeaderboardPage>> GetRanksAsync(LeaderboardQuery query,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query.BoardId))
                return PlatformResult<LeaderboardPage>.Failure("Leaderboard board id is required.");
            if (query.PageNumber < 1 || query.PageSize < 1)
                return PlatformResult<LeaderboardPage>.Failure("Leaderboard paging values must be positive.");

            var body = new QueryRequest
            {
                boardId = query.BoardId,
                period = (int)query.Period,
                scope = (int)query.Scope,
                valueType = (int)query.ValueType,
                pageNumber = query.PageNumber,
                pageSize = query.PageSize
            };
            HttpResponse response = await SendAsync(_options.QueryPath,
                JsonUtility.ToJson(body), cancellationToken);
            if (!response.IsSuccess)
                return PlatformResult<LeaderboardPage>.Failure(FormatHttpError(response));

            QueryResponse result = Parse<QueryResponse>(response.Text);
            if (result == null || !result.success || result.data == null)
                return PlatformResult<LeaderboardPage>.Failure(
                    result?.error ?? "Leaderboard server returned an invalid response.");

            EntryData[] source = result.data.entries ?? Array.Empty<EntryData>();
            var entries = new LeaderboardEntry[source.Length];
            for (int i = 0; i < source.Length; i++) entries[i] = Convert(source[i]);
            LeaderboardEntry? self = result.data.self == null
                ? (LeaderboardEntry?)null : Convert(result.data.self);
            return PlatformResult<LeaderboardPage>.Success(new LeaderboardPage(
                entries, self, result.data.pageNumber, result.data.totalCount));
        }

        private Task<HttpResponse> SendAsync(string path, string json,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<HttpResponse>(cancellationToken);

            var request = HttpRequest.Post(BuildUrl(path), Encoding.UTF8.GetBytes(json),
                "application/json; charset=utf-8");
            request.TimeoutSeconds = Math.Max(1, _options.TimeoutSeconds);
            request.MaxRetries = Math.Max(0, _options.MaxRetries);
            string token = _options.AccessTokenProvider?.Invoke();
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers["Authorization"] = "Bearer " + token;

            var source = new TaskCompletionSource<HttpResponse>();
            CancellationTokenRegistration registration = default;
            HttpRequestHandle handle = PulletHttp.Send(request, response =>
            {
                registration.Dispose();
                source.TrySetResult(response);
            });
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() =>
                {
                    handle.Cancel();
                    source.TrySetCanceled();
                });
            }
            return source.Task;
        }

        private string BuildUrl(string path) =>
            _options.BaseUrl.TrimEnd('/') + "/" + (path ?? string.Empty).TrimStart('/');

        private static T Parse<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception) { return null; }
        }

        private static LeaderboardEntry Convert(EntryData entry) => new LeaderboardEntry(
            entry.rank, new PlatformUser(entry.userId, entry.nickname, entry.avatarUrl),
            entry.value, entry.priority, entry.extra, entry.updatedAtUnixSeconds, entry.isSelf);

        private static string FormatHttpError(HttpResponse response) =>
            string.IsNullOrWhiteSpace(response.Error)
                ? $"Leaderboard HTTP request failed with status {response.StatusCode}."
                : response.Error;
    }
}
