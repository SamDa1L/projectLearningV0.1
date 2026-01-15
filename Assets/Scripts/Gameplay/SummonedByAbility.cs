using UnityEngine;

/// <summary>
/// Marker component for objects spawned by SummonAbility (0.5 Phase 6).
/// Used for debugging/tests and for safe cleanup when the ability is disabled.
/// </summary>
public class SummonedByAbility : MonoBehaviour
{
    [Tooltip("AbilityId that spawned this instance.")]
    public string abilityId = "";
}

