using System;

namespace PulletFramework.YooAssetAdapter
{
    public enum EPulletYooAssetAutomaticVersionDecision
    {
        UseRemote,
        KeepCurrent,
        PreferBuiltin,
        Incomparable
    }

    /// <summary>比较由数字分段组成的 YooAsset Package 版本。</summary>
    public static class PulletYooAssetVersion
    {
        private static readonly char[] Separators = { '.', '-' };

        /// <summary>
        /// 支持 1.0.0、v1.2 和 2026-09-14-174738。result 大于零表示 left 更新。
        /// </summary>
        public static bool TryCompare(string left, string right, out int result)
        {
            result = 0;
            if (!TrySplit(left, out string[] leftParts)
                || !TrySplit(right, out string[] rightParts))
                return false;
            if (IsDateVersion(leftParts) != IsDateVersion(rightParts))
                return false;

            int count = Math.Max(leftParts.Length, rightParts.Length);
            for (int index = 0; index < count; index++)
            {
                string leftPart = index < leftParts.Length
                    ? NormalizeNumber(leftParts[index])
                    : "0";
                string rightPart = index < rightParts.Length
                    ? NormalizeNumber(rightParts[index])
                    : "0";
                if (leftPart.Length != rightPart.Length)
                {
                    result = leftPart.Length.CompareTo(rightPart.Length);
                    return true;
                }

                int comparison = string.CompareOrdinal(leftPart, rightPart);
                if (comparison == 0)
                    continue;
                result = comparison > 0 ? 1 : -1;
                return true;
            }
            return true;
        }

        public static bool IsValid(string version)
        {
            return TrySplit(version, out _);
        }

        internal static EPulletYooAssetAutomaticVersionDecision SelectAutomatic(
            string currentVersion,
            string builtinVersion,
            string remoteVersion,
            out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(remoteVersion))
            {
                reason = "服务器返回的资源版本为空。";
                return EPulletYooAssetAutomaticVersionDecision.Incomparable;
            }

            string baseline = null;
            bool baselineIsBuiltin = false;
            if (!string.IsNullOrWhiteSpace(currentVersion))
                baseline = currentVersion.Trim();
            if (!string.IsNullOrWhiteSpace(builtinVersion))
            {
                string builtin = builtinVersion.Trim();
                if (baseline == null)
                {
                    baseline = builtin;
                    baselineIsBuiltin = true;
                }
                else if (!string.Equals(baseline, builtin, StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryCompare(builtin, baseline, out int builtinComparison))
                    {
                        reason = $"当前版本 {baseline} 与内置版本 {builtin} 无法按数字分段规则比较。";
                        return EPulletYooAssetAutomaticVersionDecision.Incomparable;
                    }
                    if (builtinComparison > 0)
                    {
                        baseline = builtin;
                        baselineIsBuiltin = true;
                    }
                }
            }

            if (baseline == null)
                return IsValid(remoteVersion)
                    ? EPulletYooAssetAutomaticVersionDecision.UseRemote
                    : SetIncomparable(remoteVersion, null, out reason);
            if (string.Equals(remoteVersion.Trim(), baseline, StringComparison.OrdinalIgnoreCase))
                return baselineIsBuiltin
                    && !string.Equals(currentVersion?.Trim(), baseline,
                        StringComparison.OrdinalIgnoreCase)
                        ? EPulletYooAssetAutomaticVersionDecision.UseRemote
                        : EPulletYooAssetAutomaticVersionDecision.KeepCurrent;
            if (!TryCompare(remoteVersion, baseline, out int comparison))
                return SetIncomparable(remoteVersion, baseline, out reason);
            if (comparison > 0)
                return EPulletYooAssetAutomaticVersionDecision.UseRemote;

            reason = $"CDN 版本 {remoteVersion} 旧于当前可用版本 {baseline}。";
            return baselineIsBuiltin
                ? EPulletYooAssetAutomaticVersionDecision.PreferBuiltin
                : EPulletYooAssetAutomaticVersionDecision.KeepCurrent;
        }

        private static EPulletYooAssetAutomaticVersionDecision SetIncomparable(
            string remoteVersion, string baseline, out string reason)
        {
            reason = baseline == null
                ? $"CDN 版本 {remoteVersion} 不是支持的数字分段格式。"
                : $"CDN 版本 {remoteVersion} 与当前可用版本 {baseline} 无法按数字分段规则比较。";
            return EPulletYooAssetAutomaticVersionDecision.Incomparable;
        }

        private static bool TrySplit(string version, out string[] parts)
        {
            parts = null;
            if (string.IsNullOrWhiteSpace(version))
                return false;

            string value = version.Trim();
            if (value.Length > 1 && (value[0] == 'v' || value[0] == 'V'))
                value = value.Substring(1);
            if (value.Length == 0)
                return false;

            parts = value.Split(Separators, StringSplitOptions.None);
            foreach (string part in parts)
            {
                if (part.Length == 0)
                    return false;
                for (int index = 0; index < part.Length; index++)
                {
                    if (part[index] < '0' || part[index] > '9')
                        return false;
                }
            }
            return true;
        }

        private static string NormalizeNumber(string value)
        {
            int index = 0;
            while (index < value.Length - 1 && value[index] == '0')
                index++;
            return index == 0 ? value : value.Substring(index);
        }

        private static bool IsDateVersion(string[] parts)
        {
            return parts.Length == 4 && parts[0].Length == 4
                && parts[1].Length == 2 && parts[2].Length == 2
                && parts[3].Length == 6;
        }
    }
}
