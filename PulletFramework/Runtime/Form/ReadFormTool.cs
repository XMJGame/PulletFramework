
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PulletFramework.Form
{
    public class ReadFormTool
    {
        public static string[] mArray;
        public static List<List<string>> mFormData = new List<List<string>>();
        public static Dictionary<int, T> ReadFormData<T>(TextAsset textAsset)
        {
            mFormData.Clear();
            if (textAsset != null)
            {
                //读取每一行的内容
                string[] lineArray = textAsset.text.Split("\r"[0]);
                for (int i = 0; i < lineArray.Length; i++)
                {
                    string text = lineArray[i].Replace("\n", "");
                    mArray = text.Split("\t"[0]);
                    if (string.IsNullOrEmpty(mArray[0])) continue;
                    //存储每行数据
                    mFormData.Add(new List<string>(mArray));
                }
                return DeserializeStringToObjects<T>();
            }
            return null;
        }

        public static Dictionary<int, T> DeserializeStringToObjects<T>()
        {
            //表对应的变量
            List<string> variable = new List<string>();
            variable = mFormData[0];
            //创建表字典
            Dictionary<int, T> result = new Dictionary<int, T>();
            //对象类型
            Type type = typeof(T);
            FieldInfo[] fieldInfo = type.GetFields();

            string strError = "";
            try
            {
                string objMemberName;
                for (int row = 1; row < mFormData.Count; row++)
                {
                    int key = 0;
                    T model = Activator.CreateInstance<T>();
                    List<string> rowData = mFormData[row];
                    if (rowData[0][0] == '#')
                    {
                        continue;
                    }
                    if (!int.TryParse(rowData[0], out key))
                    {
                        key = row;
                    }

                    for (int line = 0; line < rowData.Count; line++)
                    {
                        for (int i = 0; i < fieldInfo.Length; i++)
                        {
                            objMemberName = fieldInfo[i].Name;
                            //字一个字母大写
                            //if (char.IsUpper(objMemberName[0]) && objMemberName == variable[line])
                            //{
                            //    IList data = GetTList(fieldInfo[i].FieldType, rowData[line]);
                            //    fieldInfo[i].SetValue(model, data);

                            //    break;
                            //}
                            if (objMemberName == variable[line])
                            {
                                if (key == 1409)
                                {
                                    strError = "";
                                }
                                strError = key + "----" + objMemberName + "----" + rowData[line] + "----" + fieldInfo[i].FieldType.ToString();
                                if (fieldInfo[i].FieldType == typeof(bool))
                                {
                                    int num = int.Parse(rowData[line]);
                                    fieldInfo[i].SetValue(model, Convert.ChangeType(num, fieldInfo[i].FieldType));
                                }
                                else
                                {
                                    fieldInfo[i].SetValue(model, Convert.ChangeType(rowData[line], fieldInfo[i].FieldType));
                                }
                                break;
                            }
                        }
                    }
                    result.Add(key, model);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message + ":" + strError);
            }
            finally { }
            return result;
        }

        public static void ReadConstantForm(TextAsset textAsset, ref Dictionary<string, string> result)
        {
            //TextAsset textAsset = ResourcesManager.Instance.ResourcesLoad<TextAsset>("Forms/" + fileName);
            if (textAsset != null)
            {
                //读取每一行的内容
                string[] lineArray = textAsset.text.Split("\r"[0]);
                for (int i = 1; i < lineArray.Length; i++)
                {
                    string text = lineArray[i].Replace("\n", "");
                    mArray = text.Split("\t"[0]);

                    if (mArray.Length > 2)
                    {
                        if (mArray[0][0] == '#') continue;
                        result.Add(mArray[0], mArray[1]);
                    }
                }
            }
        }
    }
}
