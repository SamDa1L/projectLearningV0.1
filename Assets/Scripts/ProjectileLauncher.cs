using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProjectileLauncher : MonoBehaviour
{
    public GameObject projectilePrefab;
    public Transform launchPoint;

    /// <summary>
    /// Projectile 的 Resources 路径（阶段 3A）
    /// 用于从 PlayerConfig 查找伤害覆盖配置
    /// 格式: "Prefabs/Projectiles/Player/Arrow"（不含.prefab后缀）
    /// </summary>
    [Tooltip("Projectile的Resources路径（例如: Prefabs/Projectiles/Player/Arrow）")]
    public string projectileResourcesPath = "";

    private PlayerController playerController;





    // Start is called before the first frame update
    void Start()
    {
        // 尝试获取父物体的 PlayerController（阶段 3A）
        playerController = GetComponentInParent<PlayerController>();
        if (playerController == null)
        {
            Debug.LogWarning($"[ProjectileLauncher] PlayerController not found in parent, projectile damage override will not work");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    public void FirePorjectile()
    {
        GameObject projectile = Instantiate(projectilePrefab, transform.position, projectilePrefab.transform.rotation);
        Vector3 origScale = projectile.transform.localScale;

        projectile.transform.localScale = new Vector3(
            origScale.x * transform.localScale.x > 0 ? 1 : -1,
            origScale.y,
            origScale.z
            );

        // 阶段 3A: Projectile 使用 prefab 级一次性赋值（在 CastleDB Import 时已设置）
        // 不再需要运行时动态赋值，prefab 的 damage 字段已包含正确值
    }





}
