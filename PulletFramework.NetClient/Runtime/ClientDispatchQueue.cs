using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulletNet.ClientSDK;

namespace PulletFramework.NetClient
{
    /// <summary>Only main-thread delivery policy; not SDK connection/session state.</summary>
    internal sealed class ClientDispatchLimits
    {
        internal int MaximumSources = 32;
        internal int MaximumPayloads = 4096;
        internal long MaximumBytes = 16L * 1024 * 1024;
        internal int PayloadsPerSource = 1024;
        internal long BytesPerSource = 4L * 1024 * 1024;
        internal int CallbacksPerFrame = 256;
        internal long BytesPerFrame = 1024 * 1024;

        internal ClientDispatchLimits Freeze()
        {
            var copy = (ClientDispatchLimits)MemberwiseClone();
            if (copy.MaximumSources <= 0 || copy.MaximumPayloads <= 0 || copy.MaximumBytes <= 0 ||
                copy.PayloadsPerSource <= 0 || copy.BytesPerSource <= 0 ||
                copy.CallbacksPerFrame <= 0 || copy.BytesPerFrame <= 0)
                throw new ArgumentOutOfRangeException(nameof(ClientDispatchLimits), "All dispatch limits must be positive.");
            return copy;
        }
    }

    internal enum ClientPayloadAdmission { Accepted, Closed, Dropped, SourceFaulted, CopyFailed, Invalidated }
    internal enum ClientDispatchFailure { None, ReliableOverflow, CopyFailure }

    internal readonly struct ClientDispatchSnapshot
    {
        internal readonly int Sources, Payloads, Preparing, Consuming;
        internal readonly long Bytes, Accepted, Released, Dropped;
        internal ClientDispatchSnapshot(int sources, int payloads, long bytes, int preparing, int consuming,
            long accepted, long released, long dropped)
        {
            Sources = sources; Payloads = payloads; Bytes = bytes; Preparing = preparing;
            Consuming = consuming; Accepted = accepted; Released = released; Dropped = dropped;
        }
    }

    /// <summary>
    /// One bounded payload handoff, with exact HostedClient identity and per-source
    /// delivery epochs. It never connects, disconnects, ACKs or disposes a client.
    /// Counts include copying, queued and borrowed-in-callback memory until Dispose.
    /// </summary>
    internal sealed class ClientDispatchQueue
    {
        internal sealed class Source
        {
            internal readonly ClientDispatchQueue Owner;
            internal readonly HostedClient Client;
            internal readonly Queue<Reservation> Queue = new Queue<Reservation>();
            internal readonly TaskCompletionSource<bool> Exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal long Generation, Bytes;
            internal int Payloads;
            internal bool Accepting, Retired, DeliveryReady = true;
            internal ClientDispatchFailure Failure;
            internal Exception Error;
            internal Source(ClientDispatchQueue owner, HostedClient client) { Owner = owner; Client = client; }
        }

        internal sealed class Reservation
        {
            internal readonly Source Source;
            internal readonly long Generation;
            internal readonly int Length;
            internal OwnedPayload Payload;
            internal Reservation(Source source, int length)
            { Source = source; Length = length; Generation = source.Generation; }
        }

        private readonly object _gate = new object();
        private readonly List<Source> _sources = new List<Source>();
        private readonly ClientDispatchLimits _limits;
        // Controlled copy seam for tests of preparation/close, not another pool.
        private readonly Func<ReceivedPayload, OwnedPayload> _copy;
        private readonly TaskCompletionSource<bool> _closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _nextSource, _payloads, _preparing, _consuming, _draining;
        private long _bytes, _accepted, _released, _dropped;
        private bool _closing;

        internal ClientDispatchQueue(ClientDispatchLimits limits, Func<ReceivedPayload, OwnedPayload> copy = null)
        { _limits = (limits ?? throw new ArgumentNullException(nameof(limits))).Freeze(); _copy = copy; }

        internal Source Register(HostedClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            lock (_gate)
            {
                if (_closing) throw new ObjectDisposedException(nameof(ClientDispatchQueue));
                foreach (var existing in _sources)
                    if (ReferenceEquals(existing.Client, client)) throw new InvalidOperationException("Source is already registered.");
                if (_sources.Count >= _limits.MaximumSources) throw new InvalidOperationException("Dispatch source capacity reached.");
                var source = new Source(this, client);
                _sources.Add(source);
                return source;
            }
        }

        // Called at the SDK's actual new-session boundary, not later in Update.
        // A faulted source cannot be reopened just by receiving Connected again.
        internal long BeginSession(Source source)
        {
            lock (_gate)
            {
                RequireOwner(source);
                if (_closing || source.Retired || source.Failure != ClientDispatchFailure.None)
                    throw new InvalidOperationException("Source delivery is terminal.");
                source.Generation = checked(source.Generation + 1);
                ClearQueued(source);
                source.Accepting = true;
                return source.Generation;
            }
        }

        internal void Invalidate(Source source)
        {
            lock (_gate)
            {
                RequireOwner(source);
                if (source.Retired) return;
                source.Accepting = false;
                source.Generation = checked(source.Generation + 1);
                ClearQueued(source);
            }
        }

        // Reconnecting can preserve a UDP Session. Pause admission without
        // rewriting its delivery epoch; Handshaking explicitly invalidates it.
        internal void Pause(Source source)
        { lock (_gate) { RequireOwner(source); source.Accepting = false; } }

        internal void Resume(Source source)
        {
            lock (_gate)
            {
                RequireOwner(source);
                if (!_closing && !source.Retired && source.Failure == ClientDispatchFailure.None) source.Accepting = true;
            }
        }

        internal long ReadGeneration(Source source)
        { lock (_gate) { RequireOwner(source); return source.Generation; } }

        internal void SetDeliveryReady(Source source, bool ready)
        { lock (_gate) { RequireOwner(source); source.DeliveryReady = ready; } }

        internal ClientPayloadAdmission Enqueue(Source source, ReceivedPayload payload)
        {
            Reservation reservation;
            bool reliable = payload.ChannelType != ChannelType.Unreliable && payload.ChannelType != ChannelType.Sequenced;
            lock (_gate)
            {
                RequireOwner(source);
                if (_closing || source.Retired || !source.Accepting) return ClientPayloadAdmission.Closed;
                int length = payload.Memory.Length;
                // A single oversized item must not remain at a FIFO head forever.
                // Reject it before copying if it cannot fit even a fresh frame.
                if (length > _limits.BytesPerFrame || _payloads >= _limits.MaximumPayloads ||
                    source.Payloads >= _limits.PayloadsPerSource || length > _limits.MaximumBytes - _bytes ||
                    length > _limits.BytesPerSource - source.Bytes)
                {
                    if (reliable) { Fault(source, ClientDispatchFailure.ReliableOverflow, null); return ClientPayloadAdmission.SourceFaulted; }
                    _dropped++;
                    return ClientPayloadAdmission.Dropped; // Never evict another source's data.
                }
                reservation = new Reservation(source, length);
                _payloads++; source.Payloads++; _bytes += length; source.Bytes += length;
                _preparing++; _accepted++;
            }
            OwnedPayload owned = null;
            try
            {
                owned = _copy == null ? payload.ToOwned() : _copy(payload);
                if (owned == null || owned.Memory.Length != reservation.Length || owned.ChannelId != payload.ChannelId ||
                    owned.ChannelType != payload.ChannelType || owned.TransportType != payload.TransportType)
                    throw new InvalidOperationException("Payload copy did not preserve its admitted view.");
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    _preparing--;
                    reservation.Payload = owned;
                    Release(reservation);
                    if (reliable && !source.Retired && source.Generation == reservation.Generation && !_closing)
                        Fault(source, ClientDispatchFailure.CopyFailure, error);
                    else _dropped++;
                }
                return ClientPayloadAdmission.CopyFailed;
            }
            lock (_gate)
            {
                _preparing--;
                reservation.Payload = owned;
                if (_closing || source.Retired || !source.Accepting || reservation.Generation != source.Generation)
                {
                    Release(reservation);
                    return ClientPayloadAdmission.Invalidated;
                }
                try { source.Queue.Enqueue(reservation); }
                catch (Exception error)
                {
                    Release(reservation);
                    if (reliable) Fault(source, ClientDispatchFailure.CopyFailure, error); else _dropped++;
                    return ClientPayloadAdmission.CopyFailed;
                }
                return ClientPayloadAdmission.Accepted;
            }
        }

        /// <summary>One consumer only. No extra ToArray; payload is borrowed until the callback returns.</summary>
        internal int Drain(Action<HostedClient, long, ReceivedPayload> deliver, Action<HostedClient, Exception> onError)
        {
            if (deliver == null) throw new ArgumentNullException(nameof(deliver));
            if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0)
                throw new InvalidOperationException("Dispatch cannot be concurrent or recursive.");
            int delivered = 0;
            long remaining = _limits.BytesPerFrame;
            try
            {
                while (delivered < _limits.CallbacksPerFrame)
                {
                    Reservation item = null;
                    lock (_gate)
                    {
                        for (int inspected = 0; inspected < _sources.Count; inspected++)
                        {
                            Source candidate = _sources[_nextSource];
                            _nextSource = (_nextSource + 1) % _sources.Count;
                            if (!candidate.DeliveryReady || candidate.Queue.Count == 0 || candidate.Queue.Peek().Length > remaining) continue;
                            item = candidate.Queue.Dequeue();
                            _consuming++;
                            break;
                        }
                    }
                    if (item == null) break;
                    remaining -= item.Length;
                    delivered++;
                    try
                    {
                        // Dequeue is callback entry's linearization point. Close
                        // joins this reservation even if preempted before invocation.
                        var owned = item.Payload;
                        deliver(item.Source.Client, item.Generation,
                            new ReceivedPayload(owned.Memory, owned.TransportType, owned.ChannelType, owned.ChannelId));
                    }
                    catch (Exception error)
                    {
                        try { onError?.Invoke(item.Source.Client, error); }
                        catch { /* Diagnostics cannot skip ownership release or other sources. */ }
                    }
                    finally
                    {
                        lock (_gate) { _consuming--; Release(item); }
                    }
                }
                return delivered;
            }
            finally { Volatile.Write(ref _draining, 0); }
        }

        internal Task RetireAsync(Source source)
        {
            lock (_gate)
            {
                RequireOwner(source);
                Retire(source);
                return source.Exited.Task;
            }
        }

        // Requests can originate in delivery callbacks; never synchronously wait
        // for this Task there. It includes preparation and already-entered callbacks.
        internal Task CloseAsync()
        {
            lock (_gate)
            {
                if (!_closing)
                {
                    _closing = true;
                    // Retire can remove an idle source immediately.
                    for (int i = _sources.Count - 1; i >= 0; i--) Retire(_sources[i]);
                    if (_sources.Count == 0) _closed.TrySetResult(true);
                }
                return _closed.Task;
            }
        }

        internal ClientDispatchSnapshot Snapshot()
        {
            lock (_gate) return new ClientDispatchSnapshot(_sources.Count, _payloads, _bytes, _preparing,
                _consuming, _accepted, _released, _dropped);
        }

        internal ClientDispatchFailure Failure(Source source, out Exception error)
        { lock (_gate) { RequireOwner(source); error = source.Error; return source.Failure; } }

        private void Fault(Source source, ClientDispatchFailure failure, Exception error)
        {
            if (source.Failure == ClientDispatchFailure.None) { source.Failure = failure; source.Error = error; }
            source.Accepting = false;
            source.Generation = checked(source.Generation + 1);
            ClearQueued(source);
        }

        private void Retire(Source source)
        {
            if (source.Retired) return;
            source.Retired = true;
            source.Accepting = false;
            source.Generation = checked(source.Generation + 1);
            ClearQueued(source);
            CompleteRetirement(source);
        }

        private void ClearQueued(Source source)
        { while (source.Queue.Count != 0) Release(source.Queue.Dequeue()); }

        // Only a reservation's unique phase owner calls Release. Pool return
        // happens before budget return; no user callback or IO runs under _gate.
        private void Release(Reservation item)
        {
            item.Payload?.Dispose();
            _payloads--; _bytes -= item.Length;
            item.Source.Payloads--; item.Source.Bytes -= item.Length;
            _released++;
            CompleteRetirement(item.Source);
        }

        private void CompleteRetirement(Source source)
        {
            if (!source.Retired || source.Payloads != 0) return;
            int index = _sources.IndexOf(source);
            if (index < 0) return;
            _sources.RemoveAt(index);
            if (index < _nextSource) _nextSource--;
            if (_nextSource >= _sources.Count) _nextSource = 0;
            source.Exited.TrySetResult(true);
            if (_closing && _sources.Count == 0) _closed.TrySetResult(true);
        }

        private void RequireOwner(Source source)
        {
            if (source == null || !ReferenceEquals(source.Owner, this))
                throw new ArgumentException("Source belongs to a different dispatch queue.", nameof(source));
        }
    }
}
