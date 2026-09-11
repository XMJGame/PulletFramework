using System;
using System.Threading;
using System.Threading.Tasks;
using PulletMiniGame.Platform;
using UnityEngine;

namespace PulletMiniGame
{
    public static class PulletMiniGames
    {
        private static GameObject _driverObject;

        public static bool IsInstalled => PulletPlatform.IsInstalled;
        public static bool IsInitialized => PulletPlatform.IsInitialized;

        public static Task<PlatformResult> InitializeAsync(IPlatformAdapter adapter,
            CancellationToken cancellationToken = default)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<PlatformResult>(cancellationToken);

            EnsureDriver();
            PulletPlatform.Install(adapter);
            return PulletPlatform.InitializeAsync(cancellationToken);
        }

        public static void Shutdown()
        {
            PulletPlatform.Uninstall();
            if (_driverObject != null)
                UnityEngine.Object.Destroy(_driverObject);
            _driverObject = null;
        }

        private static void EnsureDriver()
        {
            if (_driverObject != null)
                return;

            _driverObject = new GameObject("[PulletMiniGame]");
            _driverObject.AddComponent<PulletMiniGameDriver>();
            UnityEngine.Object.DontDestroyOnLoad(_driverObject);
        }

        private sealed class PulletMiniGameDriver : MonoBehaviour
        {
            private void Update()
            {
                PulletPlatform.Update(Time.deltaTime, Time.unscaledDeltaTime);
            }

            private void OnDestroy()
            {
                if (_driverObject == gameObject)
                {
                    PulletPlatform.Uninstall();
                    _driverObject = null;
                }
            }
        }
    }
}
