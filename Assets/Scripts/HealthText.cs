using TMPro;
using UnityEngine;

public interface IHealthTextRecycler
{
    void Recycle(HealthText text);
}

public class HealthText : MonoBehaviour
{
    public Vector3 moveSpeed = new Vector3(0, 75 ,0);
    public float timeToFade = 1f;

    RectTransform textTransform;
    TextMeshProUGUI textMeshPro;

    private float timeElapsed = 0f;
    private Color startColor;
    private IHealthTextRecycler _recycler;



    private void Awake()
    {
        textTransform = GetComponent<RectTransform>();
        textMeshPro = GetComponent<TextMeshProUGUI>();
    }

    private void OnEnable()
    {
        // Safe default for legacy spawners that don't call ResetForSpawn().
        ResetForSpawn();
    }


    public void SetRecycler(IHealthTextRecycler recycler)
    {
        _recycler = recycler;
    }

    public void ResetForSpawn()
    {
        timeElapsed = 0f;
        if (textMeshPro != null)
        {
            startColor = textMeshPro.color;
        }
    }

    private void Update()
    {
        if (textTransform != null)
        {
            textTransform.position += moveSpeed * Time.deltaTime;
        }

        timeElapsed += Time.deltaTime;

        if (timeToFade <= 0f)
        {
            Expire();
            return;
        }

        if (timeElapsed < timeToFade)
        {
            if (textMeshPro != null)
            {
                float t = Mathf.Clamp01(timeElapsed / timeToFade);
                float fadeAlpha = startColor.a * (1f - t);
                Color c = textMeshPro.color;
                textMeshPro.color = new Color(c.r, c.g, c.b, fadeAlpha);
            }
        }
        else
        {
            Expire();
        }
    }

    private void Expire()
    {
        if (_recycler != null)
        {
            _recycler.Recycle(this);
            return;
        }

        Destroy(gameObject);
    }
}
