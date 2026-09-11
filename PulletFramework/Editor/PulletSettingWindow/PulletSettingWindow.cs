using PulletFramework.Setting;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PulletFramework.Editor
{
    public class PulletSettingWindow : EditorWindow
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
            PulletSettingWindow window = GetWindow<PulletSettingWindow>("Settings", true, WindowsDefine.DockedWindowTypes);
            window.minSize = new Vector2(800, 600);
        }

        /// <summary>
        /// YooAsset
        /// </summary>
        private VisualElement mYooAssetSetting;
        private Button mYooAssetSettingButton;
        private VisualElement mYooAssetSettingContainer;
        private EnumField mRunPlayModeEField;
        private TextField mDefaultHostServerTField;
        private TextField mFallbackHostServerTField;
        private TextField mDefaultPackageNameTField;

        /// <summary>
        /// HybridCLR
        /// </summary>
        private VisualElement mHybridCLRSetting;
        private Button mHybridCLRSettingButton;
        private VisualElement mHybridCLRSettingContainer;
        private TextField mDefaultHotUpdateAssemblyTField;
        private PropertyField mHotUpdateAssembliesPField;
        private PropertyField mAotMetaAssemblysPField;


        SerializedObject serializedObject;
        public void CreateGUI()
        {
            try
            {
                VisualElement root = this.rootVisualElement;

                // 加载布局文件
                var visualAsset = PulletEditorAssetUtility.LoadWindowUxml<PulletSettingWindow>();
                if (visualAsset == null)
                    return;

                visualAsset.CloneTree(root);

                // app 信息
                InitYooAsset(root);

                //HybridCLR
                InitHybridCLR(root);

                // 刷新窗体
                RefreshWindow();
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }

        private void InitYooAsset(VisualElement root)
        {
            //设置
            mYooAssetSettingButton = root.Q<Button>("yooAssetSettingBtn");
            mYooAssetSettingButton.clicked += OnYooAssetSettingBtnCallBack;
            mYooAssetSettingContainer = root.Q<VisualElement>("yooAssetSettingContainer");

            mRunPlayModeEField = root.Q<EnumField>("runPlayMode");
            mRunPlayModeEField.Init(PulletSettingsData.Setting.resourcePlayMode);
            mRunPlayModeEField.SetValueWithoutNotify(PulletSettingsData.Setting.resourcePlayMode);
            mRunPlayModeEField.style.width = 350;
            mRunPlayModeEField.RegisterValueChangedCallback(evt =>
            {
                PulletSettingsData.IsDirty = true;
                PulletSettingsData.Setting.resourcePlayMode = (EResourcePlayMode)mRunPlayModeEField.value;
                RefreshWindow();
            });
            mDefaultHostServerTField = root.Q<TextField>("defaultHostServer");
            mDefaultHostServerTField.SetValueWithoutNotify(PulletSettingsData.Setting.defaultHostServer);
            mDefaultHostServerTField.RegisterValueChangedCallback(evt =>
            {
                PulletSettingsData.IsDirty = true;
                PulletSettingsData.Setting.defaultHostServer = mDefaultHostServerTField.value;
                RefreshWindow();
            });
            mFallbackHostServerTField = root.Q<TextField>("fallbackHostServer");
            mFallbackHostServerTField.SetValueWithoutNotify(PulletSettingsData.Setting.fallbackHostServer);
            mFallbackHostServerTField.RegisterValueChangedCallback(evt =>
            {
                PulletSettingsData.IsDirty = true;
                PulletSettingsData.Setting.fallbackHostServer = mFallbackHostServerTField.value;
                RefreshWindow();
            });
            mDefaultPackageNameTField = root.Q<TextField>("defaultPackageName");
            mDefaultPackageNameTField.SetValueWithoutNotify(PulletSettingsData.Setting.defaultPackageName);
            mDefaultPackageNameTField.RegisterValueChangedCallback(evt =>
            {
                PulletSettingsData.IsDirty = true;
                PulletSettingsData.Setting.defaultPackageName = mDefaultPackageNameTField.value;
                RefreshWindow();
            });
        }

        private void InitHybridCLR(VisualElement root)
        {
            mHybridCLRSetting = root.Q<VisualElement>("hybridCLRSetting");
            if (PulletEditorAssetUtility.FindType(HybridCommandTypeName) == null)
            {
                mHybridCLRSetting.style.display = DisplayStyle.None;
                return;
            }

            mHybridCLRSetting.style.display = DisplayStyle.Flex;
            //设置
            mHybridCLRSettingButton = root.Q<Button>("hybridCLRSettingBtn");
            mHybridCLRSettingButton.clicked += OnHybridCLRSSettingBtnCallBack;
            mHybridCLRSettingContainer = root.Q<VisualElement>("hybridCLRSettingContainer");


            mDefaultHotUpdateAssemblyTField = root.Q<TextField>("defaultHotUpdateAssembly");
            mDefaultHotUpdateAssemblyTField.SetValueWithoutNotify(PulletSettingsData.Setting.defaultHotUpdateAssemblyName);
            mDefaultHotUpdateAssemblyTField.RegisterValueChangedCallback(evt =>
            {
                PulletSettingsData.IsDirty = true;
                PulletSettingsData.Setting.defaultHotUpdateAssemblyName = mDefaultHotUpdateAssemblyTField.value;
                RefreshWindow();
            });

            PulletSettingsData.Setting.hotUpdateAssemblies.Clear();

            foreach (string dll in GetHybridAssemblyNames("GetHotUpdateAssemblyNames"))
            {
                PulletSettingsData.Setting.hotUpdateAssemblies.Add(dll);
            }

            serializedObject = new SerializedObject(PulletSettingsData.Setting);
            mHotUpdateAssembliesPField = root.Q<PropertyField>("hotUpdateAssemblies");
            mHotUpdateAssembliesPField.BindProperty(serializedObject.FindProperty("hotUpdateAssemblies"));
            mHotUpdateAssembliesPField.SetEnabled(false);


            PulletSettingsData.Setting.aotMetaAssemblys.Clear();

            foreach (string dll in GetHybridAssemblyNames("GetAOTAssemblyNames"))
            {
                PulletSettingsData.Setting.aotMetaAssemblys.Add(dll);
            }
            mAotMetaAssemblysPField = root.Q<PropertyField>("aotMetaAssemblys");
            mAotMetaAssemblysPField.BindProperty(serializedObject.FindProperty("aotMetaAssemblys"));
            mAotMetaAssemblysPField.SetEnabled(false);

            PulletSettingsData.SaveFile();
        }

        private static IEnumerable<string> GetHybridAssemblyNames(string methodName)
        {
            Type type = PulletEditorAssetUtility.FindType(HybridCommandTypeName);
            var method = type?.GetMethod(methodName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            return method?.Invoke(null, null) as IEnumerable<string> ?? Array.Empty<string>();
        }

        public void OnDestroy()
        {
            if (PulletSettingsData.IsDirty)
                PulletSettingsData.SaveFile();
        }

        private void RefreshWindow()
        {
            if (yooAssetSetting)
            {
                mYooAssetSettingContainer.style.display = DisplayStyle.Flex;

                if (PulletSettingsData.Setting.resourcePlayMode == EResourcePlayMode.Host)
                {
                    mDefaultHostServerTField.style.display = DisplayStyle.Flex;
                    mFallbackHostServerTField.style.display = DisplayStyle.Flex;
                }
                else
                {
                    mDefaultHostServerTField.style.display = DisplayStyle.None;
                    mFallbackHostServerTField.style.display = DisplayStyle.None;
                }
            }
            else
            {
                mYooAssetSettingContainer.style.display = DisplayStyle.None;
            }

            if (mHybridCLRSettingContainer != null)
                mHybridCLRSettingContainer.style.display = hybridCLRSetting
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
        }

        #region 按钮事件
        private bool yooAssetSetting = true;
        private void OnYooAssetSettingBtnCallBack()
        {
            yooAssetSetting = !yooAssetSetting;
            PulletSettingsData.IsDirty = true;
            RefreshWindow();
        }

        private bool hybridCLRSetting = true;

        private void OnHybridCLRSSettingBtnCallBack()
        {
            hybridCLRSetting = !hybridCLRSetting;
            PulletSettingsData.IsDirty = true;
            RefreshWindow();
        }
#endregion
    }
}
