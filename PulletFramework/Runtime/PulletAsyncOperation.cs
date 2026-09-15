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
                    InvokeCompletionCallback(value);
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
            try
            {
                OnAbort();
            }
            catch (Exception exception)
            {
                SetFailed(exception.ToString());
            }
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

        /// <summary>供调度器更新之外的显式取消路径写入失败终态并立即通知。</summary>
        protected void CompleteFailed(string error)
        {
            SetFailed(error);
            InvokeCompleted();
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
            if (callback == null)
                return;

            Delegate[] callbacks = callback.GetInvocationList();
            for (int i = 0; i < callbacks.Length; i++)
                InvokeCompletionCallback((Action<PulletAsyncOperation>)callbacks[i]);
        }

        private void InvokeCompletionCallback(Action<PulletAsyncOperation> callback)
        {
            try
            {
                callback(this);
            }
            catch (Exception exception)
            {
                PLogger.Exception(exception,
                    $"Async operation completion callback failed: {GetType().Name}");
            }
        }
    }

    internal static class PulletOperationSystem
    {
        private static readonly List<PulletAsyncOperation> Operations = new List<PulletAsyncOperation>();
        private static int _clearDepth;
        private static bool _isUpdating;
        private static int _version;

        public static T Start<T>(T operation) where T : PulletAsyncOperation
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            operation.StartInternal();
            if (_clearDepth > 0 && !operation.IsDone)
            {
                operation.Abort();
                return operation;
            }
            if (!operation.IsDone && !Operations.Contains(operation))
                Operations.Add(operation);
            return operation;
        }

        public static void Update()
        {
            if (_isUpdating)
                return;

            _isUpdating = true;
            int version = _version;
            try
            {
                for (int i = Operations.Count - 1; i >= 0; i--)
                {
                    PulletAsyncOperation operation = Operations[i];
                    operation.UpdateInternal();
                    if (version != _version)
                        return;
                    if (operation.IsDone)
                        Operations.RemoveAt(i);
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        public static void Clear()
        {
            _clearDepth++;
            _version++;
            try
            {
                PulletAsyncOperation[] pending = Operations.ToArray();
                Operations.Clear();
                for (int i = pending.Length - 1; i >= 0; i--)
                    pending[i].Abort();
            }
            finally
            {
                _clearDepth--;
            }
        }
    }
}
