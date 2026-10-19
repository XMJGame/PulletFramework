using UnityEngine;

namespace PulletFramework.NetClient.Discovery
{
    // Serialized values are preserved from the retired built-in policy.
    public enum AutoConnectMode : byte
    {
        [InspectorName("直接连接（Direct）")]
        Direct = 0,
        [InspectorName("先发现再连接（DiscoverFirst）")]
        DiscoverFirst = 1,
        [InspectorName("先直连，失败后发现（DirectThenDiscover）")]
        DirectThenDiscover = 2
    }
}
