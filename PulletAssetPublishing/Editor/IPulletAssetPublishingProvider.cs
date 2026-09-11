namespace PulletAssetPublishing.Editor
{
    /// <summary>对象存储供应商扩展点；新增供应商无需修改工作台。</summary>
    public interface IPulletAssetPublishingProvider
    {
        string Id { get; }
        string DisplayName { get; }
        void ApplyDefaults(PulletAssetPublishingProfile profile);
        bool Validate(PulletAssetPublishingProfile profile, out string message);
    }
}
