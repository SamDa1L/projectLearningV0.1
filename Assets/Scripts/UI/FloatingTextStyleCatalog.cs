using System;
using TMPro;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/UI/Floating Text Style Catalog", fileName = "FloatingTextStyleCatalog")]
public sealed class FloatingTextStyleCatalog : ScriptableObject
{
    [Serializable]
    public struct TextStyle
    {
        public TMP_FontAsset fontAsset;
        public Material fontMaterialPreset;
        public FontStyles fontStyle;
        public Color color;

        public float fontSize;
        public bool enableAutoSize;
        public float fontSizeMin;
        public float fontSizeMax;

        public string prefix;
        public string suffix;
    }

    [Serializable]
    public struct MotionStyle
    {
        public Vector3 moveSpeed;
        public float timeToFade;
        public Vector2 localOffset;
        public Vector2 randomLocalOffset;
    }

    [Serializable]
    public struct Entry
    {
        public FloatingTextKind kind;
        public TextStyle textStyle;
        public MotionStyle motionStyle;
    }

    [Header("Fallback")]
    [SerializeField] private Entry defaultStyle = new Entry
    {
        kind = FloatingTextKind.Damage,
        textStyle = new TextStyle
        {
            fontStyle = FontStyles.Normal,
            color = Color.white,
            fontSize = 24f,
            enableAutoSize = false,
            fontSizeMin = 18f,
            fontSizeMax = 72f,
            prefix = "",
            suffix = "",
        },
        motionStyle = new MotionStyle
        {
            moveSpeed = new Vector3(0, 75f, 0),
            timeToFade = 1f,
            randomLocalOffset = Vector2.zero,
        }
    };

    [Header("Per Kind")]
    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    public bool TryGetStyle(FloatingTextKind kind, out Entry style)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].kind == kind)
            {
                style = entries[i];
                return true;
            }
        }

        style = defaultStyle;
        return false;
    }

    public Entry GetStyleOrDefault(FloatingTextKind kind)
    {
        TryGetStyle(kind, out var style);
        return style;
    }
}
