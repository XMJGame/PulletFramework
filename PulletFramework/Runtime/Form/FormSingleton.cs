using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PulletFramework.Resource;

namespace PulletFramework.Form
{
    public abstract class FormSingleton<T, TData> where T : class where TData : class //,IFormSingleton
    {
        private static T _Instance;
        public static T Instance
        {
            get
            {
                return _Instance;
            }
        }

        public FormSingleton()
        {
            _Instance = this as T;
            PulletFrameworks.StartCoroutine(PreLoadTextAsset());
        }

        /// <summary>
        /// 表格路径
        /// </summary>
        public abstract string formPath { get; }

        protected Dictionary<int, TData> m_DataDict;
        public Dictionary<int, TData> dataDict { get { return m_DataDict; } }
        protected List<int> m_IdList;
        public List<int> idList { get { return m_IdList; } }
        public bool IsLoaded => m_DataDict != null;
        public string LoadError { get; private set; }

        public IEnumerator PreLoadTextAsset()
        {
            int generation = PulletForm.BeginRead(ResetInstance);
            LoadError = null;
            IResourceAssetHandle assetHandle = null;
            try
            {
                try
                {
                    assetHandle = PulletResources.LoadAssetAsync<TextAsset>(formPath);
                }
                catch (System.Exception exception)
                {
                    LoadError = exception.Message;
                    PLogger.Error($"加载表格失败：{formPath}\n{exception.Message}");
                }

                if (assetHandle != null)
                {
                    yield return assetHandle;
                    if (PulletForm.IsCurrentRead(generation))
                    {
                        if (assetHandle.IsSucceeded && assetHandle.AssetObject is TextAsset textAsset)
                        {
                            try
                            {
                                Read(textAsset);
                            }
                            catch (System.Exception exception)
                            {
                                LoadError = exception.Message;
                                PLogger.Error($"解析表格失败：{formPath}\n{exception.Message}");
                            }
                        }
                        else
                        {
                            LoadError = string.IsNullOrEmpty(assetHandle.Error)
                                ? $"加载表格失败：{formPath}"
                                : assetHandle.Error;
                            PLogger.Error($"加载表格失败：{formPath}，{assetHandle.Error}");
                        }
                    }
                }
            }
            finally
            {
                assetHandle?.Release();
                PulletForm.CompleteRead(generation);
            }
        }

        private static void ResetInstance()
        {
            _Instance = null;
        }

        private void Read(TextAsset textAsset)
        {
            m_DataDict = Parse(textAsset)
                ?? throw new System.InvalidOperationException($"表格解析结果为空：{formPath}");
            m_IdList = new List<int>(m_DataDict.Keys);
        }

        /// <summary>
        /// 默认使用框架自带的 TSV 解析器。业务可以重写此方法，选择 JSON、二进制或代码生成方案。
        /// </summary>
        protected virtual Dictionary<int, TData> Parse(TextAsset textAsset)
        {
            return ReadFormTool.ReadFormData<TData>(textAsset);
        }

        public TData GetDateById(int id)
        {
            if (m_DataDict != null && m_DataDict.TryGetValue(id, out TData data))
            {
                return data;
            }
            else
            {
                return default(TData);
            }
        }
    }
}
