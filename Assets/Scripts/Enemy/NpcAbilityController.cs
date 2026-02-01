using System.Collections.Generic;
using CastleDB.Runtime;
using UnityEngine;

public partial class NpcAbilityController : MonoBehaviour
{
    private const string AbilityCatalogResourcePath = "Config/EnemyAbilityCatalog";
    private const float DefaultAbilityReleaseExpirySeconds = 1.5f;
    private const string MoveSpeedMultiplierKey = "moveSpeedMultiplier";
    private const string AttackMultiplierKey = "attackMultiplier";

    [Header("Optional Overrides")]
    [SerializeField] private AbilityCatalog abilityCatalogOverride;
    [SerializeField] private Transform firePointOverride;

    private EnemyAgentBase _agent;
    private Animator _animator;

    private AbilityCatalog _catalog;
    private Dictionary<string, AbilityCatalogEntry> _abilitiesById;
    private readonly Dictionary<string, float> _nextReadyTimeByBindingId = new Dictionary<string, float>();
    private readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();

    [Header("Projectile Pool (2.3)")]
    [SerializeField] private bool useProjectilePool = true;

    [Min(0)]
    [SerializeField] private int projectilePoolMaxSize = 16;

    private readonly Dictionary<string, PrefabGameObjectPool> _projectilePoolsByPrefabPath = new Dictionary<string, PrefabGameObjectPool>();

    [Header("VFX Pool (2.3)")]
    [SerializeField] private bool useVfxPool = true;

    [Min(0)]
    [SerializeField] private int vfxPoolMaxSize = 32;

    private VfxPoolService _vfxPool;

    private Transform _cachedFirePoint;
    private bool _searchedFirePoint;

    private PendingCast _pendingCast;
    private bool _hasPendingCast;
    private bool _loggedMissingCatalog;
    private bool _loggedMissingAgent;

    private EnemyTuningProfile _cachedPassiveProfile;
    private Dictionary<string, NpcPassiveAbilityBindingEntry> _passiveBindingsByBindingId;
    private Dictionary<string, List<NpcPassiveAbilityConditionEntry>> _passiveConditionsByBindingId;

    private readonly Dictionary<string, bool> _lastConditionTrueByBindingId = new Dictionary<string, bool>();
    private readonly Dictionary<string, ActiveBuffState> _activeBuffsByBindingId = new Dictionary<string, ActiveBuffState>();
    private readonly List<string> _tmpBuffKeys = new List<string>();

    private enum PendingCastKind
    {
        Projectile = 0,
        Buff = 1
    }

    private class ActiveBuffState
    {
        public string bindingId;
        public string sourceId;
        public AbilityBuffDefinition def;
        public Transform targetRoot;
        public StatModifierLayer stats;
        public float expiresAtTime;
        public GameObject loopVfx;
    }

    private struct PendingCast
    {
        public PendingCastKind kind;
        public string bindingId;
        public string abilityId;

        public AbilityProjectileDefinition projectile;
        public IReadOnlyList<AbilityOnHitNode> onHitNodes;

        public AbilityBuffDefinition buff;
        public Transform buffTargetRoot;
        public string buffSourceId;

        public float fallbackReleaseAtTime;
        public float expiresAtTime;
        public float directionSign;
    }

    private void Awake()
    {
        _agent = GetComponent<EnemyAgentBase>();
        _animator = GetComponent<Animator>();
    }

    private void Update()
    {
        TickActiveBuffs(Time.time);
    }

    private void OnDisable()
    {
        _hasPendingCast = false;
        _pendingCast = default;
        ClearAllActiveBuffs(spawnExpireVfx: false);
    }
}
