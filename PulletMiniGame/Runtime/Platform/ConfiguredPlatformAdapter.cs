using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulletMiniGame.Platform
{
    public sealed class ConfiguredPlatformAdapter : IPlatformAdapter, IRewardedAdService, IShareService
    {
        private readonly IPlatformAdapter _inner;
        private readonly Dictionary<string, string> _ads;
        private readonly PlatformShareRequest _share;
        public string Id => _inner.Id;
        public EPlatformCapability Capabilities => _inner.Capabilities;
        public bool IsInitialized => _inner.IsInitialized;

        public ConfiguredPlatformAdapter(IPlatformAdapter inner, MiniGameRuntimeSettings settings)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (inner.Id != PulletPlatformIds.Editor && inner.Id != settings.platformId)
                throw new ArgumentException("Runtime settings belong to a different platform.", nameof(settings));
            _ads = settings.CreateAdMap();
            _share = new PlatformShareRequest(settings.shareTitle, settings.shareImageUrl, settings.shareQuery);
        }

        public Task<PlatformResult> InitializeAsync(CancellationToken cancellationToken = default) =>
            _inner.InitializeAsync(cancellationToken);
        public void Update(float deltaTime, float unscaledDeltaTime) => _inner.Update(deltaTime, unscaledDeltaTime);
        public void Shutdown() => _inner.Shutdown();
        public bool TryGetService(Type type, out object service)
        {
            if (!_inner.TryGetService(type, out service)) return false;
            if (type == typeof(IRewardedAdService) || type == typeof(IShareService)) service = this;
            return true;
        }

        private bool Resolve(string name, out string id)
        {
            id = null;
            if (string.IsNullOrWhiteSpace(name) || !_ads.TryGetValue(name, out id)) return false;
            // The simulator has no real ad inventory, but still requires a declared business placement.
            if (Id == PulletPlatformIds.Editor && string.IsNullOrEmpty(id)) id = name;
            return !string.IsNullOrWhiteSpace(id);
        }
        private T Service<T>() where T : class => _inner.TryGetService(typeof(T), out var service) ? service as T : null;
        public bool IsReady(string placementId) => IsInitialized && Resolve(placementId, out var id)
            && Service<IRewardedAdService>()?.IsReady(id) == true;
        public Task<PlatformResult> LoadAsync(string placementId,
            CancellationToken cancellationToken = default)
        {
            var ads = Service<IRewardedAdService>();
            if (!IsInitialized || ads == null || !Resolve(placementId, out var id))
            {
                return PlatformTask.FromResult(PlatformResult.Failure(
                    $"Rewarded placement '{placementId}' is not configured or platform is unavailable."), cancellationToken);
            }
            return ads.LoadAsync(id, cancellationToken);
        }
        public Task<RewardedAdResult> ShowAsync(string placementId,
            CancellationToken cancellationToken = default)
        {
            var ads = Service<IRewardedAdService>();
            if (!IsInitialized || ads == null || !Resolve(placementId, out var id))
            {
                return PlatformTask.FromResult(new RewardedAdResult(ERewardedAdStatus.Failed,
                    $"Rewarded placement '{placementId}' is not configured or platform is unavailable."), cancellationToken);
            }
            return ads.ShowAsync(id, cancellationToken);
        }
        public Task<PlatformResult> ShareAsync(PlatformShareRequest request,
            CancellationToken cancellationToken = default)
        {
            var share = Service<IShareService>();
            if (!IsInitialized || share == null)
            {
                return PlatformTask.FromResult(
                    PlatformResult.Failure("Share service is unavailable."), cancellationToken);
            }
            return share.ShareAsync(new PlatformShareRequest(request.Title ?? _share.Title,
                request.ImageUrl ?? _share.ImageUrl, request.Query ?? _share.Query), cancellationToken);
        }
    }
}
