#if UNITY_WEBGL && (PULLET_PLATFORM_DOUYIN || DOUYINMINIGAME)
using YooAsset;
using PulletFramework.YooAssetAdapter;

public static class TiktokFileSystemCreater
{
    public static FileSystemParameters CreateFileSystemParameters(IRemoteService remoteService)
    {
        var fileSystemParams = CreateBaseFileSystemParameters(remoteService);
        return fileSystemParams;
    }
    public static FileSystemParameters CreateFileSystemParameters(IRemoteService remoteService, IBundleDecryptor assetBundleDecryptor)
    {
        var fileSystemParams = CreateBaseFileSystemParameters(remoteService);
        fileSystemParams.AddParameter(EFileSystemParameter.AssetBundleDecryptor, assetBundleDecryptor);
        return fileSystemParams;
    }
    public static FileSystemParameters CreateFileSystemParameters(IRemoteService remoteService, IBundleDecryptor assetBundleDecryptor, IBundleDecryptor rawBundleDecryptor)
    {
        var fileSystemParams = CreateBaseFileSystemParameters(remoteService);
        fileSystemParams.AddParameter(EFileSystemParameter.AssetBundleDecryptor, assetBundleDecryptor);
        fileSystemParams.AddParameter(EFileSystemParameter.RawBundleDecryptor, rawBundleDecryptor);
        return fileSystemParams;
    }

    private static FileSystemParameters CreateBaseFileSystemParameters(IRemoteService remoteService)
    {
        return PulletWebNetworkFileSystem.Create(remoteService, new TiktokPlatform());
    }
}
#endif
