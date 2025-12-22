using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastleDB.Runtime
{
    /// <summary>
    /// CastleDB JSON 工具类（公开 API）
    /// 用于跨程序集访问 JSON 解析功能
    /// </summary>
    public static class CastleDbJsonUtil
    {
        /// <summary>
        /// 尝试解析 JSON 字符串为对象（Dictionary）
        /// 用于校验 paramsJson 是否为合法的 JSON 对象
        /// </summary>
        /// <param name="json">JSON 字符串</param>
        /// <returns>如果是对象则返回 Dictionary，否则返回 null</returns>
        public static Dictionary<string, object> TryParseJsonObject(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                var parser = new SimpleJsonParser(json);
                var result = parser.Parse();
                return result as Dictionary<string, object>;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 从 TextAsset 读取 CastleDB JSON 数据
    /// 用于在 Unity 中加载 .cdb 文件
    /// 使用简单的 JSON 解析支持动态对象和字典反序列化
    /// </summary>
    public class CastleDbJsonSource : ICastleDbSource
    {
        private TextAsset _jsonAsset;

        public CastleDbJsonSource(TextAsset jsonAsset)
        {
            _jsonAsset = jsonAsset;
        }

        public CastleDbRoot ReadCastleDbJson()
        {
            if (_jsonAsset == null)
            {
                Debug.LogError("[CastleDbJsonSource] JSON Asset 为空");
                return null;
            }

            try
            {
                var root = ParseCastleDbJson(_jsonAsset.text);
                if (root == null)
                {
                    Debug.LogError("[CastleDbJsonSource] JSON 解析失败");
                    return null;
                }

                Debug.Log($"[CastleDbJsonSource] 成功读取 JSON，Sheet 数量：{root.sheets.Count}");
                return root;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CastleDbJsonSource] JSON 解析异常：{ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// 使用简单的 JSON 解析 CastleDB JSON
        /// 将 lines 字段解析为 List<Dictionary<string, object>>
        /// </summary>
        private CastleDbRoot ParseCastleDbJson(string json)
        {
            var root = new CastleDbRoot();
            var parser = new SimpleJsonParser(json);
            var obj = parser.Parse();

            if (obj is Dictionary<string, object> rootDict && rootDict.TryGetValue("sheets", out var sheetsObj))
            {
                if (sheetsObj is List<object> sheets)
                {
                    foreach (var sheetObj in sheets)
                    {
                        if (sheetObj is Dictionary<string, object> sheetDict)
                        {
                            var sheet = new SheetData();

                            if (sheetDict.TryGetValue("name", out var nameObj))
                            {
                                sheet.name = nameObj?.ToString() ?? "";
                            }

                            // 解析 lines 为 List<Dictionary<string, object>>
                            if (sheetDict.TryGetValue("lines", out var linesObj) && linesObj is List<object> lines)
                            {
                                sheet.lines = new List<object>();
                                foreach (var lineObj in lines)
                                {
                                    if (lineObj is Dictionary<string, object> lineDict)
                                    {
                                        sheet.lines.Add(lineDict);
                                    }
                                }
                            }

                            root.sheets.Add(sheet);
                        }
                    }
                }
            }

            return root;
        }

        public string GetSourceDescription()
        {
            return $"TextAsset: {_jsonAsset.name}";
        }
    }

    /// <summary>
    /// 从文件路径读取 CastleDB JSON 数据
    /// 用于编辑器工具中加载 .cdb 文件
    /// 使用简单的 JSON 解析支持动态对象和字典反序列化
    /// </summary>
    public class CastleDbFileSource : ICastleDbSource
    {
        private string _filePath;

        public CastleDbFileSource(string filePath)
        {
            _filePath = filePath;
        }

        public CastleDbRoot ReadCastleDbJson()
        {
            if (!System.IO.File.Exists(_filePath))
            {
                Debug.LogError($"[CastleDbFileSource] 文件不存在：{_filePath}");
                return null;
            }

            try
            {
                string json = System.IO.File.ReadAllText(_filePath);
                var root = ParseCastleDbJson(json);
                if (root == null)
                {
                    Debug.LogError("[CastleDbFileSource] JSON 解析失败");
                    return null;
                }

                Debug.Log($"[CastleDbFileSource] 成功读取文件，Sheet 数量：{root.sheets.Count}");
                return root;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CastleDbFileSource] 读取文件异常：{ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// 使用简单的 JSON 解析 CastleDB JSON
        /// 将 lines 字段解析为 List<Dictionary<string, object>>
        /// </summary>
        private CastleDbRoot ParseCastleDbJson(string json)
        {
            var root = new CastleDbRoot();
            var parser = new SimpleJsonParser(json);
            var obj = parser.Parse();

            if (obj is Dictionary<string, object> rootDict && rootDict.TryGetValue("sheets", out var sheetsObj))
            {
                if (sheetsObj is List<object> sheets)
                {
                    foreach (var sheetObj in sheets)
                    {
                        if (sheetObj is Dictionary<string, object> sheetDict)
                        {
                            var sheet = new SheetData();

                            if (sheetDict.TryGetValue("name", out var nameObj))
                            {
                                sheet.name = nameObj?.ToString() ?? "";
                            }

                            // 解析 lines 为 List<Dictionary<string, object>>
                            if (sheetDict.TryGetValue("lines", out var linesObj) && linesObj is List<object> lines)
                            {
                                sheet.lines = new List<object>();
                                foreach (var lineObj in lines)
                                {
                                    if (lineObj is Dictionary<string, object> lineDict)
                                    {
                                        sheet.lines.Add(lineDict);
                                    }
                                }
                            }

                            root.sheets.Add(sheet);
                        }
                    }
                }
            }

            return root;
        }

        public string GetSourceDescription()
        {
            return $"File: {_filePath}";
        }
    }

    /// <summary>
    /// 简单的 JSON 解析器
    /// 支持基本的 JSON 结构解析为 Dictionary 和 List
    /// </summary>
    internal class SimpleJsonParser
    {
        private string _json;
        private int _pos;

        public SimpleJsonParser(string json)
        {
            _json = json;
            _pos = 0;
        }

        public object Parse()
        {
            SkipWhitespace();
            return ParseValue();
        }

        private object ParseValue()
        {
            SkipWhitespace();

            if (_pos >= _json.Length)
                return null;

            char c = _json[_pos];

            return c switch
            {
                '{' => ParseObject(),
                '[' => ParseArray(),
                '"' => ParseString(),
                't' or 'f' => ParseBoolean(),
                'n' => ParseNull(),
                '-' or >= '0' and <= '9' => ParseNumber(),
                _ => null
            };
        }

        private Dictionary<string, object> ParseObject()
        {
            var dict = new Dictionary<string, object>();
            _pos++; // skip '{'
            SkipWhitespace();

            if (_pos < _json.Length && _json[_pos] == '}')
            {
                _pos++;
                return dict;
            }

            while (_pos < _json.Length)
            {
                SkipWhitespace();
                string key = ParseString();
                SkipWhitespace();

                if (_pos >= _json.Length || _json[_pos] != ':')
                    break;

                _pos++; // skip ':'
                object value = ParseValue();
                dict[key] = value;

                SkipWhitespace();
                if (_pos < _json.Length && _json[_pos] == ',')
                {
                    _pos++;
                }
                else if (_pos < _json.Length && _json[_pos] == '}')
                {
                    _pos++;
                    break;
                }
            }

            return dict;
        }

        private List<object> ParseArray()
        {
            var list = new List<object>();
            _pos++; // skip '['
            SkipWhitespace();

            if (_pos < _json.Length && _json[_pos] == ']')
            {
                _pos++;
                return list;
            }

            while (_pos < _json.Length)
            {
                object value = ParseValue();
                list.Add(value);

                SkipWhitespace();
                if (_pos < _json.Length && _json[_pos] == ',')
                {
                    _pos++;
                }
                else if (_pos < _json.Length && _json[_pos] == ']')
                {
                    _pos++;
                    break;
                }
            }

            return list;
        }

        private string ParseString()
        {
            _pos++; // skip opening '"'
            var sb = new System.Text.StringBuilder();

            while (_pos < _json.Length && _json[_pos] != '"')
            {
                if (_json[_pos] == '\\' && _pos + 1 < _json.Length)
                {
                    _pos++;
                    char escaped = _json[_pos];
                    sb.Append(escaped switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '\\' => '\\',
                        '"' => '"',
                        _ => escaped
                    });
                }
                else
                {
                    sb.Append(_json[_pos]);
                }
                _pos++;
            }

            if (_pos < _json.Length)
                _pos++; // skip closing '"'

            return sb.ToString();
        }

        private object ParseNumber()
        {
            int start = _pos;
            if (_json[_pos] == '-')
                _pos++;

            while (_pos < _json.Length && char.IsDigit(_json[_pos]))
                _pos++;

            if (_pos < _json.Length && _json[_pos] == '.')
            {
                _pos++;
                while (_pos < _json.Length && char.IsDigit(_json[_pos]))
                    _pos++;
            }

            string numStr = _json.Substring(start, _pos - start);

            if (double.TryParse(numStr, out var doubleVal))
            {
                if (doubleVal == (int)doubleVal)
                    return (int)doubleVal;
                return doubleVal;
            }

            return 0;
        }

        private bool ParseBoolean()
        {
            if (_json.Substring(_pos).StartsWith("true"))
            {
                _pos += 4;
                return true;
            }
            else if (_json.Substring(_pos).StartsWith("false"))
            {
                _pos += 5;
                return false;
            }
            return false;
        }

        private object ParseNull()
        {
            if (_json.Substring(_pos).StartsWith("null"))
            {
                _pos += 4;
                return null;
            }
            return null;
        }

        private void SkipWhitespace()
        {
            while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos]))
                _pos++;
        }
    }
}
