using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PulletMiniGame.Platform
{
    public class BridgePlatformAdapter : IPlatformAdapter
    {
        private readonly IMiniGameSdkBridge _bridge;
        private TaskCompletionSource<PlatformResult> _initializationSource;
        private bool _initializing;
        private int _generation;

        public string Id => _bridge.PlatformId;
        public EPlatformCapability Capabilities => _bridge.Capabilities;
        public bool IsInitialized { get; private set; }

        public BridgePlatformAdapter(IMiniGameSdkBridge bridge)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        public Task<PlatformResult> InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<PlatformResult>(cancellationToken);
            if (IsInitialized)
                return PlatformTask.FromResult(PlatformResult.Success(), cancellationToken);
            if (_initializing)
                return PlatformTask.WithCancellation(_initializationSource.Task, cancellationToken);
            if (!_bridge.IsAvailable)
                return PlatformTask.FromResult(
                    PlatformResult.Failure($"Platform SDK bridge '{Id}' is unavailable."), cancellationToken);

            _initializing = true;
            _initializationSource = new TaskCompletionSource<PlatformResult>();
            Task<PlatformResult> initializationTask = _initializationSource.Task;
            int generation = ++_generation;
            try
            {
                _bridge.Initialize(result => FinishInitialization(generation, result));
            }
            catch (Exception exception)
            {
                FinishInitialization(generation, PlatformResult.Failure(exception.Message));
            }

            return PlatformTask.WithCancellation(initializationTask, cancellationToken);
        }

        public void Update(float deltaTime, float unscaledDeltaTime)
        {
            if (IsInitialized)
                _bridge.Update(deltaTime, unscaledDeltaTime);
        }

        public void Shutdown()
        {
            ++_generation;
            _initializing = false;
            IsInitialized = false;
            TaskCompletionSource<PlatformResult> source = _initializationSource;
            _initializationSource = null;
            try { _bridge.Shutdown(); }
            finally { source?.TrySetResult(PlatformResult.Failure("Platform shut down during initialization.")); }
        }

        private void FinishInitialization(int generation, PlatformResult result)
        {
            if (!_initializing || generation != _generation)
                return;
            _initializing = false;
            IsInitialized = result.Succeeded;
            TaskCompletionSource<PlatformResult> source = _initializationSource;
            _initializationSource = null;
            if (!result.Succeeded)
            {
                try { _bridge.Shutdown(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            source?.TrySetResult(result);
        }

        public bool TryGetService(Type serviceType, out object service)
        {
            service = null;
            if (serviceType == typeof(IPlayerService) && Supports(EPlatformCapability.Player)
                && _bridge is IPlayerService)
                service = _bridge;
            else if (serviceType == typeof(IRewardedAdService) && Supports(EPlatformCapability.RewardedAd)
                && _bridge is IRewardedAdService)
                service = _bridge;
            else if (serviceType == typeof(IShareService) && Supports(EPlatformCapability.Share)
                && _bridge is IShareService)
                service = _bridge;
            else if (serviceType == typeof(IPlatformStorageService) && Supports(EPlatformCapability.Storage)
                && _bridge is IPlatformStorageService)
                service = _bridge;
            else if (serviceType == typeof(IAnalyticsService) && Supports(EPlatformCapability.Analytics)
                && _bridge is IAnalyticsService)
                service = _bridge;
            else if (serviceType == typeof(IPlatformLifecycleService) && Supports(EPlatformCapability.Lifecycle)
                && _bridge is IPlatformLifecycleService)
                service = _bridge;
            else if (serviceType == typeof(ISidebarRevisitService) && Supports(EPlatformCapability.SidebarRevisit)
                && _bridge is ISidebarRevisitService)
                service = _bridge;
            else if (serviceType == typeof(ILeaderboardScoreService) && Supports(EPlatformCapability.LeaderboardSubmit)
                && _bridge is ILeaderboardScoreService)
                service = _bridge;
            else if (serviceType == typeof(ILeaderboardQueryService) && Supports(EPlatformCapability.LeaderboardQuery)
                && _bridge is ILeaderboardQueryService)
                service = _bridge;
            else if (serviceType == typeof(ILeaderboardViewService) && Supports(EPlatformCapability.LeaderboardView)
                && _bridge is ILeaderboardViewService)
                service = _bridge;
            return service != null;
        }

        private bool Supports(EPlatformCapability capability)
        {
            return (Capabilities & capability) == capability;
        }
    }
}
