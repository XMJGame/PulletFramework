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
        private static IPlatformAdapter _adapter;
        private static Task<PlatformResult> _initializationTask;
        private static PlatformPlayerPrefsBackend _storageBackend;
        private static int _session;

        public static bool IsInstalled => PulletPlatform.IsInstalled;
        public static bool IsInitialized => PulletPlatform.IsInitialized;

        public static async Task<PlatformResult> InitializeAsync(IPlatformAdapter adapter,
            CancellationToken cancellationToken = default)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            cancellationToken.ThrowIfCancellationRequested();

            if (!ReferenceEquals(_adapter, adapter) || !PulletPlatform.IsCurrent(adapter))
                BeginSession(adapter);
            else
                EnsureDriver();

            Task<PlatformResult> initialization = _initializationTask;
            if (initialization == null)
            {
                int session = _session;
                initialization = InitializeSessionAsync(adapter, session);
                _initializationTask = initialization;
            }

            try
            {
                return await PlatformTask.WithCancellation(initialization, cancellationToken);
            }
            finally
            {
                if (initialization.IsCompleted
                    && ReferenceEquals(_initializationTask, initialization))
                    _initializationTask = null;
            }
        }

        public static void Shutdown()
        {
            InvalidateSession();
            PulletPlatform.Uninstall();
            GameObject driverObject = _driverObject;
            _driverObject = null;
            if (driverObject == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(driverObject);
            else
                UnityEngine.Object.DestroyImmediate(driverObject);
        }

        private static void BeginSession(IPlatformAdapter adapter)
        {
            InvalidateSession();
            PulletPlatform.Install(adapter);
            _adapter = adapter;
            EnsureDriver();
        }

        private static void InvalidateSession()
        {
            ++_session;
            _initializationTask = null;
            UninstallStorageBackend();
            _adapter = null;
        }

        private static async Task<PlatformResult> InitializeSessionAsync(
            IPlatformAdapter adapter, int session)
        {
            PlatformResult result;
            try
            {
                // 调用者取消只取消自己的等待，不中断其他调用者共享的平台初始化。
                result = await PulletPlatform.InitializeAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                result = PlatformResult.Failure(exception.Message);
            }

            if (session != _session || !ReferenceEquals(_adapter, adapter)
                || !PulletPlatform.IsCurrent(adapter))
            {
                return PlatformResult.Failure(
                    $"Platform initialization for '{adapter.Id}' was superseded by another session.");
            }

            if (!result.Succeeded)
                return result;
            if (!PulletPlatform.IsInitialized)
                return PlatformResult.Failure(
                    $"Platform adapter '{adapter.Id}' reported success without becoming initialized.");

            try
            {
                InstallStorageBackend();
                return result;
            }
            catch (Exception exception)
            {
                UninstallStorageBackend();
                return PlatformResult.Failure(
                    $"Platform '{adapter.Id}' storage initialization failed: {exception.Message}");
            }
        }

        private static void InstallStorageBackend()
        {
            if (_storageBackend != null && PulletPlayerPrefs.IsBackend(_storageBackend))
                return;
            _storageBackend = null;
            if (!PulletPlatform.TryGet(out IPlatformStorageService storage))
                return;

            var backend = new PlatformPlayerPrefsBackend(storage);
            PulletPlayerPrefs.InstallBackend(backend);
            _storageBackend = backend;
            if (PulletSound.IsInitialized)
                PulletSound.LoadPreferences();
        }

        private static void UninstallStorageBackend()
        {
            PlatformPlayerPrefsBackend backend = _storageBackend;
            _storageBackend = null;
            if (backend == null || !PulletPlayerPrefs.IsBackend(backend))
                return;

            if (PulletSound.IsInitialized)
            {
                try { PulletSound.SavePreferences(); }
                catch (Exception exception)
                {
                    PulletFramework.PLogger.Exception(
                        exception, "[PulletMiniGame] 平台音频设置保存失败。");
                }
            }
            PulletPlayerPrefs.UninstallBackend(backend, false);
        }

        private static void EnsureDriver()
        {
            if (_driverObject != null)
                return;

            _driverObject = new GameObject("[PulletMiniGame]");
            _driverObject.AddComponent<PulletMiniGameDriver>();
            if (Application.isPlaying)
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
                    InvalidateSession();
                    PulletPlatform.Uninstall();
                    _driverObject = null;
                }
            }
        }
    }
}
