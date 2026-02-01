using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CastleDB.Runtime;

namespace CastleDB.Editor.Providers
{
    public partial class MonsterDataProvider : CdbDataProviderBase
    {
        #region 数据查询

        /// <summary>
        /// 获取指定类型的所有条目
        /// </summary>
        public override List<T> GetAllEntries<T>()
        {
            if (typeof(T) == typeof(NpcEntry))
            {
                return _npcs.Cast<T>().ToList();
            }

            if (typeof(T) == typeof(DetectionZoneEntry))
            {
                return _detectionZones.Cast<T>().ToList();
            }

            if (typeof(T) == typeof(NpcAbilityEntry))
            {
                return _npcAbilities.Cast<T>().ToList();
            }

            if (typeof(T) == typeof(AbilityEntry))
            {
                return _enemyAbilities.Cast<T>().ToList();
            }

            return new List<T>();
        }

        /// <summary>
        /// 按 ID 获取指定类型的条目
        /// </summary>
        public override T GetEntryById<T>(string id)
        {
            if (typeof(T) == typeof(NpcEntry))
            {
                return _npcs.FirstOrDefault(n => n.id == id) as T;
            }

            if (typeof(T) == typeof(DetectionZoneEntry))
            {
                return _detectionZones.FirstOrDefault(z => z.id == id) as T;
            }

            if (typeof(T) == typeof(NpcAbilityEntry))
            {
                return _npcAbilities.FirstOrDefault(a => a.id == id) as T;
            }

            if (typeof(T) == typeof(AbilityEntry))
            {
                return _enemyAbilities.FirstOrDefault(a => a.id == id) as T;
            }

            return null;
        }

        /// <summary>
        /// 按 NPC ID 获取检测区列表
        /// </summary>
        public List<DetectionZoneEntry> GetDetectionZonesByNpcId(string npcId)
        {
            return _detectionZones.Where(z => z.npcId == npcId).ToList();
        }

        /// <summary>
        /// 获取所有 NPC
        /// </summary>
        public List<NpcEntry> GetAllNpcs()
        {
            return new List<NpcEntry>(_npcs);
        }

        /// <summary>
        /// 获取所有检测区
        /// </summary>
        public List<DetectionZoneEntry> GetAllDetectionZones()
        {
            return new List<DetectionZoneEntry>(_detectionZones);
        }

        #endregion
        #region 重置

        /// <summary>
        /// 清除缓存数据
        /// </summary>
        protected override void OnReset()
        {
            _npcs.Clear();
            _detectionZones.Clear();
            _npcAbilities.Clear();
            _npcPassiveAbilityBindings.Clear();
            _npcPassiveAbilityConditions.Clear();
            _enemyAbilities.Clear();
            _enemyProjectiles.Clear();
            _enemyOnHitSequences.Clear();
            _npcPassiveAbilities.Clear();
        }

        #endregion
        #region 辅助方法（迁移自 CastleDbRepository）

        private string GetStringValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? "";
            }
            return "";
        }

        private float GetFloatValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
                if (float.TryParse(value?.ToString(), out var result)) return result;
            }
            return 0f;
        }

        private float GetFloatValueWithDefault(Dictionary<string, object> dict, string key, float defaultValue)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
                if (float.TryParse(value?.ToString(), out var result)) return result;
            }
            return defaultValue;
        }

        private int GetIntValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is int i) return i;
                if (value is long l) return (int)l;
                if (int.TryParse(value?.ToString(), out var result)) return result;
            }
            return 0;
        }

        private bool GetBoolValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is bool b) return b;
                if (bool.TryParse(value?.ToString(), out var result)) return result;
            }
            return false;
        }

        #endregion
    }
}
