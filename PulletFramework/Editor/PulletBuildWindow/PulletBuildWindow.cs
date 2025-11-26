using PulletFramework.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using YooAsset;
using YooAsset.Editor;

namespace PulletFramework.Editor
{
    public class PulletBuildWindow : EditorWindow
    {
        [MenuItem("Pullets/BuildWindow", false, 2)]
        public static void OpenWindow()
        {
#if UNITY_ANDROID
            PulletBuildSettingData.Setting.buildTarget = EBuildTarget.Android;
#elif UNITY_IOS
       PulletBuildSettingData.Setting.buildTarget = EBuildTarget.iOS;
#endif
            PulletBuildWindow window = GetWindow<PulletBuildWindow>("打包工具", true, WindowsDefine.DockedWindowTypes);
            window.minSize = new Vector2(800, 600);
        }

        private List<Type> mEncryptionServicesClassTypes;
        private List<string> mEncryptionServicesClassNames;
        private List<string> mBuildPackageNames;

        //app
        private EnumField mBuildTargetField;
        private TextField mAppVersionField;
        private TextField mAppVersionCodeField;
        private TextField mApkNameField;

        //build HybridCLR
        private VisualElement mHybridCLRContainer;
        private Button mHybridCLRSettingButton;
        private VisualElement mHybridCLRAOTContainer;
        private Toggle mBuildAOTToogle;
        private Toggle mCopyAOTToogle;
        private VisualElement mHybridCLRDllContainer;
        private Toggle mBuildDLLToogle;
        private Toggle mCopyDLLToogle;

        //build ab
        private Button mAssetBundleSettingButton;
        private VisualElement mAssetBundleInfoContainer;
        private EnumField mBuildPipelineField;
        private PopupField<string> mBuildPackageField;
        private PopupField<Enum> mBuildModeField;
        private PopupField<string> mEncryptionField;
        private EnumField mCopyBuildinFileOption;
        private Toggle mShaderVariantCollectorToogle;

        private EnumField mCopyAssetBundleOperation;
        private Button mUploadBtn;
        private Toggle mCopyAssetBundleToggle;


        //build
        private Button mBuildAssetBundleButton;
        private Toggle mBuildAssetBundleToogle;
        private Button mBuildBtn;

        //是否打包
        private bool mIsPack = false;

        public void CreateGUI()
        {
            try
            {
                VisualElement root = this.rootVisualElement;

                // 加载布局文件
                var visualAsset = UxmlLoader.LoadWindowUXML<PulletBuildWindow>();
                if (visualAsset == null)
                    return;

                visualAsset.CloneTree(root);

                // app 信息
                InitAppInfo(root);

                //构建 ab
                InitBuildAssetBundle(root);

                //HybridCLR 
                InitHybridCLR(root);

                //初始化 Build
                InitBuild(root);

                // 刷新窗体
                RefreshWindow();
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }

        #region 初始化

        /// <summary>
        /// app 信息
        /// </summary>
        /// <param name="root"></param>
        private void InitAppInfo(VisualElement root)
        {
            // 构建平台
            mBuildTargetField = root.Q<EnumField>("buildTarget");
            mBuildTargetField.Init(PulletBuildSettingData.Setting.buildTarget);
            mBuildTargetField.SetValueWithoutNotify(PulletBuildSettingData.Setting.buildTarget);
            mBuildTargetField.style.width = 350;
            mBuildTargetField.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.IsDirty = true;
                PulletBuildSettingData.Setting.buildTarget = (EBuildTarget)mBuildTargetField.value;
                RefreshWindow();
            });

            // app 版本
            mAppVersionField = root.Q<TextField>("appVersion");
            mAppVersionField.SetValueWithoutNotify(PulletBuildSettingData.Setting.appVersion);
            mAppVersionField.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.IsDirty = true;
                PulletBuildSettingData.Setting.appVersion = mAppVersionField.value;
                RefreshWindow();
            });

            // app 版本号
            mAppVersionCodeField = root.Q<TextField>("appVersionCode");
            if (PulletBuildSettingData.Setting.buildTarget == EBuildTarget.Android)
            {
                mAppVersionCodeField.SetValueWithoutNotify(PulletBuildSettingData.Setting.appVersionCode.ToString());
                mAppVersionCodeField.RegisterValueChangedCallback(evt =>
                {
                    PulletBuildSettingData.IsDirty = true;
                    PulletBuildSettingData.Setting.appVersionCode = int.Parse(mAppVersionCodeField.value);
                    RefreshWindow();
                });
            }

            // apk 名称
            mApkNameField = root.Q<TextField>("apkName");
            mApkNameField.SetValueWithoutNotify(PulletBuildSettingData.Setting.apkName);
            mApkNameField.SetEnabled(false);
        }

        /// <summary>
        /// 初始化构建 ab 
        /// </summary>
        /// <param name="root"></param>
        private void InitBuildAssetBundle(VisualElement root)
        {
            mAssetBundleSettingButton = root.Q<Button>("assetBundleSetting");
            mAssetBundleSettingButton.clicked += OnAssetBundleSettingBtnCallBack;
            mAssetBundleInfoContainer = root.Q<VisualElement>("assetBundleInfoContainer");
            // 构建管线
            mBuildPipelineField = root.Q<EnumField>("buildPipeline");
            mBuildPipelineField.Init(PulletBuildSettingData.Setting.buildPipeline);
            mBuildPipelineField.SetValueWithoutNotify(PulletBuildSettingData.Setting.buildPipeline);
            mBuildPipelineField.style.width = 350;
            mBuildPipelineField.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.IsDirty = true;
                PulletBuildSettingData.Setting.buildPipeline = (EBuildPipeline)mBuildPipelineField.value;
                //if (PulletBuildSettingData.Setting.buildPipeline == EBuildPipeline.BuiltinBuildPipeline)
                //    PulletBuildSettingData.Setting.buildMode = EBuildMode.ForceRebuild;
                //else if (PulletBuildSettingData.Setting.buildPipeline == EBuildPipeline.ScriptableBuildPipeline)
                //    PulletBuildSettingData.Setting.buildMode = EBuildMode.IncrementalBuild;

                RefreshWindow();

                BuildModeContainer(root);
            });


            // 包裹名称列表
            mBuildPackageNames = GetBuildPackageNames();

            // 构建包裹
            var buildPackageContainer = root.Q("buildPackageContainer");
            buildPackageContainer.Clear();
            if (mBuildPackageNames.Count > 0)
            {
                int defaultIndex = GetDefaultPackageIndex(PulletBuildSettingData.Setting.buildPackage);
                mBuildPackageField = new PopupField<string>(mBuildPackageNames, defaultIndex);
                mBuildPackageField.label = "Build Package";
                mBuildPackageField.style.width = 350;
                mBuildPackageField.RegisterValueChangedCallback(evt =>
                {
                    PulletBuildSettingData.IsDirty = true;
                    PulletBuildSettingData.Setting.buildPackage = mBuildPackageField.value;
                });
                buildPackageContainer.Add(mBuildPackageField);
            }
            else
            {
                mBuildPackageField = new PopupField<string>();
                mBuildPackageField.label = "Build Package";
                mBuildPackageField.style.width = 350;
                buildPackageContainer.Add(mBuildPackageField);
            }

            //构建模式
            BuildModeContainer(root);

            // 加密服务类
            mEncryptionServicesClassTypes = GetEncryptionServicesClassTypes();
            mEncryptionServicesClassNames = mEncryptionServicesClassTypes.Select(t => t.Name).ToList();
            // 加密方法
            var encryptionContainer = root.Q("encryptionContainer");
            if (mEncryptionServicesClassNames.Count > 0)
            {
                int defaultIndex = GetDefaultEncryptionIndex(PulletBuildSettingData.Setting.encyptionClassName);
                mEncryptionField = new PopupField<string>(mEncryptionServicesClassNames, defaultIndex);
                mEncryptionField.label = "Encryption";
                mEncryptionField.style.width = 350;
                mEncryptionField.RegisterValueChangedCallback(evt =>
                {
                    PulletBuildSettingData.IsDirty = true;
                    PulletBuildSettingData.Setting.encyptionClassName = mEncryptionField.value;
                });
                encryptionContainer.Add(mEncryptionField);
            }
            else
            {
                mEncryptionField = new PopupField<string>();
                mEncryptionField.label = "Encryption";
                mEncryptionField.style.width = 350;
                encryptionContainer.Add(mEncryptionField);
            }

            //拷贝内置资源
            mCopyBuildinFileOption = root.Q<EnumField>("copyBuildinFileOption");
            mCopyBuildinFileOption.Init(PulletBuildSettingData.Setting.copyBuildinFileOption);
            mCopyBuildinFileOption.SetValueWithoutNotify(PulletBuildSettingData.Setting.copyBuildinFileOption);
            mCopyBuildinFileOption.style.width = 350;
            mCopyBuildinFileOption.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.IsDirty = true;
                PulletBuildSettingData.Setting.copyBuildinFileOption = (EBuildinFileCopyOption)mCopyBuildinFileOption.value;
                RefreshWindow();
            });

            //构建ab
            mShaderVariantCollectorToogle = root.Q<Toggle>("shaderVariantCollector");
            mShaderVariantCollectorToogle.SetValueWithoutNotify(PulletBuildSettingData.Setting.shaderVariantCollector);
            mShaderVariantCollectorToogle.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.Setting.shaderVariantCollector = mShaderVariantCollectorToogle.value;
                RefreshWindow();
            });

            //拷贝 ab 操作
            mCopyAssetBundleOperation = root.Q<EnumField>("copyAssetBundleOperation");
            mCopyAssetBundleOperation.Init(PulletBuildSettingData.Setting.copyAssetBundleOperation);
            mCopyAssetBundleOperation.SetValueWithoutNotify(PulletBuildSettingData.Setting.copyAssetBundleOperation);
            mCopyAssetBundleOperation.style.width = 350;
            mCopyAssetBundleOperation.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.IsDirty = true;
                PulletBuildSettingData.Setting.copyAssetBundleOperation = (ECopyAssetBundleOperation)mCopyAssetBundleOperation.value;
                RefreshWindow();
            });

            //上传 
            mUploadBtn = root.Q<Button>("uploadBtn");
            mUploadBtn.clicked += OnUploadBtnCallBack;
        }

        private void BuildModeContainer(VisualElement root) 
        {
            var buildModeContainer = root.Q("buildModeContainer");
            buildModeContainer.Clear();
            var buildModeList = GetSupportBuildModes();

            int defaultModeIndex = buildModeList.FindIndex(x => x.Equals(PulletBuildSettingData.Setting.buildMode));
            if (defaultModeIndex < 0)
                defaultModeIndex = (int)(EBuildMode)buildModeList[0];

            mBuildModeField = new PopupField<Enum>(buildModeList, defaultModeIndex);
            mBuildModeField.label = "Build Mode";
            mBuildModeField.style.width = 350;
            mBuildModeField.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.IsDirty = true;
                PulletBuildSettingData.Setting.buildMode = (EBuildMode)mBuildModeField.value;
                //AssetBundleBuilderSetting.SetPackageBuildMode(PackageName, BuildPipeline, (EBuildMode)_buildModeField.value);
            });
            buildModeContainer.Add(mBuildModeField);
        }

        private List<Enum> GetSupportBuildModes()
        {
            List<Enum> buildModeList = new List<Enum>();
            if (PulletBuildSettingData.Setting.buildPipeline == EBuildPipeline.BuiltinBuildPipeline)
            {
                buildModeList.Add(EBuildMode.ForceRebuild);
                buildModeList.Add(EBuildMode.IncrementalBuild);
                buildModeList.Add(EBuildMode.DryRunBuild);
                buildModeList.Add(EBuildMode.SimulateBuild);
            }
            else if (PulletBuildSettingData.Setting.buildPipeline == EBuildPipeline.ScriptableBuildPipeline)
            {
                buildModeList.Add(EBuildMode.IncrementalBuild);
                buildModeList.Add(EBuildMode.SimulateBuild);
            }
            return buildModeList;
        }
        /// <summary>
        /// 热梗相关
        /// </summary>
        /// <param name="root"></param>
        private void InitHybridCLR(VisualElement root)
        {
            mHybridCLRContainer = root.Q<VisualElement>("hybridCLRContainer");
#if ENABLE_HYBRIDCLR_EDITOR
            mHybridCLRContainer.style.display = DisplayStyle.Flex;
            //设置
            mHybridCLRSettingButton = root.Q<Button>("hybridCLRSetting");
            mHybridCLRSettingButton.clicked += OnHybridCLRSSettingBtnCallBack;
            mHybridCLRAOTContainer = root.Q<VisualElement>("hybridCLRAOT");
            mHybridCLRDllContainer = root.Q<VisualElement>("hybridCLRDll");

            //
            mBuildAOTToogle = root.Q<Toggle>("buildAOTDLL");
            mBuildAOTToogle.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.Setting.generateAOTDlls = mBuildAOTToogle.value;
                RefreshWindow();
            });
            mCopyAOTToogle = root.Q<Toggle>("copyAOTDLL");
            mCopyAOTToogle.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.Setting.copyAOTDlls = mCopyAOTToogle.value;
                RefreshWindow();
            });
            mBuildDLLToogle = root.Q<Toggle>("compileDlls");
            mBuildDLLToogle.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.Setting.compileHotUpdateDlls = mBuildDLLToogle.value;
                RefreshWindow();
            });
            mCopyDLLToogle = root.Q<Toggle>("copyDLL");
            mCopyDLLToogle.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.Setting.copyHotUpdateDlls = mCopyDLLToogle.value;
                RefreshWindow();
            });
#else
            mHybridCLRContainer.style.display = DisplayStyle.None;
#endif
        }

        /// <summary>
        /// 打包相关
        /// </summary>
        /// <param name="root"></param>
        private void InitBuild(VisualElement root)
        {
            mBuildAssetBundleButton = root.Q<Button>("buildAssetBundle");
            mBuildAssetBundleButton.clicked += OnBuildAssetBundleBtnCallBack;
            //是否打包ab
            mBuildAssetBundleToogle = root.Q<Toggle>("isBuildAssetBundle");
            mBuildAssetBundleToogle.SetValueWithoutNotify(PulletBuildSettingData.Setting.buildAssetBundles);
            mBuildAssetBundleToogle.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.Setting.buildAssetBundles = mBuildAssetBundleToogle.value;
                RefreshWindow();
            });
            //打包
            mBuildBtn = root.Q<Button>("build");
            mBuildBtn.clicked += OnBuildBtnCallBack;
        }

        #endregion

        public void OnDestroy()
        {
            if (PulletBuildSettingData.IsDirty)
                PulletBuildSettingData.SaveFile();
        }

        private void Update()
        {

        }

        private void RefreshWindow()
        {
            mBuildBtn.SetEnabled(true);
            // build info
            if (PulletBuildSettingData.Setting.buildTarget == EBuildTarget.Android)
            {
                mAppVersionCodeField.style.display = DisplayStyle.Flex;
                mBuildBtn.text = "打包Apk";
            }
            else if (PulletBuildSettingData.Setting.buildTarget == EBuildTarget.iOS)
            {
                mAppVersionCodeField.style.display = DisplayStyle.None;
                mBuildBtn.text = "导出XCode";
            }
            //else if (PulletBuildSettingData.Setting.buildTarget == EBuildTarget.StandaloneWindows64)
            //{
            //    mAppVersionCodeField.style.display = DisplayStyle.None;
            //    mBuildBtn.text = "导出 PC";
            //}
            else
            {
                mAppVersionCodeField.style.display = DisplayStyle.None;
                mBuildBtn.text = "暂不支持";
                mBuildBtn.SetEnabled(false);
            }

            PulletBuildSettingData.Setting.apkName = PlayerSettings.productName + "-" + PulletBuildSettingData.Setting.appVersion + "-" + GetBuildApkVersion();
            mApkNameField.SetValueWithoutNotify(PulletBuildSettingData.Setting.apkName);

            //HybridCLR
#if ENABLE_HYBRIDCLR_EDITOR
            UpdateHybridCLR();
#endif
            //Update build AssetBundle info 
            UpdateAssetBundleInfo();
            //build 
            mShaderVariantCollectorToogle.SetValueWithoutNotify(PulletBuildSettingData.Setting.shaderVariantCollector);
            mBuildAssetBundleToogle.SetValueWithoutNotify(PulletBuildSettingData.Setting.buildAssetBundles);
        }


        private void UpdateHybridCLR()
        {
            if (PulletBuildSettingData.Setting.showHybridCLRSettings)
            {
                mHybridCLRAOTContainer.style.display = DisplayStyle.Flex;
                mHybridCLRDllContainer.style.display = DisplayStyle.Flex;

                mBuildAOTToogle.SetValueWithoutNotify(PulletBuildSettingData.Setting.generateAOTDlls);
                mCopyAOTToogle.SetValueWithoutNotify(PulletBuildSettingData.Setting.copyAOTDlls);
                mBuildDLLToogle.SetValueWithoutNotify(PulletBuildSettingData.Setting.compileHotUpdateDlls);
                mCopyDLLToogle.SetValueWithoutNotify(PulletBuildSettingData.Setting.copyHotUpdateDlls);
            }
            else
            {
                mHybridCLRAOTContainer.style.display = DisplayStyle.None;
                mHybridCLRDllContainer.style.display = DisplayStyle.None;
            }
        }

        private void UpdateAssetBundleInfo()
        {
            if (PulletBuildSettingData.Setting.showAssetBundleInfoSettings)
            {
                mAssetBundleInfoContainer.style.display = DisplayStyle.Flex;
            }
            else
            {
                mAssetBundleInfoContainer.style.display = DisplayStyle.None;
            }
        }

        #region 按钮事件
        private void OnHybridCLRSSettingBtnCallBack()
        {
            PulletBuildSettingData.Setting.showHybridCLRSettings = !PulletBuildSettingData.Setting.showHybridCLRSettings;
            RefreshWindow();
        }

        private void OnAssetBundleSettingBtnCallBack()
        {
            PulletBuildSettingData.Setting.showAssetBundleInfoSettings = !PulletBuildSettingData.Setting.showAssetBundleInfoSettings;
            RefreshWindow();
        }

        private void OnUploadBtnCallBack()
        {
            if (EditorUtility.DisplayDialog("提示", $"是否上传AssetBundle到 TencentCOS！", "Yes", "No"))
            {
                UploadAssetBundlesToTencentCOS();
            }
            else
            {
                Debug.LogWarning("[Upload] 已经取消上传");
            }
        }

        /// <summary>
        /// 构建AB
        /// </summary>
        private void OnBuildAssetBundleBtnCallBack()
        {
            if (EditorUtility.DisplayDialog("提示", $"是否开始构建AssetBundle！", "Yes", "No"))
            {
                mIsPack = false;
                //开始构建AB
                BuildAssetBundle();
            }
            else
            {
                Debug.LogWarning("[Build] 打包已经取消");
            }
        }

        /// <summary>
        /// 打包APK 或者 XCode
        /// </summary>
        private void OnBuildBtnCallBack()
        {
            string tips = "";
            if (PulletBuildSettingData.Setting.buildTarget == EBuildTarget.Android)
            {
                tips = "是否开始打包Apk!";
            }
            else if (PulletBuildSettingData.Setting.buildTarget == EBuildTarget.iOS)
            {
                tips = "是否开始导出XCode!";
            }
            else
            {
                tips = "是否开始打包Apk!";
            }


            if (EditorUtility.DisplayDialog("提示", tips, "Yes", "No"))
            {
                mIsPack = true;
                //开始构建AB
                if (PulletBuildSettingData.Setting.buildAssetBundles)
                {
                    BuildAssetBundle();
                }
                else
                {
                    Build();
                }
            }
            else
            {
                Debug.LogWarning("[Build] 打包已经取消");
            }
        }
        #endregion

        /// <summary>
        /// 打包AB
        /// </summary>
        private void BuildAssetBundle()
        {
            //编译 AOT or HotDlls
#if ENABLE_HYBRIDCLR_EDITOR
            CompileHybridCLR();
#endif
            //搜集shader 变体
            if (PulletBuildSettingData.Setting.shaderVariantCollector)
            {
                CollectorShaderVariant();
                return;
            }
            //开始打包AB
            AssetDatabase.Refresh();
            EditorApplication.delayCall += ExecuteBuildAssetBundle;
        }

#if ENABLE_HYBRIDCLR_EDITOR
        private void CompileHybridCLR()
        {
            BuildTarget buildTarget = PulletBuildSettingData.Setting.GetBuildTarget();
            if (PulletBuildSettingData.Setting.generateAOTDlls)
                HybridCLRCommand.GenerateAOTDlls(buildTarget);
            if (PulletBuildSettingData.Setting.copyAOTDlls)
                HybridCLRCommand.CopyAOTAssembliesToAssetPath();

            if (PulletBuildSettingData.Setting.compileHotUpdateDlls)
                HybridCLRCommand.CompileDll(buildTarget);
            if (PulletBuildSettingData.Setting.copyAOTDlls)
                HybridCLRCommand.CopyHotUpdateAssembliesToAssetPath();
        }
#endif

        /// <summary>
        /// 收集shader 
        /// </summary>
        private void CollectorShaderVariant()
        {
            //保存当前场景
            Scene tempScene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(tempScene);
            AssetDatabase.Refresh();
            string tempScenePath = tempScene.path;
            //先收集变体
            string savePath = ShaderVariantCollectorSetting.GeFileSavePath(PulletBuildSettingData.Setting.buildPackage);
            ShaderVariantCollector.Run(savePath, PulletBuildSettingData.Setting.buildPackage, int.MaxValue, delegate ()
            {
                AssetDatabase.Refresh();
                //打开game 场景
                EditorSceneManager.OpenScene(tempScenePath);
                //开始打包AB
                AssetDatabase.Refresh();
                EditorApplication.delayCall += ExecuteBuildAssetBundle;
            });
        }


        /// <summary>
        /// 执行构建AssetBundle
        /// </summary>
        private void ExecuteBuildAssetBundle()
        {
            //默认管线
            if (PulletBuildSettingData.Setting.buildPipeline == EBuildPipeline.BuiltinBuildPipeline)
            {
                OnBuiltinBuildPipeline_Build();
            }
            else if (PulletBuildSettingData.Setting.buildPipeline == EBuildPipeline.ScriptableBuildPipeline)
            {
                ScriptableBuildPipeline_Build();
            }
            //BuildParameters buildParameters = new BuildParameters();
            //buildParameters.StreamingAssetsRoot = AssetBundleBuilderHelper.GetDefaultStreamingAssetsRoot();
            //buildParameters.BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot();
            //buildParameters.BuildTarget = PulletBuildSettingData.Setting.GetBuildTarget();
            //buildParameters.BuildPipeline = PulletBuildSettingData.Setting.buildPipeline;
            //buildParameters.BuildMode = PulletBuildSettingData.Setting.buildMode;
            //buildParameters.PackageName = PulletBuildSettingData.Setting.buildPackage;
            //buildParameters.PackageVersion = GetBuildPackageVersion();
            //buildParameters.VerifyBuildingResult = true;
            //buildParameters.SharedPackRule = new ZeroRedundancySharedPackRule();
            //buildParameters.EncryptionServices = CreateEncryptionServicesInstance();
            //buildParameters.CompressOption = ECompressOption.LZ4;
            //buildParameters.OutputNameStyle = EOutputNameStyle.HashName;
            //buildParameters.CopyBuildinFileOption = PulletBuildSettingData.Setting.copyBuildinFileOption;
            //buildParameters.CopyBuildinFileTags = string.Empty;

            //if (PulletBuildSettingData.Setting.buildPipeline == EBuildPipeline.ScriptableBuildPipeline)
            //{
            //    buildParameters.SBPParameters = new BuildParameters.SBPBuildParameters();
            //    buildParameters.SBPParameters.WriteLinkXML = true;
            //}

            //var builder = new AssetBundleBuilder();
            //var buildResult = builder.Run(buildParameters);
            //if (buildResult.Success)
            //{
            //    if (mIsPack)
            //    {
            //        Build();
            //    }
            //    else
            //    {
            //        EditorUtility.RevealInFinder(buildResult.OutputPackageDirectory);
            //    }
            //    AssetDatabase.Refresh();
            //    // 拷贝ab 到指定目录操作
            //    CopyAssetBundleToAssignPathOperation(buildResult.OutputPackageDirectory);
            //}
        }

        private void OnBuiltinBuildPipeline_Build()
        {
            BuiltinBuildParameters buildParameters = new BuiltinBuildParameters();
            buildParameters.BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot();
            buildParameters.BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot();
            buildParameters.BuildPipeline = PulletBuildSettingData.Setting.buildPipeline.ToString();
            buildParameters.BuildTarget = PulletBuildSettingData.Setting.GetBuildTarget();
            buildParameters.BuildMode = PulletBuildSettingData.Setting.buildMode;
            buildParameters.PackageName = PulletBuildSettingData.Setting.buildPackage;
            buildParameters.PackageVersion = GetBuildPackageVersion();
            buildParameters.EnableSharePackRule = true;
            buildParameters.VerifyBuildingResult = true;

            buildParameters.FileNameStyle = PulletBuildSettingData.Setting.outputNameStyle;
            buildParameters.BuildinFileCopyOption = PulletBuildSettingData.Setting.copyBuildinFileOption;
            buildParameters.BuildinFileCopyParams = "";
            buildParameters.EncryptionServices = CreateEncryptionServicesInstance();
            buildParameters.CompressOption = ECompressOption.LZ4;
            BuiltinBuildPipeline pipeline = new BuiltinBuildPipeline();
            var buildResult = pipeline.Run(buildParameters, true);
            if (buildResult.Success)
            {
                if (mIsPack)
                {
                    Build();
                }
                else
                {
                    EditorUtility.RevealInFinder(buildResult.OutputPackageDirectory);
                }
                AssetDatabase.Refresh();
                // 拷贝ab 到指定目录操作
                CopyAssetBundleToAssignPathOperation(buildResult.OutputPackageDirectory);
            }
            //buildParameters.SharedPackRule = new ZeroRedundancySharedPackRule();
            //buildParameters.EncryptionServices = CreateEncryptionServicesInstance();
            //buildParameters.OutputNameStyle = EOutputNameStyle.HashName;
            //buildParameters.CopyBuildinFileOption = PulletBuildSettingData.Setting.copyBuildinFileOption;
            //buildParameters.CopyBuildinFileTags = string.Empty;
        }

        private void ScriptableBuildPipeline_Build()
        {
            ScriptableBuildParameters buildParameters = new ScriptableBuildParameters();
            buildParameters.BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot();
            buildParameters.BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot();
            buildParameters.BuildPipeline = PulletBuildSettingData.Setting.buildPipeline.ToString();
            buildParameters.BuildTarget = PulletBuildSettingData.Setting.GetBuildTarget();
            buildParameters.BuildMode = PulletBuildSettingData.Setting.buildMode;
            buildParameters.PackageName = PulletBuildSettingData.Setting.buildPackage;
            buildParameters.PackageVersion = GetBuildPackageVersion();
            buildParameters.EnableSharePackRule = true;
            buildParameters.VerifyBuildingResult = true;

            buildParameters.FileNameStyle = PulletBuildSettingData.Setting.outputNameStyle;
            buildParameters.BuildinFileCopyOption = PulletBuildSettingData.Setting.copyBuildinFileOption;
            buildParameters.BuildinFileCopyParams = "";
            buildParameters.EncryptionServices = CreateEncryptionServicesInstance();
            buildParameters.CompressOption = ECompressOption.LZ4;

            ScriptableBuildPipeline pipeline = new ScriptableBuildPipeline();
            var buildResult = pipeline.Run(buildParameters, true);
            if (buildResult.Success)
            {
                if (mIsPack)
                {
                    Build();
                }
                else
                {
                    EditorUtility.RevealInFinder(buildResult.OutputPackageDirectory);
                }
                AssetDatabase.Refresh();
                // 拷贝ab 到指定目录操作
                CopyAssetBundleToAssignPathOperation(buildResult.OutputPackageDirectory);
            }
        }
        /// <summary>
        /// 打包Apk或者XCode
        /// </summary>
        private void Build()
        {
            AssetDatabase.Refresh();

            PlayerSettings.bundleVersion = PulletBuildSettingData.Setting.appVersion;
#if UNITY_ANDROID
            PlayerSettings.Android.bundleVersionCode = PulletBuildSettingData.Setting.appVersionCode;
            SetKetstore();
#elif UNITY_IOS

#endif
            //保存路径
            string save = GetSavePath(PulletBuildSettingData.Setting.GetBuildTarget());

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = GetBuildScenes(),
                locationPathName = save + PulletBuildSettingData.Setting.apkName + ".apk",
#if UNITY_ANDROID
                targetGroup = BuildTargetGroup.Android,
#elif UNITY_IOS
                    targetGroup = BuildTargetGroup.iOS,
#endif
                target = PulletBuildSettingData.Setting.GetBuildTarget(),
#if UNITY_ANDROID
                options = BuildOptions.CompressWithLz4
#elif UNITY_IOS
            options = BuildOptions.None
#endif
            };

            BuildPipeline.BuildPlayer(buildPlayerOptions);
#if UNITY_ANDROID
            Debug.Log("Android平台安装包生成完毕");
            EditorUtility.RevealInFinder(save);
#elif UNITY_IOS
            Debug.Log("iOS平台Xcode工程生成完毕");
            EditorUtility.RevealInFinder(mSavePath);
#endif
        }

        /// <summary>
        /// 拷贝ab 到指定目录操作
        /// </summary>
        private void CopyAssetBundleToAssignPathOperation(string OutputPackageDirectory)
        {
            //拷贝AB 到指定目录
            if (PulletBuildSettingData.Setting.copyAssetBundleOperation != ECopyAssetBundleOperation.None)
            {
                string sourcePath = OutputPackageDirectory;
                string destPath = PulletBuildSettingData.Setting.GetOutputPath();

                //清空
                EditorTools.ClearFolder(destPath);
                //拷贝
                EditorTools.CopyDirectory(sourcePath, destPath);

                if (PulletBuildSettingData.Setting.copyAssetBundleOperation == ECopyAssetBundleOperation.Copy)
                    EditorUtility.RevealInFinder(destPath);
            }
            AssetDatabase.Refresh();
            //打包zip
            if (PulletBuildSettingData.Setting.copyAssetBundleOperation == ECopyAssetBundleOperation.CopyAndPack_Zip
                || PulletBuildSettingData.Setting.copyAssetBundleOperation == ECopyAssetBundleOperation.CopyAndUpload)
            {
                ZipHelper.ZipDirectory(PulletBuildSettingData.Setting.GetVersionPath(), PulletBuildSettingData.Setting.GetPackagPath(), PulletBuildSettingData.Setting.appVersion);


                EditorUtility.RevealInFinder(PulletBuildSettingData.Setting.GetVersionPath());

                //上传 cos
                if (PulletBuildSettingData.Setting.copyAssetBundleOperation == ECopyAssetBundleOperation.CopyAndUpload)
                {
                    UploadAssetBundlesToTencentCOS();
                }
            }

        }

        private void UploadAssetBundlesToTencentCOS()
        {
            AssetDatabase.Refresh();
            string cosKey = $"{PulletBuildSettingData.Setting.buildPackage}/{PulletBuildSettingData.Setting.appVersion}.zip";
            string srcPath = $"{PulletBuildSettingData.Setting.GetVersionPath()}.zip";
            TencentCOS.PutObject(cosKey, srcPath);
        }

        private string GetBuildApkVersion()
        {
            int totalMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            return DateTime.Now.ToString("yyyy-MM-dd");
        }


        // 构建版本相关
        private string GetBuildPackageVersion()
        {
            int totalMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            return DateTime.Now.ToString("yyyy-MM-dd") + "-" + totalMinutes;
        }

        // 构建包裹相关
        private int GetDefaultPackageIndex(string packageName)
        {
            for (int index = 0; index < mBuildPackageNames.Count; index++)
            {
                if (mBuildPackageNames[index] == packageName)
                {
                    return index;
                }
            }

            PulletBuildSettingData.IsDirty = true;
            PulletBuildSettingData.Setting.buildPackage = mBuildPackageNames[0];
            return 0;
        }
        private List<string> GetBuildPackageNames()
        {
            List<string> result = new List<string>();
            foreach (var package in AssetBundleCollectorSettingData.Setting.Packages)
            {
                result.Add(package.PackageName);
            }
            return result;
        }

        // 加密类相关
        private int GetDefaultEncryptionIndex(string className)
        {
            for (int index = 0; index < mEncryptionServicesClassNames.Count; index++)
            {
                if (mEncryptionServicesClassNames[index] == className)
                {
                    return index;
                }
            }

            PulletBuildSettingData.IsDirty = true;
            PulletBuildSettingData.Setting.encyptionClassName = mEncryptionServicesClassNames[0];
            return 0;
        }
        private List<Type> GetEncryptionServicesClassTypes()
        {
            return EditorTools.GetAssignableTypes(typeof(IEncryptionServices));
        }

        private IEncryptionServices CreateEncryptionServicesInstance()
        {
            if (mEncryptionField.index < 0)
                return null;
            var classType = mEncryptionServicesClassTypes[mEncryptionField.index];
            return (IEncryptionServices)Activator.CreateInstance(classType);
        }

        private string GetSavePath(BuildTarget buildTarget)
        {
            if (buildTarget == BuildTarget.Android)
                return Directory.GetParent(Application.dataPath).ToString() + "/AndroidApk/";
            else
                return Directory.GetParent(Application.dataPath).ToString() + "/XCodeProject/";
        }

        private string[] GetBuildScenes()
        {
            List<string> names = new List<string>();
            foreach (EditorBuildSettingsScene e in EditorBuildSettings.scenes)
            {
                if (e == null)
                    continue;
                if (e.enabled)
                    names.Add(e.path);
            }
            Debug.Log("打包得场景列表：" + names);
            return names.ToArray();
        }

        private static void SetKetstore()
        {
            if (PulletEditorSettingData.Setting.keystoreName == "") return;
            PlayerSettings.Android.keystoreName = PulletEditorSettingData.Setting.keystoreName;
            PlayerSettings.Android.keystorePass = PulletEditorSettingData.Setting.keystorePass;
            PlayerSettings.Android.keyaliasName = PulletEditorSettingData.Setting.keyaliasName;
            PlayerSettings.Android.keyaliasPass = PulletEditorSettingData.Setting.keyaliasPass;
        }
    }
}
