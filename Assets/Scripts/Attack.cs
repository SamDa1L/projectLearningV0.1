using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Attack : MonoBehaviour
{
    //Collider2D attackCollider;

    /// <summary>
    /// 攻击的稳定标识符（用于 CastleDB PlayerAttackOverride 匹配）
    /// 默认值为 GameObject.name，后续可独立修改而不影响层级/动画绑定
    /// </summary>
    [Tooltip("攻击的稳定标识符，用于 CastleDB 配置匹配。建议使用 ASCII/数字/下划线，避免空格/中文/符号")]
    public string attackId = "";

    public int attackDamage = 10;
    public Vector2 knockback = Vector2.zero;

    //private void Awake()
    //{
    //    attackCollider = GetComponent<Collider2D>();
    //}
    void OnTriggerEnter2D(Collider2D collision)
    {
        // 检查碰撞目标是否可被伤害
        Damageable damageable = collision.GetComponent<Damageable>();

        if (damageable != null) 
        {
            // 阵营判定：仅对敌对阵营生效，避免友伤（Enemy <-> Friend 才敌对，Neutral 不敌对）。
            FactionId attackerFaction = FactionUtility.GetFaction(gameObject);
            FactionId targetFaction = FactionUtility.GetFaction(collision.gameObject);
            if (!FactionUtility.IsHostile(attackerFaction, targetFaction))
            {
                return;
            }

            Vector2 deliveredKnockBack = transform.parent.localScale.x > 0 ? knockback : new Vector2(-knockback.x, knockback.y);

            if (attackDamage <= 0)
            {
                return;
            }

            float attackMultiplier = 1f;
            StatModifierLayer stats = GetComponentInParent<StatModifierLayer>();
            if (stats != null)
            {
                attackMultiplier = Mathf.Max(0f, stats.AttackMultiplier);
            }

            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(attackDamage * attackMultiplier));

            bool gotHit = damageable.Hit(finalDamage, deliveredKnockBack);

            if (gotHit)
            {
                Debug.Log($"{collision.name} took {finalDamage} damage");
            }
            
        }

    }
}
