using System;
using System.Text;
using System.Threading.Tasks;
using PulletNet.ClientSDK;
using UnityEngine;

namespace PulletFramework.NetClient.Samples
{
    public sealed class PulletNetConnectionSample : MonoBehaviour
    {
        [SerializeField] private PulletNetworkManager manager;
        [SerializeField] private string message = "Hello PulletNet";
        private PulletNetworkManager _subscribedManager;

        private void OnEnable()
        {
            _subscribedManager = manager;
            if (_subscribedManager != null) _subscribedManager.PayloadReceived += OnPayloadReceived;
        }

        private void OnDisable()
        {
            if (!ReferenceEquals(_subscribedManager, null)) _subscribedManager.PayloadReceived -= OnPayloadReceived;
            _subscribedManager = null;
        }

        // Application code can await these methods. This sample only borrows the
        // Manager; disabling a UI listener must not shut down someone else's network.
        public Task<ConnectionResult> ConnectAsync() => RequireManager().ConnectAsync();
        public Task<SendResult> SendAsync() => RequireManager().SendAsync(Encoding.UTF8.GetBytes(message)).AsTask();
        public Task<ConnectionResult> DisconnectAsync() => RequireManager().DisconnectAsync();
        public Task ShutdownAsync() => RequireManager().ShutdownAsync();

        [ContextMenu("Connect")]
        public void Connect() => ObserveResult("Connect", ConnectAsync);

        [ContextMenu("Send UTF-8 Message")]
        public void SendMessage() => ObserveResult("Send", SendAsync);

        [ContextMenu("Disconnect")]
        public void Disconnect() => ObserveResult("Disconnect", DisconnectAsync);

        [ContextMenu("Shutdown (Permanent)")]
        public async void Shutdown()
        {
            try { await ShutdownAsync(); Debug.Log("[PulletNet Sample] Shutdown completed."); }
            catch (Exception error) { Debug.LogException(error); }
        }

        // async void exists only at Inspector command boundaries. The awaited
        // operation is owned by Manager; exceptions never escape a UI command.
        private static async void ObserveResult<T>(string operation, Func<Task<T>> command)
        {
            try { var result = await command(); Debug.Log($"[PulletNet Sample] {operation}: {result}"); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private PulletNetworkManager RequireManager()
            => manager != null ? manager : throw new InvalidOperationException("Assign the sample's Manager reference first.");

        private static void OnPayloadReceived(ReceivedPayload value)
        {
            Debug.Log($"[PulletNet Sample] Received {value.Memory.Length} bytes.");
        }
    }
}
