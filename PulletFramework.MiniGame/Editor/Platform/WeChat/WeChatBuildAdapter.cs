using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PulletFramework.MiniGame.Editor;
using UnityEditor;
using UnityEngine;

namespace PulletFramework.MiniGame.Platform.WeChat.Editor
{
    public sealed class WeChatBuildAdapter : IPlatformBuildAdapter
    {
        private const string ConvertCoreTypeName = "WeChatWASM.WXConvertCore";

        public string Id => PulletPlatformIds.WeChat;
        public string DisplayName => "微信小游戏官方转换 SDK";
        public string ActionLabel => "导出微信小游戏";

        public bool IsAvailable(out string reason)
        {
            Type coreType = FindType(ConvertCoreTypeName);
            if (coreType == null)
            {
                reason = "未安装 com.qq.weixin.minigame 官方转换 SDK。";
                return false;
            }

            if (FindExportMethod(coreType) == null)
            {
                reason = "已发现微信 SDK，但当前版本未公开 DoExport(bool) 接口。";
                return false;
            }

            try
            {
                object config = GetStaticMember(coreType, "config");
                object project = config == null ? null : GetMember(config, "ProjectConf");
                object compile = config == null ? null : GetMember(config, "CompileOptions");
                if (project == null || compile == null
                    || !HasWritableMember(project, "Appid")
                    || !HasWritableMember(project, "DST")
                    || !HasWritableMember(project, "relativeDST")
                    || !HasWritableMember(compile, "DevelopBuild"))
                {
                    reason = "微信 SDK 配置结构与当前适配器不兼容，请升级适配器或切换已验证的 SDK 版本。";
                    return false;
                }
            }
            catch (Exception exception)
            {
                reason = $"读取微信 SDK 配置失败：{exception.Message}";
                return false;
            }

            reason = null;
            return true;
        }

        public void Export(PlatformBuildContext context)
        {
            if (!(context.Settings is WeChatPlatformSettings settings))
                throw new ArgumentException("WeChatPlatformSettings is required.", nameof(context));

            Type coreType = FindType(ConvertCoreTypeName)
                ?? throw new InvalidOperationException("WeChat mini game SDK is not installed.");
            MethodInfo exportMethod = FindExportMethod(coreType)
                ?? throw new MissingMethodException(ConvertCoreTypeName, "DoExport(bool)");

            object config = GetStaticMember(coreType, "config")
                ?? throw new InvalidOperationException("Unable to load the WeChat MiniGameConfig asset.");
            ApplyConfiguration(config, context, settings);

            if (config is UnityEngine.Object configAsset)
                EditorUtility.SetDirty(configAsset);
            AssetDatabase.SaveAssets();

            object result;
            try
            {
                result = exportMethod.Invoke(null, new object[] { true });
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException(
                    "WeChat SDK export failed.", exception.InnerException ?? exception);
            }
            if (Convert.ToInt32(result) != 0)
                throw new InvalidOperationException($"WeChat export failed: {result}");

            string miniGameOutput = ResolveMiniGameOutputPath(context.OutputPath);
            MiniGameFirstPackageCdnPublisher.ReconcileExport(Id, settings);
            MiniGameLoadingPagePostprocessor.Apply(miniGameOutput, settings.showDefaultUnityLoadingLogo);
            if (settings.enableNativeLeaderboard)
                PatchOpenDataTemplate(miniGameOutput, settings.nativeLeaderboardKey);

            PulletFramework.PLogger.EditorInfo(
                $"[PulletFramework.MiniGame] WeChat mini game build completed: {miniGameOutput}");
        }

        private static void ApplyConfiguration(
            object config,
            PlatformBuildContext context,
            WeChatPlatformSettings settings)
        {
            object project = GetMember(config, "ProjectConf")
                ?? throw new MissingMemberException(config.GetType().FullName, "ProjectConf");
            object compile = GetMember(config, "CompileOptions")
                ?? throw new MissingMemberException(config.GetType().FullName, "CompileOptions");
            object sdkOptions = GetMember(config, "SDKOptions");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputPath = Path.IsPathRooted(context.OutputPath)
                ? Path.GetFullPath(context.OutputPath)
                : Path.GetFullPath(Path.Combine(projectRoot, context.OutputPath));
            string relativeOutput = Path.GetRelativePath(projectRoot, outputPath).Replace('\\', '/');

            SetRequired(project, "Appid", settings.appId);
            TrySet(project, "projectName", settings.gameName);
            SetRequired(project, "DST", outputPath);
            SetRequired(project, "relativeDST", relativeOutput);
            TrySet(project, "CDN", settings.firstPackageResourceMode == EFirstPackageResourceMode.Cdn
                ? EnsureTrailingSlash(settings.cdnUrl)
                : string.Empty);
            TrySet(project, "assetLoadType",
                settings.firstPackageResourceMode == EFirstPackageResourceMode.Cdn ? 0 : 1);
            TrySet(project, "dataFileSubPrefix", string.Empty);
            TrySet(project, "StreamCDN", string.Empty);
            TrySet(project, "compressDataPackage", true);
            TrySet(project, "MemorySize", settings.initialMemoryMb);
            TrySet(project, "Orientation", settings.orientation == EMiniGameOrientation.Portrait ? 0 : 1);

            if (settings.startupImage != null)
                TrySet(project, "bgImageSrc", AssetDatabase.GetAssetPath(settings.startupImage));

            TrySet(compile, "DevelopBuild", context.DevelopmentBuild);
            TrySet(compile, "AutoProfile", false);
            TrySet(compile, "ProfilingMemory", false);
            TrySet(compile, "profilingFuncs", false);
            TrySet(compile, "ScriptOnly", false);
            TrySet(compile, "Il2CppOptimizeSize", !context.DevelopmentBuild);
            TrySet(compile, "Webgl2", true);
            TrySet(compile, "DeleteStreamingAssets", !MiniGameYooAssetBuiltinIntegration.IsEnabled);
            // 保留 Unity 构建缓存。清理小游戏输出目录不应触发完整 IL2CPP 重编译。
            TrySet(compile, "CleanBuild", false);
            TrySet(compile, "enableIOSPerformancePlus", settings.iosHighPerformancePlus);
            if (sdkOptions != null)
                TrySet(sdkOptions, "UseFriendRelation", settings.enableNativeLeaderboard);
        }

        private static string ResolveOutputPath(string configuredPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(projectRoot, configuredPath));
        }

        private static string ResolveMiniGameOutputPath(string configuredPath)
        {
            return Path.Combine(ResolveOutputPath(configuredPath), "minigame");
        }

        private static void PatchOpenDataTemplate(string outputPath, string leaderboardKey)
        {
            string indexPath = Path.Combine(outputPath, "open-data", "index.js");
            if (!File.Exists(indexPath))
                throw new FileNotFoundException(
                    "微信导出结果中没有开放数据域模板，请确认 SDKOptions.UseFriendRelation 已启用。", indexPath);

            string content = File.ReadAllText(indexPath, Encoding.UTF8);
            ReplaceRequired(ref content, "const RANK_KEY = 'user_rank';",
                $"let rankKey = '{leaderboardKey}';");
            ReplaceRequired(ref content,
                "setUserRecord(RANK_KEY, Math.ceil(Math.random() * 1000));", string.Empty);
            ReplaceRequired(ref content, "getFriendRankData(RANK_KEY)", "getFriendRankData(rankKey)");
            ReplaceRequired(ref content, "getGroupFriendsRankData(shareTicket, RANK_KEY)",
                "getGroupFriendsRankData(shareTicket, rankKey)");
            ReplaceRequired(ref content, "setUserRecord(RANK_KEY, data.score)",
                "setUserRecord(data.key || rankKey, data.score)");
            ReplaceRequired(ref content, "                renderFriendsRank();",
                "                rankKey = data.key || rankKey;\n                renderFriendsRank();");
            ReplaceRequired(ref content, "                renderGroupFriendsRank(data.shareTicket);",
                "                rankKey = data.key || rankKey;\n                renderGroupFriendsRank(data.shareTicket);");
            File.WriteAllText(indexPath, content, new UTF8Encoding(false));
        }

        private static void ReplaceRequired(ref string content, string oldValue, string newValue)
        {
            if (!content.Contains(oldValue))
                throw new InvalidOperationException(
                    "微信 SDK 的开放数据域模板结构已经变化，请升级 PulletFramework.MiniGame 适配器。缺少片段：" + oldValue);
            content = content.Replace(oldValue, newValue);
        }

        private static MethodInfo FindExportMethod(Type coreType)
        {
            return coreType.GetMethod(
                "DoExport",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(bool) },
                null);
        }

        private static Type FindType(string fullName)
        {
            return Type.GetType($"{fullName}, WxEditor", false)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(fullName, false))
                    .FirstOrDefault(type => type != null);
        }

        private static object GetStaticMember(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (property != null)
                return property.GetValue(null);
            return type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }

        private static object GetMember(object target, string name)
        {
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null)
                return property.GetValue(target);
            return type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
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

        private static bool HasWritableMember(object target, string name)
        {
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return property?.CanWrite == true
                || type.GetField(name, BindingFlags.Public | BindingFlags.Instance) != null;
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null || targetType.IsInstanceOfType(value))
                return value;
            if (targetType.IsEnum)
                return Enum.ToObject(targetType, value);
            return Convert.ChangeType(value, targetType);
        }

        private static string EnsureTrailingSlash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.TrimEnd('/') + "/";
        }
    }
}
