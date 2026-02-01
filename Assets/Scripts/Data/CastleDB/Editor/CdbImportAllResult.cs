using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CastleDB.Runtime;

namespace CastleDB.Editor
{
    public class CdbImportAllResult
    {
        public bool IsSuccess { get; }
        public IReadOnlyList<string> LogMessages { get; }
        public IReadOnlyList<CdbImportResult> ProviderResults { get; }

        private CdbImportAllResult(bool success, List<string> logMessages, List<CdbImportResult> providerResults)
        {
            IsSuccess = success;
            LogMessages = logMessages ?? new List<string>();
            ProviderResults = providerResults ?? new List<CdbImportResult>();
        }

        public static CdbImportAllResult Success(List<string> logMessages, List<CdbImportResult> providerResults)
        {
            return new CdbImportAllResult(true, logMessages, providerResults);
        }

        public static CdbImportAllResult Failure(List<string> logMessages)
        {
            return new CdbImportAllResult(false, logMessages, null);
        }

        /// <summary>
        /// 获取格式化的日志文本
        /// </summary>
        public string GetFormattedLog()
        {
            return string.Join("\n", LogMessages);
        }
    }
}
