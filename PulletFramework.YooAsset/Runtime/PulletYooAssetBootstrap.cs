using System.Collections;
using UnityEngine;

namespace PulletFramework.YooAssetAdapter
{
    /// <summary>可选场景组件；已有唯一 GameLaunch 时可直接调用 PulletYooAssets。</summary>
    public sealed class PulletYooAssetBootstrap : MonoBehaviour
    {
        [SerializeField] private bool initializeOnStart = true;

        private IEnumerator Start()
        {
            if (initializeOnStart)
            {
                if (!PulletYooAssets.IsConfigured)
                {
                    PLogger.Error("[PulletYooAsset] 未找到 PulletYooAssetSettings 配置。");
                    yield break;
                }
                yield return PulletYooAssets.PrepareDefaultPackageAsync();
            }
        }
    }
}
