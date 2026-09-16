using System;
using UnityEngine;

namespace PulletFramework.HybridCLR
{
    public enum EPulletHotUpdateStage
    {
        None,
        LoadingManifest,
        Validating,
        LoadingAotMetadata,
        LoadingHotUpdateAssemblies,
        InvokingEntry,
        Completed,
        Failed
    }

    public sealed class PulletHotUpdateOperation : CustomYieldInstruction
    {
        public override bool keepWaiting => !IsDone;
        public bool IsDone { get; internal set; }
        public bool Succeeded => IsDone && Stage == EPulletHotUpdateStage.Completed;
        public EPulletHotUpdateStage Stage { get; internal set; }
        public float Progress { get; internal set; }
        public string CurrentAssembly { get; internal set; }
        public string Error { get; internal set; }

        public event Action<PulletHotUpdateOperation> ProgressChanged;
        public event Action<PulletHotUpdateOperation> Completed;

        internal void Report(EPulletHotUpdateStage stage, float progress, string assemblyName = null)
        {
            Stage = stage;
            Progress = Mathf.Clamp01(progress);
            CurrentAssembly = assemblyName;
            InvokeSafely(ProgressChanged);
        }

        internal void Complete()
        {
            IsDone = true;
            Report(EPulletHotUpdateStage.Completed, 1f);
            InvokeSafely(Completed);
        }

        internal void Fail(string error)
        {
            Error = string.IsNullOrWhiteSpace(error) ? "未知热更新加载错误。" : error;
            IsDone = true;
            Report(EPulletHotUpdateStage.Failed, Progress, CurrentAssembly);
            InvokeSafely(Completed);
        }

        private void InvokeSafely(Action<PulletHotUpdateOperation> callback)
        {
            if (callback == null)
                return;
            Delegate[] listeners = callback.GetInvocationList();
            for (int i = 0; i < listeners.Length; i++)
            {
                try
                {
                    ((Action<PulletHotUpdateOperation>)listeners[i])(this);
                }
                catch (Exception exception)
                {
                    PLogger.Exception(exception, "HybridCLR operation callback failed.");
                }
            }
        }
    }
}
