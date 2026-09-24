using System;
using System.Threading;
using NUnit.Framework;

namespace PulletFramework.NetClient.Tests
{
    public sealed class PulletConnectionCoordinatorTests
    {
        [Test]
        public void Disconnect_CancelsPendingConnectionAndInvalidatesItsVersion()
        {
            var coordinator = new PulletConnectionCoordinator();
            using (var lifetime = new CancellationTokenSource())
            {
                var connecting = coordinator.BeginConnection(lifetime.Token);
                long disconnectVersion = coordinator.BeginDisconnect();

                Assert.That(connecting.Token.IsCancellationRequested, Is.True);
                Assert.That(coordinator.IsCurrent(connecting.Version), Is.False);
                Assert.That(coordinator.IsCurrent(disconnectVersion), Is.True);
                coordinator.Complete(connecting);
            }
        }

        [Test]
        public void NewConnection_CancelsPreviousIntentWithoutInvalidatingNewOne()
        {
            var coordinator = new PulletConnectionCoordinator();
            using (var lifetime = new CancellationTokenSource())
            {
                var first = coordinator.BeginConnection(lifetime.Token);
                var second = coordinator.BeginConnection(lifetime.Token);

                Assert.That(first.Token.IsCancellationRequested, Is.True);
                Assert.That(coordinator.IsCurrent(first.Version), Is.False);
                Assert.That(coordinator.IsCurrent(second.Version), Is.True);
                coordinator.Complete(first);
                Assert.That(coordinator.IsCurrent(second.Version), Is.True);
                coordinator.Complete(second);
            }
        }

        [Test]
        public void RequestCancellation_CancelsOnlyLinkedIntent()
        {
            var coordinator = new PulletConnectionCoordinator();
            using (var lifetime = new CancellationTokenSource())
            using (var request = new CancellationTokenSource())
            {
                var intent = coordinator.BeginConnection(lifetime.Token, request.Token);
                request.Cancel();

                Assert.That(intent.Token.IsCancellationRequested, Is.True);
                Assert.That(lifetime.IsCancellationRequested, Is.False);
                coordinator.Complete(intent);
            }
        }
    }
}
