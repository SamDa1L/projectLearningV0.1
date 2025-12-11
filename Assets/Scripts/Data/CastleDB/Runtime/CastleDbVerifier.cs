using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastleDB.Runtime
{
    /// <summary>
    /// CastleDB 验证器
    /// 用于在运行时验证 CastleDB 数据的完整性和正确性
    /// </summary>
    public class CastleDbVerifier
    {
        /// <summary>
        /// 验证 CastleDB 加载结果
        /// </summary>
        public static bool VerifyLoadResult(CastleDbService service, string logFilePath = "Logs/CastleDbLoad.log")
        {
            if (service == null)
            {
                Debug.LogError("[CastleDbVerifier] 服务为空");
                return false;
            }

            var versionInfo = service.GetVersionInfo();
            if (versionInfo == null)
            {
                Debug.LogError("[CastleDbVerifier] 版本信息为空");
                return false;
            }

            Debug.Log($"[CastleDbVerifier] 版本信息: {versionInfo}");

            var npcs = service.GetAllNpcs();
            Debug.Log($"[CastleDbVerifier] NPC 数量: {npcs.Count}");

            var zones = service.GetAllDetectionZones();
            Debug.Log($"[CastleDbVerifier] 检测区数量: {zones.Count}");

            // 验证 Knight 数据
            var knight = service.GetNpcById("M_Knight");
            if (knight == null)
            {
                Debug.LogError("[CastleDbVerifier] 未找到 Knight NPC");
                return false;
            }

            Debug.Log($"[CastleDbVerifier] Knight 数据: {knight}");

            // 验证 FlyingEye 数据
            var flyingEye = service.GetNpcById("M_FlyingEye");
            if (flyingEye == null)
            {
                Debug.LogError("[CastleDbVerifier] 未找到 FlyingEye NPC");
                return false;
            }

            Debug.Log($"[CastleDbVerifier] FlyingEye 数据: {flyingEye}");

            // 验证检测区
            var knightZones = service.GetDetectionZonesByNpcId("M_Knight");
            Debug.Log($"[CastleDbVerifier] Knight 检测区数量: {knightZones.Count}");

            Debug.Log("[CastleDbVerifier] 所有验证通过!");
            return true;
        }

        /// <summary>
        /// 验证版本匹配
        /// </summary>
        public static bool VerifyVersionMatch(CastleDbService service, string expectedVersion = "0.2")
        {
            var versionInfo = service.GetVersionInfo();
            if (versionInfo == null)
            {
                Debug.LogError("[CastleDbVerifier] 版本信息为空");
                return false;
            }

            if (versionInfo.schemaVersion != expectedVersion)
            {
                Debug.LogError($"[CastleDbVerifier] 版本不匹配: 期望 {expectedVersion}，实际 {versionInfo.schemaVersion}");
                return false;
            }

            Debug.Log($"[CastleDbVerifier] 版本匹配: {versionInfo.schemaVersion}");
            return true;
        }
    }
}
