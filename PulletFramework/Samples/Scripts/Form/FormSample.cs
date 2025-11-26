using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;
using PulletFramework;
using PulletFramework.Form;

public class FormSample : MonoBehaviour
{
    void Start()
    {
        // 初始化资源系统
        YooAssets.Initialize();
        YooAssets.SetOperationSystemMaxTimeSlice(30);
        PulletFrameworks.StartCoroutine(InitPackage());
    }

    private IEnumerator InitPackage()
    {
        yield return new WaitForSeconds(1f);

        var playMode = EPlayMode.EditorSimulateMode;

        // 创建默认的资源包
        string packageName = "DefaultPackage";
        var package = YooAssets.TryGetPackage(packageName);
        if (package == null)
        {
            package = YooAssets.CreatePackage(packageName);
            YooAssets.SetDefaultPackage(package);
        }

        // 编辑器下的模拟模式
        InitializationOperation initializationOperation = null;
        if (playMode == EPlayMode.EditorSimulateMode)
        {
            var createParameters = new EditorSimulateModeParameters();
           // createParameters.SimulateManifestFilePath = EditorSimulateModeHelper.SimulateBuild(packageName);
           // initializationOperation = package.InitializeAsync(createParameters);
        }

        yield return initializationOperation;
        if (package.InitializeStatus == EOperationStatus.Succeed)
        {
            //PulletForm.AddForm<TestForm>();
            //yield return PulletFrameworks.StartCoroutine(PulletForm.IsReadFinish());
            //SubtitleData subtitleData = TestForm.Instance.GetDateById(10);
           // Debug.LogError(subtitleData.subtitle);

            //单例测试
            SingletonTest.Instance.Run();
            SingletonTest.Instance.Run();
        }
        else
        {
            Debug.LogWarning($"{initializationOperation.Error}");
        }
    }
}
