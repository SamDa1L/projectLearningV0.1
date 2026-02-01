using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class FloatingTextPoolingPerformanceTests
{
    private static FloatingTextStyleCatalog CreateCatalog(float timeToFade, Vector3 moveSpeed)
    {
        var catalog = ScriptableObject.CreateInstance<FloatingTextStyleCatalog>();

        // Override the private defaultStyle for a fast/low-noise test run.
        var field = typeof(FloatingTextStyleCatalog).GetField("defaultStyle", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field, "FloatingTextStyleCatalog.defaultStyle field not found (reflection).");

        var entry = (FloatingTextStyleCatalog.Entry)field.GetValue(catalog);
        entry.motionStyle.timeToFade = timeToFade;
        entry.motionStyle.moveSpeed = moveSpeed;
        entry.motionStyle.localOffset = Vector2.zero;
        entry.motionStyle.randomLocalOffset = Vector2.zero;

        field.SetValue(catalog, entry);
        return catalog;
    }

    private static UIManager CreateUiManagerRoot(bool usePool, int poolMax, float timeToFade, out GameObject root)
    {
        root = new GameObject(usePool ? "UIManagerRoot_PoolOn" : "UIManagerRoot_PoolOff");

        var uiManager = root.AddComponent<UIManager>();
        uiManager.useFloatingTextPool = usePool;
        uiManager.floatingTextPoolMaxSize = poolMax;

        // World camera for WorldToScreenPoint.
        var camGo = new GameObject("WorldCamera");
        camGo.transform.SetParent(root.transform);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        var cam = camGo.AddComponent<Camera>();
        uiManager.worldCamera = cam;

        // Canvas for UI placement.
        var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(root.transform);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        uiManager.gameCanvas = canvas;
        uiManager.floatingTextRoot = canvasGo.transform;

        // Floating text "prefab" (scene object used as a template for Instantiate).
        var prefab = new GameObject("FloatingTextPrefab", typeof(RectTransform));
        var tmp = prefab.AddComponent<TextMeshProUGUI>();
        tmp.raycastTarget = false;
        tmp.text = "0";
        tmp.color = Color.white;

        var healthText = prefab.AddComponent<HealthText>();
        healthText.moveSpeed = Vector3.zero;
        healthText.timeToFade = 9999f; // Prevent the template from self-destroying during the test.

        uiManager.floatingTextPrefab = prefab;
        uiManager.floatingTextStyleCatalog = CreateCatalog(timeToFade, Vector3.zero);

        return uiManager;
    }

    [UnityTest]
    public IEnumerator FloatingTextPool_ReducesInstantiateCount()
    {
        const int waveSize = 20;
        const int waveCount = 5;
        const float timeToFade = 0.05f;
        float waitTime = timeToFade + 0.05f;

        int noPoolInstantiates;
        {
            UIManager uiManager = CreateUiManagerRoot(usePool: false, poolMax: 0, timeToFade: timeToFade, out GameObject root);

            for (int w = 0; w < waveCount; w++)
            {
                for (int i = 0; i < waveSize; i++)
                {
                    uiManager.CharacterTookDamage(null, 1, Vector2.zero);
                }

                yield return new WaitForSeconds(waitTime);
                yield return null;
            }

            noPoolInstantiates = uiManager.FloatingTextInstantiateCount;
            Debug.Log($"[FloatingTextPoolBench] pool=OFF instantiates={noPoolInstantiates} reuses={uiManager.FloatingTextReuseCount}");

            Object.Destroy(root);
            yield return null;
        }

        int poolInstantiates;
        {
            UIManager uiManager = CreateUiManagerRoot(usePool: true, poolMax: waveSize, timeToFade: timeToFade, out GameObject root);

            for (int w = 0; w < waveCount; w++)
            {
                for (int i = 0; i < waveSize; i++)
                {
                    uiManager.CharacterTookDamage(null, 1, Vector2.zero);
                }

                yield return new WaitForSeconds(waitTime);
                yield return null;
            }

            poolInstantiates = uiManager.FloatingTextInstantiateCount;
            Debug.Log($"[FloatingTextPoolBench] pool=ON instantiates={poolInstantiates} reuses={uiManager.FloatingTextReuseCount}");

            Object.Destroy(root);
            yield return null;
        }

        Assert.AreEqual(waveSize * waveCount, noPoolInstantiates, "Expected 1 Instantiate per spawn when pooling is off.");
        Assert.Less(poolInstantiates, noPoolInstantiates, "Pooling should reduce Instantiate count.");
        Assert.LessOrEqual(poolInstantiates, waveSize, "With enough pool capacity and time between waves, only the first wave should Instantiate.");
    }
}

