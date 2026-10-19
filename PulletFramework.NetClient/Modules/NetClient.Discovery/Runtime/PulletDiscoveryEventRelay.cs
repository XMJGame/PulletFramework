using System;
using System.Collections.Generic;
using PulletFramework.Discovery;
using UnityEngine;
using UnityEngine.Events;

namespace PulletFramework.NetClient.Discovery
{
    /// <summary>Optional Inspector listeners only; disabling the relay never owns/stops IO.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(PulletDiscoveryConnector))]
    public sealed class PulletDiscoveryEventRelay : MonoBehaviour
    {
        [Serializable] public sealed class AttemptEvent : UnityEvent<int, int> { }
        [Serializable] public sealed class ServersEvent : UnityEvent<DiscoveredServer[]> { }
        [Serializable] public sealed class ErrorEvent : UnityEvent<string> { }
        public UnityEvent onDiscoveryStarted = new UnityEvent();
        public AttemptEvent onDiscoveryAttempt = new AttemptEvent();
        public ServersEvent onDiscoverySucceeded = new ServersEvent();
        public ErrorEvent onDiscoveryFailed = new ErrorEvent();
        public UnityEvent onDiscoveryStopped = new UnityEvent();
        private PulletDiscoveryConnector _discovery;
        private bool _subscribed;
        private void OnEnable()
        {
            if (_subscribed) return;
            _discovery = GetComponent<PulletDiscoveryConnector>();
            _discovery.DiscoveryStarted += Started;
            _discovery.DiscoveryAttempted += Attempt;
            _discovery.DiscoverySucceeded += Succeeded;
            _discovery.DiscoveryFailed += Failed;
            _discovery.DiscoveryStopped += Stopped;
            _subscribed = true;
        }
        private void OnDisable()
        {
            if (!_subscribed || _discovery == null) return;
            _subscribed = false;
            _discovery.DiscoveryStarted -= Started;
            _discovery.DiscoveryAttempted -= Attempt;
            _discovery.DiscoverySucceeded -= Succeeded;
            _discovery.DiscoveryFailed -= Failed;
            _discovery.DiscoveryStopped -= Stopped;
        }
        private void Started() => onDiscoveryStarted?.Invoke();
        private void Attempt(int a, int b) => onDiscoveryAttempt?.Invoke(a, b);
        private void Failed(string error) => onDiscoveryFailed?.Invoke(error);
        private void Stopped() => onDiscoveryStopped?.Invoke();
        private void Succeeded(IReadOnlyList<DiscoveredServer> servers)
        {
            var snapshot = new DiscoveredServer[servers.Count];
            for (int i = 0; i < snapshot.Length; i++) snapshot[i] = servers[i];
            onDiscoverySucceeded?.Invoke(snapshot);
        }
    }
}
