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

    private Vector3 inputDirection;
    private bool jumpPressed;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        animator = GetComponent<Animator>();
        cameraFollow = Camera.main.GetComponent<CameraFollow>();
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
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        animator.SetFloat("Speed", horizontalVelocity.magnitude);
    }

    void FixedUpdate()
    {
        //Debug.Log("Grounded=" + IsGrounded() + ", PosY=" + transform.position.y + ", v=" + Input.GetAxis("Vertical"));
        if (IsGrounded())
        {
            if (IsAttacking())
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
        foreach (Collider hit in hits)
        {
            // 碰到的东西如果有 Health 组件(即敌人)，就扣血
            Health h = hit.GetComponent<Health>();
            if (h != null) h.TakeDamage(attackDamage);
        }
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