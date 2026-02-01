using System.Collections.Generic;
using CastleDB.Editor.Providers;
using CastleDB.Runtime;
using CastleDB.Tests.EditMode.TestHelpers;
using NUnit.Framework;
using UnityEngine;

namespace CastleDB.Tests.EditMode
{
    /// <summary>
    /// 2.2 paramsJson 解析减负：结构化 + 一次性缓存 的核心单测。
    /// 约束：不依赖 Unity Console 的 Error/Warning 日志断言（避免“故意试错”式用例）。
    /// </summary>
    public class ParamsJsonOptimizationTests
    {
        [Test]
        public void CastleDbParamsJson_ParseAnimTriggerAndReleaseDelay_ValidJson_ReadsFields()
        {
            CastleDbParamsJson.ParseAnimTriggerAndReleaseDelay(
                "{\"animTrigger\":\"cast\",\"releaseDelay\":0.25}",
                out string animTrigger,
                out float releaseDelay);

            Assert.AreEqual("cast", animTrigger);
            Assert.AreEqual(0.25f, releaseDelay, 1e-5f);
        }

        [Test]
        public void CastleDbParamsJson_ParseAnimTriggerAndReleaseDelay_InvalidJson_ReturnsDefaults()
        {
            CastleDbParamsJson.ParseAnimTriggerAndReleaseDelay(
                "{not_json}",
                out string animTrigger,
                out float releaseDelay);

            Assert.AreEqual("", animTrigger);
            Assert.AreEqual(0f, releaseDelay, 1e-5f);
        }

        [Test]
        public void CastleDbParamsJson_ParseAnimTriggerAndReleaseDelay_MissingOrInvalidFields_AreSafe()
        {
            // 缺字段：保持默认值
            CastleDbParamsJson.ParseAnimTriggerAndReleaseDelay(
                "{}",
                out string animTrigger1,
                out float releaseDelay1);

            Assert.AreEqual("", animTrigger1);
            Assert.AreEqual(0f, releaseDelay1, 1e-5f);

            // 非法/负数：releaseDelay 会被 clamp 到 >= 0
            CastleDbParamsJson.ParseAnimTriggerAndReleaseDelay(
                "{\"animTrigger\":\" \",\"releaseDelay\":-5}",
                out string animTrigger2,
                out float releaseDelay2);

            Assert.AreEqual("", animTrigger2);
            Assert.AreEqual(0f, releaseDelay2, 1e-5f);
        }

        [Test]
        public void AbilityCatalogEntry_GetCastParams_LegacyParamsJson_ParsedOnceAndCached()
        {
            var entry = new AbilityCatalogEntry
            {
                castParamsVersion = 0,
                paramsJson = "{\"animTrigger\":\"cast\",\"releaseDelay\":0.25}"
            };

            entry.GetCastParams(out string trigger1, out float delay1);
            Assert.AreEqual("cast", trigger1);
            Assert.AreEqual(0.25f, delay1, 1e-5f);

            // 修改 paramsJson 也不应影响已缓存结果（运行时约定：产物不应动态修改）
            entry.paramsJson = "{\"animTrigger\":\"changed\",\"releaseDelay\":10}";
            entry.GetCastParams(out string trigger2, out float delay2);
            Assert.AreEqual("cast", trigger2);
            Assert.AreEqual(0.25f, delay2, 1e-5f);
        }

        [Test]
        public void NpcAbilityEntry_GetCastParams_LegacyParamsJson_ParsedOnceAndCached()
        {
            var entry = new NpcAbilityEntry
            {
                castParamsVersion = 0,
                paramsJson = "{\"animTrigger\":\"cast\",\"releaseDelay\":0.25}"
            };

            entry.GetCastParams(out string trigger1, out float delay1);
            Assert.AreEqual("cast", trigger1);
            Assert.AreEqual(0.25f, delay1, 1e-5f);

            entry.paramsJson = "{\"animTrigger\":\"changed\",\"releaseDelay\":10}";
            entry.GetCastParams(out string trigger2, out float delay2);
            Assert.AreEqual("cast", trigger2);
            Assert.AreEqual(0.25f, delay2, 1e-5f);
        }

        [Test]
        public void AbilityCatalog_ApplyFromCastleDb_WritesStructuredCastParams()
        {
            AbilityCatalog catalog = null;

            try
            {
                catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
                catalog.ApplyFromCastleDb(
                    abilityEntries: new List<AbilityEntry>
                    {
                        new AbilityEntry
                        {
                            id = "A_Test",
                            hookType = (int)AbilityHookType.Attack,
                            priority = 0,
                            enabled = true,
                            kind = (int)AbilityKind.BuiltinDefault,
                            projectileId = "",
                            summonId = "",
                            buffId = "",
                            cooldown = 0f,
                            onHitSequenceId = "",
                            paramsJson = "{\"animTrigger\":\"cast\",\"releaseDelay\":0.25}"
                        }
                    },
                    projectileDefinitions: new List<AbilityProjectileDefinition>(),
                    summonDefinitions: new List<AbilitySummonDefinition>(),
                    onHitSequenceDefinitions: new List<AbilityOnHitSequenceDefinition>(),
                    buffDefinitions: new List<AbilityBuffDefinition>());

                Assert.AreEqual(1, catalog.entries.Count);
                Assert.AreEqual(1, catalog.entries[0].castParamsVersion);
                Assert.AreEqual("cast", catalog.entries[0].animTrigger);
                Assert.AreEqual(0.25f, catalog.entries[0].releaseDelay, 1e-5f);
            }
            finally
            {
                if (catalog != null)
                {
                    Object.DestroyImmediate(catalog);
                }
            }
        }

        [Test]
        public void MonsterDataProvider_NpcAbility_ParsesAndWritesStructuredCastParams()
        {
            var root = new CastleDbRoot
            {
                sheets = new List<SheetData>
                {
                    new SheetData
                    {
                        name = "NpcAbility",
                        lines = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                { "id", "B_Test" },
                                { "npcId", "M_Test" },
                                { "abilityId", "A_Test" },
                                { "enabled", true },
                                { "priority", 1 },
                                { "cooldownOverride", 0f },
                                { "triggerRole", 1 },
                                { "minRange", 0f },
                                { "maxRange", 0f },
                                { "paramsJson", "{\"animTrigger\":\"cast\",\"releaseDelay\":0.25}" }
                            }
                        }
                    }
                }
            };

            var source = new MockCastleDbSource(root);
            var provider = new MonsterDataProvider();

            try
            {
                provider.Initialize(source, CreateDescriptor(providerId: "Monster"));
                var entries = provider.GetAllEntries<NpcAbilityEntry>();

                Assert.AreEqual(1, entries.Count);
                Assert.AreEqual(1, entries[0].castParamsVersion);
                Assert.AreEqual("cast", entries[0].animTrigger);
                Assert.AreEqual(0.25f, entries[0].releaseDelay, 1e-5f);
            }
            finally
            {
                provider.Reset();
            }
        }

        private static CdbModuleDescriptor CreateDescriptor(string providerId)
        {
            var metaEntries = new List<MetaEntry>
            {
                new MetaEntry { key = "providerId", value = providerId },
                new MetaEntry { key = "schemaVersion", value = "0.4" },
                new MetaEntry { key = "displayName", value = "测试模块" },
                new MetaEntry { key = "resourcePath", value = "Data/Test" },
                new MetaEntry { key = "dependencies", value = "" }
            };

            return CdbModuleDescriptor.FromMetaEntries(metaEntries, "Assets/Resources/Data/Test.cdb");
        }
    }
}

