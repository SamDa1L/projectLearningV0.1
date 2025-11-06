using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Damageable : MonoBehaviour
{
    Animator animator;

    [SerializeField]
    private int _maxHealth = 100;

    public int MaxHealth 
    {
        get
        {
            return _maxHealth;
        }
        set 
        { 
            _maxHealth = value;
        }
    }

    [SerializeField]
    private int _health = 100;

    public int Health 
    {
        get 
        {
            return _health;
        }
        set
        {
            _health = value;

            //如果血量降到0以下，则角色死亡
            if(_health <= 0)
            {
                IsAlive = false;
            }
        }
    }

    [SerializeField]
    private bool _isAlive = true;

    [SerializeField]
    private bool isInvincible = false;
    private float timeSinceHit = 0;
    public float invincibilityTime = 0.25f;

    public bool IsAlive 
    {
        get
        {
            return _isAlive;
        }
        set
        {
            _isAlive = value;
            animator.SetBool(AnimationStrings.isAlive, value);
            Debug.Log("存活" +  value);
        }
    }


    private void Awake()
    {
        animator = GetComponent<Animator>();
    }




    public void Hit(int damage)
    {
        if(IsAlive && !isInvincible)
        {
            Health -= damage;
            isInvincible = true;
        }
    }


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (isInvincible)
        {
            if(timeSinceHit > invincibilityTime)
            {
                //取消无敌
                isInvincible = false ;
                timeSinceHit = 0;
            }
            timeSinceHit += Time.deltaTime;

        }

        Hit(10);
    }
}
