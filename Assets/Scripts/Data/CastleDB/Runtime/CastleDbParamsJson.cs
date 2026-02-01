using System.Collections.Generic;
using UnityEngine;

namespace CastleDB.Runtime
{
    /// <summary>
    /// paramsJson 解析工具（0.5 / 2.2）
    /// 目标：
    /// - 将高频字段（如 animTrigger/releaseDelay）从 JSON 中结构化出来，减少运行时重复解析与 GC Alloc
    /// - 同时为旧产物提供“只解析一次”的兜底能力（配合非序列化缓存字段使用）
    /// </summary>
    public static class CastleDbParamsJson
    {
        private const string AnimTriggerKey = "animTrigger";
        private const string ReleaseDelayKey = "releaseDelay";

        /// <summary>
        /// 解析“施法公共参数”（目前仅 animTrigger/releaseDelay）。
        /// 约定：
        /// - animTrigger：空/缺失视为“不覆盖”（由调用方做 fallback）
        /// - releaseDelay：缺失/非法视为 0；且会被 clamp 到 >=0
        /// </summary>
        public static void ParseAnimTriggerAndReleaseDelay(string paramsJson, out string animTrigger, out float releaseDelaySeconds)
        {
            animTrigger = "";
            releaseDelaySeconds = 0f;

            if (string.IsNullOrWhiteSpace(paramsJson))
            {
                return;
            }

            Dictionary<string, object> obj = CastleDbJsonUtil.TryParseJsonObject(paramsJson);
            if (obj == null)
            {
                return;
            }

            if (obj.TryGetValue(AnimTriggerKey, out var rawTrigger) && rawTrigger != null)
            {
                string trigger = rawTrigger.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(trigger))
                {
                    animTrigger = trigger;
                }
            }

            if (TryReadFloat(obj, ReleaseDelayKey, out float rawDelay))
            {
                releaseDelaySeconds = Mathf.Max(0f, rawDelay);
            }
        }

        private static bool TryReadFloat(Dictionary<string, object> obj, string key, out float value)
        {
            value = 0f;

            if (obj == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (!obj.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }

            switch (raw)
            {
                case float f:
                    value = f;
                    return true;
                case double d:
                    value = (float)d;
                    return true;
                case int i:
                    value = i;
                    return true;
                case long l:
                    value = l;
                    return true;
                case string s:
                    return float.TryParse(s, out value);
            }

            return false;
        }
    }
}

