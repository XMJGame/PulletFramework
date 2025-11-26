using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

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

        public IEnumerator PreLoadTextAsset()
        {
            AssetHandle assetHandle = YooAssets.LoadAssetAsync<TextAsset>(formPath);
            yield return assetHandle;
            Read(assetHandle.AssetObject as TextAsset);
            PulletForm.readCount--;
        }

        private void Read(TextAsset textAsset)
        {
            m_DataDict = ReadFormTool.ReadFormData<TData>(textAsset);
            m_IdList = new List<int>(m_DataDict.Keys);
        }

        public TData GetDateById(int id)
        {
            if (m_DataDict.TryGetValue(id, out TData data))
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
