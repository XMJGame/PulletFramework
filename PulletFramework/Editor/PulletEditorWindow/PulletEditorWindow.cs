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
    public class PulletEditorWindow : EditorWindow
    {
        [MenuItem("Pullets/PulletEditor Setting", false, 3)]
        public static void OpenWindow()
        {
            PulletEditorWindow window = GetWindow<PulletEditorWindow>("Editor Setting", true, WindowsDefine.DockedWindowTypes);
            window.minSize = new Vector2(800, 600);
        }

        //android
        private Button mAndroidSettingBtn;
        private VisualElement mAndroidContainer;
        private TextField mKeystoreNameField;
        private TextField mKeystorePassField;
        private TextField mKeyaliasNameField;
        private TextField mKeyaliasPassField;

        //cos
        private Button mTencentCOSSettingBtn;
        private VisualElement mTencentCOSContainer;
        private TextField mSecretIdField;
        private TextField mSecretKeyField;
        private TextField mBucketField;
        private TextField mCosKeyField;

        public void CreateGUI()
        {
            try
            {
                VisualElement root = this.rootVisualElement;

                // 加载布局文件
                var visualAsset = UxmlLoader.LoadWindowUXML<PulletEditorWindow>();
                if (visualAsset == null)
                    return;

                visualAsset.CloneTree(root);

                //android 
                mAndroidSettingBtn = root.Q<Button>("androidSetting");
                mAndroidSettingBtn.clicked += OnAndroidSettingBtnCallBack;

                mAndroidContainer = root.Q<VisualElement>("androidContainer");

                mKeystoreNameField = root.Q<TextField>("keystoreName");
                mKeystoreNameField.SetValueWithoutNotify(PulletEditorSettingData.Setting.keystoreName);
                mKeystoreNameField.RegisterValueChangedCallback(evt =>
                {
                    PulletEditorSettingData.IsDirty = true;
                    PulletEditorSettingData.Setting.keystoreName = mKeystoreNameField.value;
                    RefreshWindow();
                });

                mKeystorePassField = root.Q<TextField>("keystorePass");
                mKeystorePassField.SetValueWithoutNotify(PulletEditorSettingData.Setting.keystorePass);
                mKeystorePassField.RegisterValueChangedCallback(evt =>
                {
                    PulletEditorSettingData.IsDirty = true;
                    PulletEditorSettingData.Setting.keystorePass = mKeystorePassField.value;
                    RefreshWindow();
                });

                mKeyaliasNameField = root.Q<TextField>("keyaliasName");
                mKeyaliasNameField.SetValueWithoutNotify(PulletEditorSettingData.Setting.keyaliasName);
                mKeyaliasNameField.RegisterValueChangedCallback(evt =>
                {
                    PulletEditorSettingData.IsDirty = true;
                    PulletEditorSettingData.Setting.keyaliasName = mKeyaliasNameField.value;
                    RefreshWindow();
                });

                mKeyaliasPassField = root.Q<TextField>("keyaliasPass");
                mKeyaliasPassField.SetValueWithoutNotify(PulletEditorSettingData.Setting.keyaliasPass);
                mKeyaliasPassField.RegisterValueChangedCallback(evt =>
                {
                    PulletEditorSettingData.IsDirty = true;
                    PulletEditorSettingData.Setting.keyaliasPass = mKeyaliasPassField.value;
                    RefreshWindow();
                });


                // tencent cos 
                mTencentCOSSettingBtn = root.Q<Button>("tencentCOSSetting");
                mTencentCOSSettingBtn.clicked += OnTencentCOSSettingBtnCallBack;

                mTencentCOSContainer = root.Q<VisualElement>("tencentCOSContainer");
                mSecretIdField = root.Q<TextField>("secretId");
                mSecretIdField.SetValueWithoutNotify(PulletEditorSettingData.Setting.secretId);
                mSecretIdField.RegisterValueChangedCallback(evt =>
                {
                    PulletEditorSettingData.IsDirty = true;
                    PulletEditorSettingData.Setting.secretId = mSecretIdField.value;
                    RefreshWindow();
                });

                mSecretKeyField = root.Q<TextField>("secretKey");
                mSecretKeyField.SetValueWithoutNotify(PulletEditorSettingData.Setting.secretKey);
                mSecretKeyField.RegisterValueChangedCallback(evt =>
                {
                    PulletEditorSettingData.IsDirty = true;
                    PulletEditorSettingData.Setting.secretKey = mSecretKeyField.value;
                    RefreshWindow();
                });

                mBucketField = root.Q<TextField>("bucket");
                mBucketField.SetValueWithoutNotify(PulletEditorSettingData.Setting.bucket);
                mBucketField.RegisterValueChangedCallback(evt =>
                {
                    PulletEditorSettingData.IsDirty = true;
                    PulletEditorSettingData.Setting.bucket = mBucketField.value;
                    RefreshWindow();
                });

                mCosKeyField = root.Q<TextField>("cosKey");
                mCosKeyField.SetValueWithoutNotify(PulletEditorSettingData.Setting.cosKey);
                mCosKeyField.RegisterValueChangedCallback(evt =>
                {
                    PulletEditorSettingData.IsDirty = true;
                    PulletEditorSettingData.Setting.cosKey = mCosKeyField.value;
                    RefreshWindow();
                });

                // 刷新窗体
                RefreshWindow();
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }

        public void OnDestroy()
        {
            if (PulletEditorSettingData.IsDirty)
                PulletEditorSettingData.SaveFile();
        }

        private void Update()
        {

        }

        private void RefreshWindow()
        {
            if (PulletEditorSettingData.Setting.androidSetting)
            {
                mAndroidContainer.style.display = DisplayStyle.Flex;
            }
            else 
            {
                mAndroidContainer.style.display = DisplayStyle.None;
            }

            if (PulletEditorSettingData.Setting.tencentCOSSetting)
            {
                mTencentCOSContainer.style.display = DisplayStyle.Flex;
            }
            else
            {
                mTencentCOSContainer.style.display = DisplayStyle.None;
            }
        }

        private void OnAndroidSettingBtnCallBack()
        {
            PulletEditorSettingData.IsDirty = true;
            PulletEditorSettingData.Setting.androidSetting = !PulletEditorSettingData.Setting.androidSetting;
            RefreshWindow();
        }
        private void OnTencentCOSSettingBtnCallBack()
        {
            PulletEditorSettingData.IsDirty = true;
            PulletEditorSettingData.Setting.tencentCOSSetting = !PulletEditorSettingData.Setting.tencentCOSSetting;
            RefreshWindow();
        }
    }
}
