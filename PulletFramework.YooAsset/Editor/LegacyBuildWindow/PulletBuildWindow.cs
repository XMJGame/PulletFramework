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
        private const string HybridCommandTypeName =
            "PulletFramework.Editor.HybridCLRCommand, PulletFramework.HybridCLR.Editor";

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
        private EnumField mBundledCopyOption;
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
                var visualAsset = PulletEditorAssetUtility.LoadWindowUxml<PulletBuildWindow>();
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
                PulletBuildSettingData.Setting.buildPipeline = (EPulletBuildPipeline)mBuildPipelineField.value;
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
            mBundledCopyOption = root.Q<EnumField>("bundledCopyOption");
            mBundledCopyOption.Init(PulletBuildSettingData.Setting.bundledCopyOption);
            mBundledCopyOption.SetValueWithoutNotify(PulletBuildSettingData.Setting.bundledCopyOption);
            mBundledCopyOption.style.width = 350;
            mBundledCopyOption.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.IsDirty = true;
                PulletBuildSettingData.Setting.bundledCopyOption = (EPulletBundledCopyOption)mBundledCopyOption.value;
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
                defaultModeIndex = 0;

            mBuildModeField = new PopupField<Enum>(buildModeList, defaultModeIndex);
            mBuildModeField.label = "Build Mode";
            mBuildModeField.style.width = 350;
            mBuildModeField.RegisterValueChangedCallback(evt =>
            {
                PulletBuildSettingData.IsDirty = true;
                PulletBuildSettingData.Setting.buildMode = (EPulletResourceBuildMode)mBuildModeField.value;
            });
            buildModeContainer.Add(mBuildModeField);
        }

        private List<Enum> GetSupportBuildModes()
        {
            List<Enum> buildModeList = new List<Enum>();
            buildModeList.Add(EPulletResourceBuildMode.Incremental);
            buildModeList.Add(EPulletResourceBuildMode.ForceRebuild);
            return buildModeList;
        }
        /// <summary>
        /// 热梗相关
        /// </summary>
        /// <param name="root"></param>
        private void InitHybridCLR(VisualElement root)
        {
            mHybridCLRContainer = root.Q<VisualElement>("hybridCLRContainer");
            if (PulletEditorAssetUtility.FindType(HybridCommandTypeName) == null)
            {
                mHybridCLRContainer.style.display = DisplayStyle.None;
                return;
            }

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
            if (mHybridCLRAOTContainer != null)
                UpdateHybridCLR();
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
            if (PulletEditorAssetUtility.FindType(HybridCommandTypeName) != null)
                CompileHybridCLR();
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

        private void CompileHybridCLR()
        {
            BuildTarget buildTarget = PulletBuildSettingData.Setting.GetBuildTarget();
            if (PulletBuildSettingData.Setting.generateAOTDlls)
                InvokeHybridCommand("GenerateAOTDlls", buildTarget);
            if (PulletBuildSettingData.Setting.copyAOTDlls)
                InvokeHybridCommand("CopyAOTAssembliesToAssetPath");

            if (PulletBuildSettingData.Setting.compileHotUpdateDlls)
                InvokeHybridCommand("CompileDll", buildTarget);
            if (PulletBuildSettingData.Setting.copyHotUpdateDlls)
                InvokeHybridCommand("CopyHotUpdateAssembliesToAssetPath");
        }

        private static void InvokeHybridCommand(string methodName, params object[] arguments)
        {
            Type type = PulletEditorAssetUtility.FindType(HybridCommandTypeName);
            var method = type?.GetMethods(System.Reflection.BindingFlags.Public |
                                          System.Reflection.BindingFlags.Static)
                .FirstOrDefault(candidate => candidate.Name == methodName &&
                                             candidate.GetParameters().Length == arguments.Length);
            if (method == null)
                throw new MissingMethodException(HybridCommandTypeName, methodName);
            method.Invoke(null, arguments);
        }

        /// <summary>
        /// 收集shader
        /// </summary>
        private void CollectorShaderVariant()
        {
            Debug.LogWarning("YooAsset 3.x no longer provides ShaderVariantCollector. " +
                             "Add a ShaderVariantCollection asset to the package collector when one is required.");
            EditorApplication.delayCall += ExecuteBuildAssetBundle;
        }


        /// <summary>
        /// 执行构建AssetBundle
        /// </summary>
        private void ExecuteBuildAssetBundle()
        {
            //默认管线
            if (PulletBuildSettingData.Setting.buildPipeline == EPulletBuildPipeline.LegacyBuildPipeline)
            {
                LegacyBuildPipelineBuild();
            }
            else if (PulletBuildSettingData.Setting.buildPipeline == EPulletBuildPipeline.ScriptableBuildPipeline)
            {
                ScriptableBuildPipeline_Build();
            }
        }

        private void LegacyBuildPipelineBuild()
        {
            LegacyBuildParameters buildParameters = new LegacyBuildParameters();
            buildParameters.BuildOutputRoot = BundleBuilderHelper.GetDefaultBuildOutputRoot();
            buildParameters.BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot();
            buildParameters.BuildPipeline = PulletBuildSettingData.Setting.buildPipeline.ToString();
            buildParameters.BuildTarget = PulletBuildSettingData.Setting.GetBuildTarget();
            buildParameters.ClearBuildCacheFiles = PulletBuildSettingData.Setting.buildMode == EPulletResourceBuildMode.ForceRebuild;
            buildParameters.PackageName = PulletBuildSettingData.Setting.buildPackage;
            buildParameters.PackageVersion = GetBuildPackageVersion();
            buildParameters.EnableSharePackRule = true;
            buildParameters.VerifyBuildingResult = true;

            buildParameters.FileNameStyle = (EFileNameStyle)(int)PulletBuildSettingData.Setting.outputNameStyle;
            buildParameters.BundledCopyOption = (EBundledCopyOption)(int)PulletBuildSettingData.Setting.bundledCopyOption;
            buildParameters.BundledCopyParams = "";
            buildParameters.BundleEncryptor = CreateEncryptionServicesInstance();
            buildParameters.CompressOption = ECompressOption.LZ4;
            LegacyBuildPipeline pipeline = new LegacyBuildPipeline();
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

        private void ScriptableBuildPipeline_Build()
        {
            ScriptableBuildParameters buildParameters = new ScriptableBuildParameters();
            buildParameters.BuildOutputRoot = BundleBuilderHelper.GetDefaultBuildOutputRoot();
            buildParameters.BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot();
            buildParameters.BuildPipeline = PulletBuildSettingData.Setting.buildPipeline.ToString();
            buildParameters.BuildTarget = PulletBuildSettingData.Setting.GetBuildTarget();
            buildParameters.ClearBuildCacheFiles = PulletBuildSettingData.Setting.buildMode == EPulletResourceBuildMode.ForceRebuild;
            buildParameters.PackageName = PulletBuildSettingData.Setting.buildPackage;
            buildParameters.PackageVersion = GetBuildPackageVersion();
            buildParameters.EnableSharePackRule = true;
            buildParameters.VerifyBuildingResult = true;

            buildParameters.FileNameStyle = (EFileNameStyle)(int)PulletBuildSettingData.Setting.outputNameStyle;
            buildParameters.BundledCopyOption = (EBundledCopyOption)(int)PulletBuildSettingData.Setting.bundledCopyOption;
            buildParameters.BundledCopyParams = "";
            buildParameters.BundleEncryptor = CreateEncryptionServicesInstance();
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
            PulletPlayerBuildService.Build(PulletBuildSettingData.Setting);
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
                FileUtil.DeleteFileOrDirectory(destPath);
                FileUtil.CopyFileOrDirectory(sourcePath, destPath);

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

        private async void UploadAssetBundlesToTencentCOS()
        {
            AssetDatabase.Refresh();
            string cosKey = $"{PulletBuildSettingData.Setting.buildPackage}/{PulletBuildSettingData.Setting.appVersion}.zip";
            string srcPath = $"{PulletBuildSettingData.Setting.GetVersionPath()}.zip";
            await TencentCOS.PutObject(cosKey, srcPath);
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
            foreach (var package in BundleCollectorSettingData.Setting.Packages)
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
            return EditorAssemblyUtility.GetAssignableTypes(typeof(IBundleEncryptor));
        }

        private IBundleEncryptor CreateEncryptionServicesInstance()
        {
            if (mEncryptionField.index < 0)
                return null;
            var classType = mEncryptionServicesClassTypes[mEncryptionField.index];
            return (IBundleEncryptor)Activator.CreateInstance(classType);
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
