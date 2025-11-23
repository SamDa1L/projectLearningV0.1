using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Attack : MonoBehaviour
{
    //Collider2D attackCollider;
    public int attackDamage = 10;
    public Vector2 knockback = Vector2.zero;

    //private void Awake()
    //{
    //    attackCollider = GetComponent<Collider2D>();
    //}



    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        //检查是否能被击中
        Damageable damageable = collision.GetComponent<Damageable>();

        if (damageable != null) 
        {
            Vector2 deliveredKnockBack = transform.parent.localScale.x > 0 ? knockback : new Vector2(-knockback.x, knockback.y);

            //击中目标
            bool gotHit = damageable.Hit(attackDamage, deliveredKnockBack);

            if (gotHit) 
            {
                Debug.Log(collision.name + "击中" + attackDamage);
            }
            
        }

    }
}
