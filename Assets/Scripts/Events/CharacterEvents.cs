using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;


public class CharacterEvents
{
    /// <summary>
    /// 0.45 修正：添加 Vector2 worldPos 参数（契约 P0-2）
    /// 角色受到伤害事件
    /// 参数：Damageable target, int amount, Vector2 worldPos
    /// </summary>
    public static UnityAction<Damageable, int, Vector2> characterDamaged;

    /// <summary>
    /// 0.45 修正：添加 Vector2 worldPos 参数（契约 P0-2）
    /// 角色受到治疗事件
    /// 参数：Damageable target, int amount, Vector2 worldPos
    /// </summary>
    public static UnityAction<Damageable, int, Vector2> characterHealed;
}
