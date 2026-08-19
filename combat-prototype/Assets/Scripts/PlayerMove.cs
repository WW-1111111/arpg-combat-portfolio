using System.Collections;
using UnityEngine;

public class PlayerMove : MonoBehaviour
{
    private Rigidbody rb;
    private CapsuleCollider capsule;
    private Animator animator;
    private CameraFollow cameraFollow;   // 主相机上的锁定脚本(读当前锁定的敌人)

    public float speed = 5f;
    public float turnSpeed = 360f;        // 转身速度(度/秒)，越小转得越慢越自然
    public float jumpForce = 10f;
    public float airControl = 5f;
    public float fallMultiplier = 15f;
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.6f;

    [Header("攻击判定")]
    public float attackRange = 1.2f;                          // 命中检测半径
    public float attackDamage = 25f;                          // 每次攻击伤害
    public Vector3 attackOffset = new Vector3(0f, 1f, 1.5f);  // 判定球相对角色的位置(前方/上方)
    public AudioClip[] hitSounds;              // 命中音效(可放多个，命中时随机播一个，避免单调)
    public float hitstopDuration = 0.08f;      // 顿帧持续(真实秒)
    public float hitstopScale = 0.05f;         // 顿帧时的时间流速(接近0=几乎冻结)

    [Header("闪避")]
    public KeyCode dodgeKey = KeyCode.LeftShift;   // 闪避键
    public float dodgeSpeed = 10f;                 // 闪避冲刺速度
    public float dodgeDuration = 0.5f;             // 闪避动作总时长(秒)
    public float invulnerableTime = 0.4f;          // 无敌帧时长(应略短于dodgeDuration)

    private Vector3 inputDirection;
    private bool jumpPressed;
    private Health health;           // 自己的血量(闪避无敌帧用)
    private bool isDodging = false;
    private Vector3 dodgeDir;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        animator = GetComponent<Animator>();
        cameraFollow = Camera.main.GetComponent<CameraFollow>();
        health = GetComponent<Health>();
    }

    void Update()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 camForward = Camera.main.transform.forward;
        Vector3 camRight = Camera.main.transform.right;
        camForward.y = 0; camRight.y = 0;
        camForward.Normalize(); camRight.Normalize();
        inputDirection = camForward * v + camRight * h;

        // 让角色转向移动方向（动作RPG标准：朝哪跑就面朝哪，避免侧/后移时仍是“向前跑”）
        Transform lockTarget = cameraFollow != null ? cameraFollow.CurrentTarget : null;
        if (lockTarget != null)
        {
            // 锁定时：始终面向敌人
            Vector3 toEnemy = lockTarget.position - transform.position;
            toEnemy.y = 0;
            if (toEnemy.sqrMagnitude > 0.01f)
            {
                Quaternion tr = Quaternion.LookRotation(toEnemy);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, tr, turnSpeed * Time.deltaTime);
            }
        }
        else if (inputDirection.sqrMagnitude > 0.01f && !IsAttacking())
        {
            // 未锁定：面向移动方向
            Quaternion targetRot = Quaternion.LookRotation(inputDirection);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            jumpPressed = true;                 // 通知 FixedUpdate 施加跳跃物理力
            animator.SetTrigger("Jumping");     // 点燃 Animator 里的 Jumping 触发器 → 播放跳跃动画
        }

        // 鼠标左键 = 攻击（攻击中不可打断：必须等当前攻击播完才能出下一击）
        if (Input.GetMouseButtonDown(0))
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (!stateInfo.IsName("Attack"))    // 只有"当前不在Attack状态"时才允许出招
            {
                animator.SetTrigger("Attack");
            }
        }

        // 新增：设置 Animator Speed
        // 闪避键 = 翻滚（攻击中/闪避中不可再触发）
        if (Input.GetKeyDown(dodgeKey) && !isDodging && !IsAttacking())
        {
            StartCoroutine(Dodge());
        }

        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        animator.SetFloat("Speed", horizontalVelocity.magnitude);
    }

    void FixedUpdate()
    {
        //Debug.Log("Grounded=" + IsGrounded() + ", PosY=" + transform.position.y + ", v=" + Input.GetAxis("Vertical"));
        if (IsGrounded())
        {
            if (isDodging)
            {
                // 闪避中：朝闪避方向冲刺
                rb.linearVelocity = new Vector3(dodgeDir.x * dodgeSpeed, rb.linearVelocity.y, dodgeDir.z * dodgeSpeed);
            }
            else if (IsAttacking())
            {
                // 攻击中：锁住水平移动（出招承诺感）；y轴保留给重力/跳跃
                rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            }
            else
            {
                Vector3 movement = inputDirection * speed;
                rb.linearVelocity = new Vector3(movement.x, rb.linearVelocity.y, movement.z);
            }
        }
        else
        {
            Vector3 airForce = inputDirection * airControl;
            rb.AddForce(airForce, ForceMode.Acceleration);
        }

        if (IsGrounded() && jumpPressed)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
        }

        if (rb.linearVelocity.y < 0)
        {
            rb.AddForce(Vector3.down * fallMultiplier, ForceMode.Acceleration);
        }

        jumpPressed = false;
    }

    bool IsAttacking()
    {
        // 当前在Attack状态 = 正在攻击
        return animator.GetCurrentAnimatorStateInfo(0).IsName("Attack");
    }

    // 由攻击动画的 Animation Event 在"挥砍命中帧"调用
    public void OnAttackHit()
    {
        // 在角色前方做一个球形范围检测，找出范围内所有碰撞体
        Vector3 center = transform.TransformPoint(attackOffset);
        Collider[] hits = Physics.OverlapSphere(center, attackRange);
        bool hitSomething = false;
        foreach (Collider hit in hits)
        {
            Health h = hit.GetComponent<Health>();
            // 必须跳过自己：判定球心在身前 1.5、半径 1.2，近端只到身前 0.3，
            // 正好罩住角色自身的胶囊体 —— 不排除的话每挥一刀就自伤 attackDamage 点。
            if (h == null || h.gameObject == gameObject) continue;
            h.TakeDamage(attackDamage);
            hitSomething = true;
        }

        if (hitSomething)
        {
            if (hitSounds != null && hitSounds.Length > 0)
            {
                AudioClip clip = hitSounds[Random.Range(0, hitSounds.Length)];   // 从数组里随机选一个
                AudioSource.PlayClipAtPoint(clip, center);                       // 独立播放,不怕敌人被销毁
            }
            StartCoroutine(HitStop());                                           // 顿帧
        }
    }

    // 顿帧：命中瞬间把时间几乎冻结一小会，制造"咔"的打击感(只狼那种)
    IEnumerator HitStop()
    {
        Time.timeScale = hitstopScale;
        yield return new WaitForSecondsRealtime(hitstopDuration);  // 真实时间,不受timeScale影响
        Time.timeScale = 1f;
    }

    // 闪避(翻滚)：朝输入方向冲刺，前段有无敌帧
    IEnumerator Dodge()
    {
        isDodging = true;
        // 有方向输入就朝输入方向翻，否则向后翻
        dodgeDir = inputDirection.sqrMagnitude > 0.01f ? inputDirection.normalized : -transform.forward;
        if (dodgeDir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dodgeDir);
        animator.SetTrigger("roll");

        if (health != null) health.invulnerable = true;          // 开无敌帧
        yield return new WaitForSeconds(invulnerableTime);
        if (health != null) health.invulnerable = false;         // 关无敌帧

        yield return new WaitForSeconds(Mathf.Max(0f, dodgeDuration - invulnerableTime));  // 动作收尾
        isDodging = false;
    }

    // 在Scene视图里画出攻击判定球(选中角色时显示)，方便调位置和大小
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.TransformPoint(attackOffset), attackRange);
    }

    bool IsGrounded()
    {
        // 计算 Capsule 底部的世界坐标
        Vector3 capsuleBottom = transform.position + capsule.center - new Vector3(0, capsule.height / 2, 0);
        // 起点向上抬一点点（0.05），避免起点正好在 Plane 表面（边界 case 物理不稳定）
        Vector3 rayOrigin = capsuleBottom + Vector3.up * 0.05f;
        return Physics.Raycast(rayOrigin, Vector3.down, groundCheckDistance, groundLayer);
    }
}