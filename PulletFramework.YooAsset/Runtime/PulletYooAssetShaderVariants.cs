namespace PulletFramework.YooAssetAdapter
{
    public static class PulletYooAssetShaderVariants
    {
        public static string GetLocation(PulletYooAssetSettings settings, string packageName)
        {
            return settings.ResolveShaderVariantName(packageName);
        }
    }
}
