using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using PulletMiniGame.Editor;
using UnityEditor;
using UnityEngine;

namespace PulletMiniGame.Platform.Douyin.Editor
{
    public sealed class DouyinBuildToolAdapter : IPlatformBuildAdapter
    {
        private const string SettingsTypeName = "TTSDK.Tool.StarkBuilderSettings";
        private const string BuildManagerTypeName = "TTSDK.Tool.API.BuildManager";
        private const int DeleteRetryCount = 4;

        public string Id => PulletPlatformIds.Douyin;
        public string DisplayName => "抖音小游戏官方 TTSDK";
        public string ActionLabel => "构建抖音小游戏";

        public bool IsAvailable(out string reason)
        {
            Type settingsType = FindType(SettingsTypeName);
            Type buildManagerType = FindType(BuildManagerTypeName);
            if (settingsType == null || FindBuildMethod(buildManagerType) == null)
            {
                reason = "未检测到 TTSDK BuildManager，请通过 BGDT 安装最新版 TTSDK。";
                return false;
            }

            if (GetSettings(settingsType) == null)
            {
                reason = "TTSDK 构建配置对象不可用，请先打开一次 ByteGame/TTSDKTools/Build Tool。";
                return false;
            }

            reason = null;
            return true;
        }

        public void Export(PlatformBuildContext context)
        {
            if (!(context.Settings is DouyinPlatformSettings settings))
                throw new ArgumentException("DouyinPlatformSettings is required.", nameof(context));

            Type settingsType = FindType(SettingsTypeName)
                ?? throw new InvalidOperationException("Douyin TTSDK is not installed.");
            Type buildManagerType = FindType(BuildManagerTypeName)
                ?? throw new InvalidOperationException("Douyin TTSDK BuildManager is unavailable.");
            MethodInfo buildMethod = FindBuildMethod(buildManagerType)
                ?? throw new MissingMethodException(BuildManagerTypeName, "Build(Framework, bool)");
            object sdkSettings = GetSettings(settingsType)
                ?? throw new InvalidOperationException("Unable to load StarkBuilderSettings.");

            string outputPath = ResolveOutputPath(context.OutputPath);
            PrepareOutputDirectory(outputPath, context.CleanOutput);
            ApplyConfiguration(sdkSettings, context, settings, outputPath);
            SaveSettings(sdkSettings);

            object wasm = Enum.Parse(buildMethod.GetParameters()[0].ParameterType, "Wasm");
            object invocation;
            try
            {
                invocation = buildMethod.Invoke(null, new[] { wasm, (object)false });
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException(
                    "Douyin mini game build failed.", exception.InnerException ?? exception);
            }

            if (!(invocation is Task task))
                throw new InvalidOperationException("TTSDK BuildManager returned an unexpected result.");

            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Douyin mini game build failed.", exception);
            }

            string artifactPath = invocation.GetType().GetProperty("Result")?.GetValue(invocation) as string;
            if (string.IsNullOrWhiteSpace(artifactPath))
                throw new InvalidOperationException("TTSDK completed without returning an artifact path.");

            InjectLaunchProgressLogging(artifactPath);
            PulletFramework.PLogger.EditorInfo(
                $"[PulletMiniGame] Douyin mini game build completed: {artifactPath}");
        }

        private static void ApplyConfiguration(
            object target,
            PlatformBuildContext context,
            DouyinPlatformSettings settings,
            string outputPath)
        {
            SetRequired(target, "appId", settings.appId);
            SetRequired(target, "OutputDir", outputPath);
            SetRequired(target, "wasmMemorySize", settings.initialMemoryMb);
            SetRequired(target, "isWebGL2", true);
            SetRequired(target, "needCompress", true);
            SetRequired(target, "isDevBuild", context.DevelopmentBuild);
            SetRequired(target, "buildOptions",
                context.DevelopmentBuild ? BuildOptions.Development : BuildOptions.None);
            TrySet(target, "profiling", false);
            TrySet(target, "symbolMode", context.DevelopmentBuild ? 1 : 0);
            TrySet(target, "stripEngineCode", !context.DevelopmentBuild);
            SetRequired(target, "orientation", settings.orientation.ToString());
            SetRequired(target, "menuButtonStyle", settings.menuButtonStyle.ToString());
            SetRequired(target, "isOldBuildFormat", settings.useLegacyBuildFormat);
            SetRequired(target, "iOSPerformancePlus", settings.iosHighPerformancePlus);
            TrySet(target, "idePath", settings.developerToolPath ?? string.Empty);
            TrySet(target, "CDN", settings.firstPackageResourceMode == EFirstPackageResourceMode.Cdn
                ? settings.cdnUrl ?? string.Empty
                : string.Empty);
        }

        private static void InjectLaunchProgressLogging(string artifactPath)
        {
            string gameScriptPath = Path.Combine(artifactPath, "game.js");
            if (!File.Exists(gameScriptPath))
                return;

            const string callback = "gameManager.onLaunchProgress((e) => {";
            const string marker = "console.log('[PulletStartup]'";
            string contents = File.ReadAllText(gameScriptPath);
            if (contents.Contains(marker) || !contents.Contains(callback))
                return;

            contents = contents.Replace(
                callback,
                callback + Environment.NewLine
                    + "    console.log('[PulletStartup]', e.type, e.data);");
            File.WriteAllText(gameScriptPath, contents, new UTF8Encoding(false));
        }

        private static MethodInfo FindBuildMethod(Type buildManagerType)
        {
            return buildManagerType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.Name == "Build"
                        && parameters.Length == 2
                        && parameters[0].ParameterType.FullName == "TTSDK.Tool.Framework"
                        && parameters[1].ParameterType == typeof(bool)
                        && typeof(Task).IsAssignableFrom(method.ReturnType);
                });
        }

        private static object GetSettings(Type settingsType)
        {
            object instance = settingsType.GetProperty(
                "Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            return instance ?? settingsType.GetMethod(
                "LoadSettings", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        }

        private static void SaveSettings(object settings)
        {
            settings.GetType().GetMethod(
                "Save", BindingFlags.Public | BindingFlags.Instance)?.Invoke(settings, null);
            if (settings is UnityEngine.Object asset)
                EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        private static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
        }

        private static string ResolveOutputPath(string configuredPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(projectRoot, configuredPath));
        }

        private static void PrepareOutputDirectory(string outputPath, bool cleanOutput)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedOutput = Path.GetFullPath(outputPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(projectRoot, normalizedOutput, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Douyin output directory cannot be the Unity project root.");

            if (cleanOutput && Directory.Exists(normalizedOutput))
                ClearDirectoryContents(normalizedOutput);
            Directory.CreateDirectory(normalizedOutput);
        }

        private static void ClearDirectoryContents(string directoryPath)
        {
            try
            {
                foreach (string filePath in Directory.EnumerateFiles(directoryPath))
                    DeleteFileWithRetry(filePath);

                foreach (string childPath in Directory.EnumerateDirectories(directoryPath))
                {
                    var child = new DirectoryInfo(childPath);
                    if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
                        ClearDirectoryContents(childPath);
                    TryDeleteEmptyDirectory(childPath);
                }
            }
            catch (UnauthorizedAccessException exception)
            {
                throw CreateOutputLockedException(directoryPath, exception);
            }
            catch (IOException exception)
            {
                throw CreateOutputLockedException(directoryPath, exception);
            }
        }

        private static void DeleteFileWithRetry(string filePath)
        {
            Exception lastError = null;
            for (int attempt = 0; attempt < DeleteRetryCount; attempt++)
            {
                try
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                    File.Delete(filePath);
                    return;
                }
                catch (Exception exception) when (
                    exception is IOException || exception is UnauthorizedAccessException)
                {
                    lastError = exception;
                    if (attempt + 1 < DeleteRetryCount)
                        System.Threading.Thread.Sleep(150 * (attempt + 1));
                }
            }

            throw CreateOutputLockedException(filePath, lastError);
        }

        private static void TryDeleteEmptyDirectory(string directoryPath)
        {
            try
            {
                Directory.Delete(directoryPath, false);
            }
            catch (IOException)
            {
                // 开发者工具可能占用输出目录；内容已清空时保留目录不影响覆盖构建。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上，真正无法删除的文件已在 DeleteFileWithRetry 中报告。
            }
        }

        private static InvalidOperationException CreateOutputLockedException(
            string path, Exception innerException)
        {
            return new InvalidOperationException(
                $"无法清理抖音输出：{path}\n请停止开发者工具中的编译/预览；若仍失败，关闭抖音开发者工具后重试。",
                innerException);
        }

        private static void SetRequired(object target, string name, object value)
        {
            if (!TrySet(target, name, value))
                throw new MissingMemberException(target.GetType().FullName, name);
        }

        private static bool TrySet(object target, string name, object value)
        {
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, ConvertValue(value, property.PropertyType));
                return true;
            }

            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
                return false;
            field.SetValue(target, ConvertValue(value, field.FieldType));
            return true;
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null || targetType.IsInstanceOfType(value))
                return value;
            if (targetType.IsEnum)
                return value is string name
                    ? Enum.Parse(targetType, name)
                    : Enum.ToObject(targetType, value);
            return Convert.ChangeType(value, targetType);
        }
    }
}
