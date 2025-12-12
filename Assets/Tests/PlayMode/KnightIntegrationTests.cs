using CastleDB.Runtime;
using NUnit.Framework;
using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Knight integration: CastleDB → Profile → EnemyAgentBase → Damageable.
/// Runs in Editor/PlayMode to satisfy the phase 2A minimal test chain.
/// </summary>
public class KnightIntegrationTests
{
    private GameObject knightGameObject;
    private Knight knight;
    private Damageable damageable;

    [OneTimeSetUp]
    public void Setup()
    {
        var knightPrefab = Resources.Load<GameObject>("Prefabs/Enemy/KnightEnemy/KnightEnemy");
        Assert.IsNotNull(knightPrefab, "Knight Prefab not found");

        knightGameObject = Object.Instantiate(knightPrefab);
        Assert.IsNotNull(knightGameObject, "Knight Prefab instantiation failed");

        knight = knightGameObject.GetComponent<Knight>();
        Assert.IsNotNull(knight, "Knight component missing");

        damageable = knightGameObject.GetComponent<Damageable>();
        Assert.IsNotNull(damageable, "Damageable component missing");
    }

    [OneTimeTearDown]
    public void Teardown()
    {
        Object.DestroyImmediate(knightGameObject);
    }

    [UnityTest]
    public IEnumerator InitializesAndIsAlive()
    {
        yield return null;

        Assert.IsNotNull(knight, "Knight not initialized");
        Assert.IsTrue(knight.IsAlive(), "Knight should be alive");
    }

    [UnityTest]
    public IEnumerator ProfileAppliedToDamageable()
    {
        yield return null;

        Assert.Greater(damageable.MaxHealth, 0, "MaxHealth should be > 0");
        Assert.Greater(damageable.invincibilityTime, 0, "invincibilityTime should be > 0");
    }

    [UnityTest]
    public IEnumerator HitReducesHealth()
    {
        yield return null;

        int initialHealth = damageable.MaxHealth;
        Assert.Greater(initialHealth, 0, "Initial health should be > 0");

        const int damage = 10;
        bool hitSuccess = damageable.Hit(damage, Vector2.right);

        Assert.IsTrue(hitSuccess, "Hit should succeed");
        Assert.AreEqual(initialHealth - damage, damageable.Health, "Health should decrease after hit");
    }

    [UnityTest]
    public IEnumerator KnockbackMultiplierPresent()
    {
        yield return null;

        float knockbackMultiplier = damageable.knockbackMultiplier;
        Assert.Greater(knockbackMultiplier, 0, "knockbackMultiplier should be > 0");
    }

    [UnityTest]
    public IEnumerator CastleDbEntryMatchesProfile()
    {
        yield return null;

        var asset = Resources.Load<TextAsset>("Data/CastleDbDemo/MonsterSystem");
        Assert.IsNotNull(asset, "CastleDB asset not found");

        var source = new CastleDbJsonSource(asset);
        var root = source.ReadCastleDbJson();
        Assert.IsNotNull(root, "CastleDB parse failed");

        NpcEntry knightEntry = null;
        foreach (var sheet in root.sheets)
        {
            if (sheet.name != "NPC")
                continue;

            foreach (var line in sheet.lines)
            {
                var entry = JsonUtility.FromJson<NpcEntry>(JsonUtility.ToJson(line));
                if (entry != null && entry.id == "M_Knight")
                {
                    knightEntry = entry;
                    break;
                }
            }
        }

        Assert.IsNotNull(knightEntry, "Knight entry missing in CastleDB");
        Assert.Greater(knightEntry.maxHealth, 0, "Knight maxHealth should be > 0");
        Assert.Greater(knightEntry.moveSpeed, 0, "Knight moveSpeed should be > 0");

        Assert.AreEqual((int)knightEntry.maxHealth, damageable.MaxHealth, "MaxHealth should match CastleDB");
    }

    [UnityTest]
    public IEnumerator DeathConditionHandled()
    {
        yield return null;

        int maxHealth = damageable.MaxHealth;
        damageable.Hit(maxHealth + 10, Vector2.zero);

        yield return null;

        Assert.IsFalse(damageable.IsAlive, "Knight should die after lethal damage");
        Assert.IsFalse(knight.IsAlive(), "Knight.IsAlive should return false");
    }
}
