using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HybridCLR;
using PulletFramework.Resource;
using UnityEngine;

namespace PulletFramework.HybridCLR
{
    /// <summary>与资源系统无关的 HybridCLR DLL 校验与加载入口。</summary>
    public static class PulletHotUpdate
    {
        private static readonly Dictionary<string, string> LoadedHashes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Assembly> LoadedAssemblies =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        /// <summary>从指定资源位置读取清单，加载程序集并执行清单声明的入口。</summary>
        public static PulletHotUpdateOperation InitializeAsync(
            string manifestLocation,
            string packageName = null,
            string playerVersion = null)
        {
            var operation = new PulletHotUpdateOperation();
            PulletFrameworks.StartCoroutine(Run(
                manifestLocation, packageName,
                playerVersion ?? Application.version, operation));
            return operation;
        }

        private static IEnumerator Run(string manifestLocation,
            string packageName, string playerVersion,
            PulletHotUpdateOperation operation)
        {
            // StartCoroutine 会立即执行到首个 yield；延后一帧以允许调用方先订阅进度事件。
            yield return null;

            if (string.IsNullOrWhiteSpace(manifestLocation))
            {
                operation.Fail("热更新清单资源地址不能为空。");
                yield break;
            }
            if (!PulletResources.IsConfigured)
            {
                operation.Fail("PulletResources 尚未安装资源适配器。");
                yield break;
            }

            operation.Report(EPulletHotUpdateStage.LoadingManifest, 0f);
            byte[] manifestBytes = null;
            string manifestError = null;
            IEnumerator manifestLoad;
            try
            {
                manifestLoad = LoadBytes(manifestLocation, packageName,
                    value => manifestBytes = value,
                    error => manifestError = error);
            }
            catch (Exception exception)
            {
                operation.Fail($"加载热更新清单失败：{exception.Message}");
                yield break;
            }
            if (manifestLoad == null)
            {
                operation.Fail("加载热更新清单失败：资源来源返回了空操作。");
                yield break;
            }
            while (true)
            {
                object current;
                try
                {
                    if (!manifestLoad.MoveNext())
                        break;
                    current = manifestLoad.Current;
                }
                catch (Exception exception)
                {
                    operation.Fail($"加载热更新清单失败：{exception.Message}");
                    yield break;
                }
                yield return current;
            }
            if (!string.IsNullOrEmpty(manifestError)
                || manifestBytes == null || manifestBytes.Length == 0)
            {
                operation.Fail($"加载热更新清单失败：{manifestError ?? "返回了空数据"}");
                yield break;
            }

            PulletHotUpdateManifest manifest;
            try
            {
                string json = Encoding.UTF8.GetString(manifestBytes).TrimStart('\uFEFF');
                manifest = PulletHotUpdateManifest.FromJson(json);
            }
            catch (Exception exception)
            {
                operation.Fail($"解析热更新清单失败：{exception.Message}");
                yield break;
            }

            operation.Report(EPulletHotUpdateStage.Validating, 0f);
            string validationError = ValidateAndSort(manifest, playerVersion,
                out List<PulletHotUpdateAssemblyInfo> ordered);
            if (!string.IsNullOrEmpty(validationError))
            {
                operation.Fail(validationError);
                yield break;
            }

            int total = ordered.Count;
            for (int i = 0; i < total; i++)
            {
                PulletHotUpdateAssemblyInfo info = ordered[i];
                EPulletHotUpdateStage stage = info.kind == EPulletHotUpdateAssemblyKind.AotMetadata
                    ? EPulletHotUpdateStage.LoadingAotMetadata
                    : EPulletHotUpdateStage.LoadingHotUpdateAssemblies;
                operation.Report(stage, total == 0 ? 0f : (float)i / total, info.name);

                byte[] bytes = null;
                string loadError = null;
                IEnumerator sourceOperation;
                try
                {
                    sourceOperation = LoadBytes(info.location, packageName,
                        value => bytes = value,
                        error => loadError = error);
                }
                catch (Exception exception)
                {
                    operation.Fail($"加载程序集 {info.name} 失败：{exception.Message}");
                    yield break;
                }
                if (sourceOperation == null)
                {
                    operation.Fail($"加载程序集 {info.name} 失败：字节来源返回了空操作。");
                    yield break;
                }
                while (true)
                {
                    object current;
                    try
                    {
                        if (!sourceOperation.MoveNext())
                            break;
                        current = sourceOperation.Current;
                    }
                    catch (Exception exception)
                    {
                        operation.Fail($"加载程序集 {info.name} 失败：{exception.Message}");
                        yield break;
                    }
                    yield return current;
                }
                if (!string.IsNullOrEmpty(loadError) || bytes == null || bytes.Length == 0)
                {
                    operation.Fail($"加载程序集 {info.name} 失败：{loadError ?? "返回了空数据"}");
                    yield break;
                }
                if (info.byteLength > 0 && bytes.LongLength != info.byteLength)
                {
                    operation.Fail($"程序集 {info.name} 长度校验失败，期望 {info.byteLength}，实际 {bytes.LongLength}。");
                    yield break;
                }

                string hash = ComputeSha256(bytes);
                if (!string.IsNullOrWhiteSpace(info.sha256)
                    && !string.Equals(hash, info.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    operation.Fail($"程序集 {info.name} SHA-256 校验失败。");
                    yield break;
                }

                if (LoadedHashes.TryGetValue(info.name, out string loadedHash))
                {
                    if (!string.Equals(loadedHash, hash, StringComparison.OrdinalIgnoreCase))
                    {
                        operation.Fail($"程序集 {info.name} 已加载另一版本；社区版不支持卸载后重载，请重启 Player。");
                        yield break;
                    }
                    continue;
                }

                if (info.kind == EPulletHotUpdateAssemblyKind.AotMetadata)
                {
#if ENABLE_IL2CPP && !UNITY_EDITOR
                    LoadImageErrorCode code = RuntimeApi.LoadMetadataForAOTAssembly(
                        bytes, HomologousImageMode.SuperSet);
                    if (code != LoadImageErrorCode.OK)
                    {
                        operation.Fail($"程序集 {info.name} 补充元数据失败：{code}。");
                        yield break;
                    }
#endif
                    LoadedHashes.Add(info.name, hash);
                    continue;
                }

                Assembly assembly;
                try
                {
                    assembly = Assembly.Load(bytes);
                }
                catch (Exception exception)
                {
                    operation.Fail($"程序集 {info.name} 装载失败：{exception.GetBaseException().Message}");
                    yield break;
                }

                string actualName = assembly.GetName().Name;
                if (!string.Equals(actualName, info.name, StringComparison.OrdinalIgnoreCase))
                {
                    operation.Fail($"程序集名称不匹配，清单为 {info.name}，DLL 实际为 {actualName}。");
                    yield break;
                }
                LoadedHashes.Add(info.name, hash);
                LoadedAssemblies.Add(info.name, assembly);
            }

            if (!LoadedAssemblies.TryGetValue(manifest.entryAssembly,
                    out Assembly entryAssembly))
            {
                operation.Fail($"未加载入口程序集：{manifest.entryAssembly}。");
                yield break;
            }

            Type entryType = entryAssembly.GetType(manifest.entryType, false);
            if (entryType == null)
            {
                operation.Fail($"入口程序集 {manifest.entryAssembly} 中不存在类型："
                    + manifest.entryType);
                yield break;
            }
            MethodInfo entryMethod;
            try
            {
                entryMethod = entryType.GetMethod(manifest.entryMethod,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
            }
            catch (Exception exception)
            {
                operation.Fail($"查找热更新入口失败：{exception.Message}");
                yield break;
            }
            if (entryMethod == null)
            {
                operation.Fail($"未找到无参数静态热更新入口："
                    + $"{manifest.entryType}.{manifest.entryMethod}。");
                yield break;
            }

            operation.Report(EPulletHotUpdateStage.InvokingEntry, 0.98f,
                manifest.entryAssembly);
            object entryResult;
            try
            {
                entryResult = entryMethod.Invoke(null, null);
            }
            catch (Exception exception)
            {
                operation.Fail("执行热更新入口失败："
                    + exception.GetBaseException().Message);
                yield break;
            }
            if (entryResult is IEnumerator entryRoutine)
            {
                while (true)
                {
                    object current;
                    try
                    {
                        if (!entryRoutine.MoveNext())
                            break;
                        current = entryRoutine.Current;
                    }
                    catch (Exception exception)
                    {
                        operation.Fail("执行热更新入口失败："
                            + exception.GetBaseException().Message);
                        yield break;
                    }
                    yield return current;
                }
            }

            operation.Complete();
        }

        private static IEnumerator LoadBytes(string location, string packageName,
            Action<byte[]> succeeded, Action<string> failed)
        {
            IResourceAssetHandle handle = null;
            try
            {
                handle = PulletResources.LoadAssetAsync<TextAsset>(location, packageName);
            }
            catch (Exception exception)
            {
                failed?.Invoke(exception.Message);
                yield break;
            }

            try
            {
                yield return handle;
                if (!handle.IsSucceeded)
                {
                    failed?.Invoke(handle.Error);
                    yield break;
                }

                var textAsset = handle.AssetObject as TextAsset;
                byte[] bytes = textAsset?.bytes;
                if (bytes == null || bytes.Length == 0)
                    failed?.Invoke($"TextAsset 为空：{location}");
                else
                    succeeded?.Invoke(bytes);
            }
            finally
            {
                handle.Release();
            }
        }

        public static string ValidateAndSort(PulletHotUpdateManifest manifest,
            string playerVersion, out List<PulletHotUpdateAssemblyInfo> ordered)
        {
            ordered = new List<PulletHotUpdateAssemblyInfo>();
            if (manifest == null)
                return "热更新清单不能为空。";
            if (manifest.schemaVersion != PulletHotUpdateManifest.CurrentSchemaVersion)
                return $"不支持热更新清单版本 {manifest.schemaVersion}。";
            if (!manifest.SupportsPlayerVersion(playerVersion))
                return $"热更新内容不适用于 Player {playerVersion}。";
            if (string.IsNullOrWhiteSpace(manifest.entryAssembly)
                || string.IsNullOrWhiteSpace(manifest.entryType)
                || string.IsNullOrWhiteSpace(manifest.entryMethod))
                return "热更新清单缺少入口程序集、类型或方法。";

            PulletHotUpdateAssemblyInfo[] entries = manifest.assemblies
                ?? Array.Empty<PulletHotUpdateAssemblyInfo>();
            var hot = new Dictionary<string, PulletHotUpdateAssemblyInfo>(
                StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Length; i++)
            {
                PulletHotUpdateAssemblyInfo entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.name)
                    || string.IsNullOrWhiteSpace(entry.location))
                    return $"热更新清单第 {i + 1} 项缺少名称或资源地址。";
                if (!names.Add(entry.name))
                    return $"热更新清单包含重复程序集：{entry.name}。";
                if (entry.byteLength <= 0 || !IsSha256(entry.sha256))
                    return $"程序集 {entry.name} 缺少长度或 SHA-256 校验值。";
                if (entry.kind == EPulletHotUpdateAssemblyKind.AotMetadata)
                    ordered.Add(entry);
                else if (entry.kind == EPulletHotUpdateAssemblyKind.HotUpdate)
                    hot.Add(entry.name, entry);
                else
                    return $"程序集 {entry.name} 类型无效：{entry.kind}。";
            }

            var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (PulletHotUpdateAssemblyInfo entry in hot.Values)
            {
                string error = Visit(entry, hot, states, ordered);
                if (!string.IsNullOrEmpty(error))
                    return error;
            }
            if (!hot.ContainsKey(manifest.entryAssembly))
                return $"热更新入口程序集未包含在清单中：{manifest.entryAssembly}。";
            return null;
        }

        private static string Visit(PulletHotUpdateAssemblyInfo entry,
            Dictionary<string, PulletHotUpdateAssemblyInfo> hot,
            Dictionary<string, int> states,
            List<PulletHotUpdateAssemblyInfo> ordered)
        {
            if (states.TryGetValue(entry.name, out int state))
                return state == 1 ? $"热更新程序集存在循环依赖：{entry.name}。" : null;
            states[entry.name] = 1;
            string[] dependencies = entry.dependencies ?? Array.Empty<string>();
            for (int i = 0; i < dependencies.Length; i++)
            {
                if (!hot.TryGetValue(dependencies[i], out PulletHotUpdateAssemblyInfo dependency))
                    return $"热更新程序集 {entry.name} 缺少依赖：{dependencies[i]}。";
                string error = Visit(dependency, hot, states, ordered);
                if (!string.IsNullOrEmpty(error))
                    return error;
            }
            states[entry.name] = 2;
            ordered.Add(entry);
            return null;
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

    }
}
