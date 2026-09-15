using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace PulletFramework.Form
{
    /// <summary>把以制表符分隔的 TextAsset 转换为配置对象。</summary>
    public static class ReadFormTool
    {
        // 保留旧字段供已有项目兼容，新代码请直接调用 ReadFormData。
        public static string[] mArray;
        public static List<List<string>> mFormData = new List<List<string>>();

        public static Dictionary<int, T> ReadFormData<T>(TextAsset textAsset)
        {
            return ReadFormData<T>(textAsset, false);
        }

        public static Dictionary<int, T> ReadFormData<T>(TextAsset textAsset, bool strict)
        {
            mFormData = ParseRows(textAsset);
            if (strict && mFormData.Count == 0)
                throw new FormatException($"表格 {typeof(T).Name} 没有可读取的表头或数据。");
            return DeserializeRows<T>(mFormData, strict);
        }

        public static Dictionary<int, T> DeserializeStringToObjects<T>()
        {
            return DeserializeRows<T>(mFormData, false);
        }

        private static Dictionary<int, T> DeserializeRows<T>(
            List<List<string>> rows, bool strict)
        {
            var result = new Dictionary<int, T>();
            if (rows == null || rows.Count == 0)
                return result;

            List<string> headers = rows[0];
            var fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            var errors = strict ? new List<string>() : null;
            FieldInfo[] publicFields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < publicFields.Length; i++)
                fields[publicFields[i].Name] = publicFields[i];

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                List<string> row = rows[rowIndex];
                if (row.Count == 0 || string.IsNullOrWhiteSpace(row[0]) || row[0][0] == '#')
                    continue;

                int key = int.TryParse(row[0], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int parsedKey) ? parsedKey : rowIndex;
                try
                {
                    T model = Activator.CreateInstance<T>();
                    int columnCount = Math.Min(row.Count, headers.Count);
                    for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                    {
                        string header = headers[columnIndex];
                        if (string.IsNullOrWhiteSpace(header)
                            || !fields.TryGetValue(header, out FieldInfo field))
                            continue;
                        field.SetValue(model, ConvertCell(row[columnIndex], field.FieldType));
                    }

                    if (result.ContainsKey(key))
                    {
                        string error = $"表格 {typeof(T).Name} 第 {rowIndex + 1} 行包含重复 ID：{key}。";
                        if (strict)
                            errors.Add(error);
                        else
                            PLogger.Warning(error + " 已忽略。");
                        continue;
                    }
                    result.Add(key, model);
                }
                catch (Exception exception)
                {
                    string error =
                        $"表格 {typeof(T).Name} 第 {rowIndex + 1} 行解析失败，ID={key}：{exception.Message}";
                    if (strict)
                        errors.Add(error);
                    else
                        PLogger.Error(error);
                }
            }
            if (strict && errors.Count > 0)
                throw new FormatException(string.Join("\n", errors));
            return result;
        }

        public static void ReadConstantForm(
            TextAsset textAsset, ref Dictionary<string, string> result)
        {
            if (result == null)
                result = new Dictionary<string, string>();
            List<List<string>> rows = ParseRows(textAsset);
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                List<string> row = rows[rowIndex];
                if (row.Count < 2 || string.IsNullOrWhiteSpace(row[0]) || row[0][0] == '#')
                    continue;
                result[row[0]] = row[1];
            }
        }

        private static List<List<string>> ParseRows(TextAsset textAsset)
        {
            var rows = new List<List<string>>();
            if (textAsset == null)
                return rows;

            string[] lines = textAsset.text.Split(
                new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                if (string.IsNullOrWhiteSpace(lines[lineIndex]))
                    continue;
                mArray = lines[lineIndex].Split('\t');
                if (mArray.Length == 0 || string.IsNullOrWhiteSpace(mArray[0]))
                    continue;
                rows.Add(new List<string>(mArray));
            }
            return rows;
        }

        private static object ConvertCell(string value, Type destinationType)
        {
            Type targetType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
            if (targetType == typeof(string))
                return value;
            if (targetType == typeof(bool))
            {
                if (value == "1") return true;
                if (value == "0") return false;
                if (bool.TryParse(value, out bool parsed)) return parsed;
                throw new FormatException($"'{value}' 不是有效的 bool 值。");
            }
            if (targetType.IsEnum)
                return Enum.Parse(targetType, value, true);
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
    }
}
