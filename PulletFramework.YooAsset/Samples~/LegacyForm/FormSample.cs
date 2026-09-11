using System.Collections;
using UnityEngine;
using YooAsset;
using PulletFramework;
using PulletFramework.YooAssetAdapter;

public class FormSample : MonoBehaviour
{
    void Start()
    {
        // 初始化资源系统
        YooAssets.Initialize();
        PulletFrameworks.StartCoroutine(InitPackage());
    }

    private IEnumerator InitPackage()
    {
        yield return new WaitForSeconds(1f);

        // 创建默认的资源包
        string packageName = "DefaultPackage";
        if (!YooAssets.TryGetPackage(packageName, out ResourcePackage package))
            package = YooAssets.CreatePackage(packageName);

        var options = new OfflinePlayModeOptions
        {
            BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
        };
        InitializePackageOperation initializationOperation = package.InitializePackageAsync(options);

        yield return initializationOperation;
        if (package.InitializeStatus == EOperationStatus.Succeeded)
        {
            YooAssetResourceAdapter.Install(packageName);
            //单例测试
            SingletonTest.Instance.Run();
            SingletonTest.Instance.Run();
        }
        else
        {
            PLogger.Warning($"{initializationOperation.Error}");
        }
    }
}
