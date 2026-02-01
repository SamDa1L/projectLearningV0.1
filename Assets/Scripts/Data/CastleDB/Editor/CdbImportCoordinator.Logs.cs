using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CastleDB.Runtime;

namespace CastleDB.Editor
{
    public partial class CdbImportCoordinator
    {
        #region 日志访问

        /// <summary>
        /// 获取所有日志消息
        /// </summary>
        public IReadOnlyList<string> GetLogMessages()
        {
            return _logMessages.AsReadOnly();
        }

        #endregion
    }
}
