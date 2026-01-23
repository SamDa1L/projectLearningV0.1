using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Projectile : MonoBehaviour
{
    public Vector2 moveSpeed = new Vector2(15f, 0);
    public int damage = 10;
    public Vector2 knockback = new Vector2(0, 0);

    private Rigidbody2D rb;
    private IGameObjectRecycler _recycler;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    // 兼容：如果生成方未显式调用 ResetForSpawn()，首次生成时也能正确设置速度。
    private void Start()
    {
        ResetForSpawn();
    }

    public void SetRecycler(IGameObjectRecycler recycler)
    {
        _recycler = recycler;
    }

    public void ResetForSpawn()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }

        if (rb != null)
        {
            rb.velocity = new Vector2(moveSpeed.x * transform.localScale.x, moveSpeed.y);
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        Damageable damageable = collision.GetComponent<Damageable>();

        if (damageable != null)
        {
            Vector2 deliveredKnockBack = transform.localScale.x > 0
                ? knockback
                : new Vector2(-knockback.x, knockback.y);

            bool gotHit = damageable.Hit(damage, deliveredKnockBack);

            if (gotHit)
            {
                Despawn();
            }
        }
    }

    private void Despawn()
    {
        if (_recycler != null)
        {
            _recycler.Recycle(gameObject);
            return;
        }

        Destroy(gameObject);
    }
}
