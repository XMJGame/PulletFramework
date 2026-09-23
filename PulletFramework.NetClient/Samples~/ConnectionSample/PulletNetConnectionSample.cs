using System.Text;
using PulletNet.ClientSDK;
using UnityEngine;

namespace PulletFramework.NetClient.Samples
{
    public sealed class PulletNetConnectionSample : MonoBehaviour
    {
        [SerializeField] private PulletNetworkManager manager;
        [SerializeField] private string message = "Hello PulletNet";

        private void OnEnable()
        {
            if (manager != null)
                manager.PayloadReceived += OnPayloadReceived;
        }

        private void OnDisable()
        {
            if (manager != null)
                manager.PayloadReceived -= OnPayloadReceived;
        }

        [ContextMenu("Connect")]
        public async void Connect()
        {
            if (manager == null)
                return;

            ConnectionResult result = await manager.ConnectAsync();
            Debug.Log($"[PulletNet Sample] Connect: {result}");
        }

        [ContextMenu("Send UTF-8 Message")]
        public async void SendMessage()
        {
            if (manager == null)
                return;

            SendResult result = await manager.SendAsync(Encoding.UTF8.GetBytes(message));
            Debug.Log($"[PulletNet Sample] Send: {result}");
        }

        [ContextMenu("Disconnect")]
        public async void Disconnect()
        {
            if (manager == null)
                return;

            ConnectionResult result = await manager.DisconnectAsync();
            Debug.Log($"[PulletNet Sample] Disconnect: {result}");
        }

        private static void OnPayloadReceived(ReceivedPayload value)
        {
            Debug.Log($"[PulletNet Sample] Received {value.Memory.Length} bytes.");
        }
    }
}
