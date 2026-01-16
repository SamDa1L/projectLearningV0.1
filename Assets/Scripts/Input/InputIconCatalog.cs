using UnityEngine;

public enum InputIconDevice
{
    Keyboard = 0,
    PlayStation = 1,
    Xbox = 2,
    Switch = 3
}

[CreateAssetMenu(fileName = "InputIconCatalog", menuName = "Config/InputIconCatalog", order = 0)]
public sealed class InputIconCatalog : ScriptableObject
{
    [SerializeField] private Sprite[] keyboard = new Sprite[4];
    [SerializeField] private Sprite[] playStation = new Sprite[4];
    [SerializeField] private Sprite[] xbox = new Sprite[4];
    [SerializeField] private Sprite[] switchIcons = new Sprite[4];

    public Sprite GetSprite(InputIconDevice device, int slotIndex)
    {
        Sprite[] sprites = GetSprites(device);
        if (sprites == null || slotIndex < 0 || slotIndex >= sprites.Length)
        {
            return null;
        }

        return sprites[slotIndex];
    }

    public Sprite[] GetSprites(InputIconDevice device)
    {
        switch (device)
        {
            case InputIconDevice.Keyboard:
                return keyboard;
            case InputIconDevice.PlayStation:
                return playStation;
            case InputIconDevice.Switch:
                return switchIcons;
            default:
                return xbox;
        }
    }
}
