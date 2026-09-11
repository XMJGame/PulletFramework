using System;
using System.Collections;
using System.Collections.Generic;

namespace PulletFramework
{
    public enum EPulletOperationStatus
    {
        None,
        Processing,
        Succeeded,
        Failed
    }

    public abstract class PulletAsyncOperation : IEnumerator
    {
        private Action<PulletAsyncOperation> _completed;
        private bool _completionInvoked;

        public EPulletOperationStatus Status { get; private set; } = EPulletOperationStatus.None;
        public string Error { get; private set; }
        public float Progress { get; protected set; }
        public bool IsDone => Status == EPulletOperationStatus.Succeeded || Status == EPulletOperationStatus.Failed;

        public event Action<PulletAsyncOperation> Completed
        {
            add
            {
                if (value == null)
                    return;
                if (_completionInvoked)
                    value(this);
                else
                    _completed += value;
            }
            remove => _completed -= value;
        }

        public object Current => null;
        public bool MoveNext() => !IsDone;
        public void Reset() { }

        public void WaitForAsyncComplete()
        {
            StartInternal();
            if (!IsDone)
                OnWaitForAsyncComplete();

            int guard = 0;
            while (!IsDone && guard++ < 1000000)
                UpdateInternal();

            if (!IsDone)
                SetFailed($"Operation {GetType().Name} did not complete synchronously.");
            InvokeCompleted();
        }

        public void Abort()
        {
            if (IsDone)
                return;
            OnAbort();
            if (!IsDone)
                SetFailed("Operation aborted.");
            InvokeCompleted();
        }

        protected void SetSucceeded()
        {
            if (IsDone)
                return;
            Progress = 1f;
            Status = EPulletOperationStatus.Succeeded;
        }

        protected void SetFailed(string error)
        {
            if (IsDone)
                return;
            Error = string.IsNullOrEmpty(error) ? "Unknown operation error." : error;
            Status = EPulletOperationStatus.Failed;
        }

        protected void ClearCompletedCallbacks()
        {
            _completed = null;
        }

        protected abstract void OnStart();
        protected abstract void OnUpdate();
        protected virtual void OnAbort() { }
        protected virtual void OnWaitForAsyncComplete() { }

        internal void StartInternal()
        {
            if (Status != EPulletOperationStatus.None)
                return;
            Status = EPulletOperationStatus.Processing;
            try
            {
                OnStart();
            }
            catch (Exception exception)
            {
                SetFailed(exception.ToString());
            }
            if (IsDone)
                InvokeCompleted();
        }

        internal void UpdateInternal()
        {
            StartInternal();
            if (IsDone)
                return;
            try
            {
                OnUpdate();
            }
            catch (Exception exception)
            {
                SetFailed(exception.ToString());
            }
            if (IsDone)
                InvokeCompleted();
        }

        private void InvokeCompleted()
        {
            if (_completionInvoked || !IsDone)
                return;
            _completionInvoked = true;
            Action<PulletAsyncOperation> callback = _completed;
            _completed = null;
            callback?.Invoke(this);
        }
    }

    internal static class PulletOperationSystem
    {
        private static readonly List<PulletAsyncOperation> Operations = new List<PulletAsyncOperation>();

        public static T Start<T>(T operation) where T : PulletAsyncOperation
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            operation.StartInternal();
            if (!operation.IsDone && !Operations.Contains(operation))
                Operations.Add(operation);
            return operation;
        }

        public static void Update()
        {
            for (int i = Operations.Count - 1; i >= 0; i--)
            {
                PulletAsyncOperation operation = Operations[i];
                operation.UpdateInternal();
                if (operation.IsDone)
                    Operations.RemoveAt(i);
            }
        }

        public static void Clear()
        {
            Operations.Clear();
        }
    }
}
