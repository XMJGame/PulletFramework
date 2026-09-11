using System;
using System.Threading;
using System.Threading.Tasks;
using PulletFramework.Setting;
using PulletFramework.Sound;
using PulletMiniGame.Platform;
using UnityEngine;

namespace PulletMiniGame
{
    public static class PulletMiniGames
    {
        private static GameObject _driverObject;

        public static bool IsInstalled => PulletPlatform.IsInstalled;
        public static bool IsInitialized => PulletPlatform.IsInitialized;

        public static async Task<PlatformResult> InitializeAsync(IPlatformAdapter adapter,
            CancellationToken cancellationToken = default)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            cancellationToken.ThrowIfCancellationRequested();

            EnsureDriver();
            PulletPlatform.Install(adapter);
            PlatformResult result = await PulletPlatform.InitializeAsync(cancellationToken);
            if (result.Succeeded
                && PulletPlatform.TryGet(out IPlatformStorageService storage))
            {
                PulletPlayerPrefs.InstallBackend(new PlatformPlayerPrefsBackend(storage));
                if (PulletSound.IsInitialized)
                    PulletSound.LoadPreferences();
            }
            return result;
        }

        public static void Shutdown()
        {
            if (PulletPlatform.IsInitialized
                && PulletPlatform.TryGet(out IPlatformStorageService _))
            {
                PulletSound.SavePreferences();
                PulletPlayerPrefs.UninstallBackend();
            }
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
