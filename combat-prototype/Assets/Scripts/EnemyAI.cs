using UnityEngine;

// 挂在敌人身上：发现玩家 → 追击 → 近身按冷却攻击扣血
public class EnemyAI : MonoBehaviour
{
    public float detectRange = 10f;     // 发现玩家的距离(超出就不动)
    public float attackRange = 2f;      // 进入这个距离就停下攻击
    public float moveSpeed = 3f;        // 追击速度
    public float attackDamage = 10f;    // 每次攻击对玩家的伤害
    public float attackCooldown = 1.5f; // 攻击间隔(秒)

    private Transform player;
    private Health playerHealth;
    private float lastAttackTime;

    void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");   // 靠Tag找玩家
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<Health>();
        }
    }

    void Update()
    {
        if (player == null) return;

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist > detectRange) return;   // 玩家太远，待机

        // 面向玩家(只转水平方向)
        Vector3 dir = player.position - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir);

        if (dist > attackRange)
        {
            // 还没到近身距离 → 朝玩家移动(追击)
            transform.position += dir.normalized * moveSpeed * Time.deltaTime;
        }
        else
        {
            // 已近身 → 每隔 attackCooldown 秒攻击一次
            if (Time.time - lastAttackTime >= attackCooldown)
            {
                lastAttackTime = Time.time;
                Debug.Log(gameObject.name + " 攻击玩家！");
                if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
            }
        }
    }
}
