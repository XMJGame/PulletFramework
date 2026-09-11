using System.Collections;
using UnityEngine;

namespace PulletFramework.YooAssetAdapter
{
    /// <summary>可选场景组件；已有唯一 GameLaunch 时，直接调用 Runtime.Initialize 即可。</summary>
    public sealed class PulletYooAssetBootstrap : MonoBehaviour
    {
        [SerializeField] private PulletYooAssetSettings settings;
        [SerializeField] private bool initializeOnStart = true;

        public PulletYooAssetSettings Settings => settings;

        private IEnumerator Start()
        {
            if (initializeOnStart)
                yield return PulletYooAssetRuntime.Initialize(settings);
        }
    }
}
