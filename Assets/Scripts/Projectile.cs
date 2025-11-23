using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Projectile : MonoBehaviour
{
    public Vector2 moveSpeed = new Vector2(15f, 0);
    public int damage = 10;
    public Vector2 knockback = new Vector2 (0, 0);


    Rigidbody2D rb;



    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }


    // Start is called before the first frame update
    void Start()
    {
        rb.velocity = new Vector2(moveSpeed.x * transform.localScale.x, moveSpeed.y);
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    private void OnTriggerEnter2D(Collider2D collision)
    {
        Damageable damageable = collision.GetComponent<Damageable>();


        if(damageable != null)
        {
            Vector2 deliveredKnockBack = transform.localScale.x > 0 ? knockback : new Vector2(-knockback.x, knockback.y);

            //击中目标
            bool gotHit = damageable.Hit(damage, deliveredKnockBack);

            if (gotHit)
            {
                Debug.Log(collision.name + "击中" + damage);
                Destroy(gameObject);
            }
        }


    }




}
