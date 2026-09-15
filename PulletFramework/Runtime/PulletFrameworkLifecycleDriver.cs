using UnityEngine;

namespace PulletFramework
{
    internal sealed class PulletFrameworkLifecycleDriver : MonoBehaviour
    {
        private void Update()
        {
            PulletFrameworks.Update(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            Sound.PulletSound.SetApplicationPaused(pauseStatus);
        }

        private void OnDestroy()
        {
            PulletFrameworks.OnDriverDestroyed(this);
        }
    }
}
