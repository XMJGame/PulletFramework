using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulletMiniGame.Platform
{
    public static class PulletPlatform
    {
        private static IPlatformAdapter _adapter;

        public static bool IsInstalled => _adapter != null;
        public static bool IsInitialized => _adapter != null && _adapter.IsInitialized;
        public static string Id => _adapter?.Id ?? "none";
        public static EPlatformCapability Capabilities => _adapter?.Capabilities ?? EPlatformCapability.None;

        internal static IPlatformAdapter Adapter => _adapter;

        internal static bool IsCurrent(IPlatformAdapter adapter) =>
            adapter != null && ReferenceEquals(_adapter, adapter);

        public static void Install(IPlatformAdapter adapter)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            if (ReferenceEquals(_adapter, adapter))
                return;

            _adapter?.Shutdown();
            _adapter = adapter;
        }

        public static Task<PlatformResult> InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (IsInitialized)
                return PlatformTask.FromResult(PlatformResult.Success(), cancellationToken);
            if (_adapter == null)
                return PlatformTask.FromResult(PlatformResult.Failure(
                    "No platform adapter is installed. Call PulletPlatform.Install() during bootstrap."),
                    cancellationToken);

            return _adapter.InitializeAsync(cancellationToken);
        }

        public static bool Supports(EPlatformCapability capability)
        {
            return (Capabilities & capability) == capability;
        }

        public static bool TryGet<TService>(out TService service) where TService : class
        {
            if (IsInitialized
                && _adapter.TryGetService(typeof(TService), out object value)
                && value is TService typedService)
            {
                service = typedService;
                return true;
            }

            service = null;
            return false;
        }

        public static TService Get<TService>() where TService : class
        {
            if (TryGet(out TService service))
                return service;
            throw new InvalidOperationException(
                $"Platform '{Id}' does not provide {typeof(TService).Name}.");
        }

        public static void Uninstall()
        {
            IPlatformAdapter adapter = _adapter;
            _adapter = null;
            adapter?.Shutdown();
        }

        internal static void Update(float deltaTime, float unscaledDeltaTime)
        {
            _adapter?.Update(deltaTime, unscaledDeltaTime);
        }
    }
}
