using System.Collections.Generic;
using UnityEngine;

// FR2：由有限状态机驱动的 Boss。
//
// 设计前提：项目里没有 Boss 动画资源，所以所有"意图"都用颜色 / 位移 / 缩放表达——
// 即使 Boss 只是个胶囊体，玩家（和录像的观众）也能看清它此刻在干什么：
//     变红发亮 + 前倾 = 蓄力，快躲开
//     猛然前冲          = 出手
//     变暗下沉          = 收招硬直，可以打
//     发蓝瘫软          = 被打出硬直，输出窗口
//
// 状态图：
//     Idle ──玩家进入侦测──▶ Chase ──进入攻击距离──▶ Windup ─▶ Strike ─▶ Recover ─┐
//       ▲                     ▲                                                  │
//       └──玩家离开侦测────────┴──────────────────────────────────────────────────┘
//     任意状态 ──短时间内累计伤害超过躯干值──▶ Stagger（可输出的空档）─▶ Chase
//     血量归零 ─▶ Dead
//
// 血量与死亡仍归通用的 Health 组件管，本脚本不碰血量数值。死亡时 Health.Die()
// 用 gameObject.tag（"Boss"）上报 QuestManager —— 与任务机制层的 targetId 对上。
//
// 表现层的所有写入都集中在 UpdateLook() 一处，不散落在各 Tick 里，
// 否则会和 Health 的受击缩放互相覆盖、每帧闪烁。
[RequireComponent(typeof(Health))]
public class BossController : MonoBehaviour
{
    public enum State { Idle, Chase, Windup, Strike, Recover, Stagger, Dead }

    [Header("身份")]
    public string displayName = "Champion";     // 顶部血条上的名字；BossSpawner 用任务的 targetFlavour 填

    [Header("感知与移动")]
    public float detectRange = 18f;
    public float attackRange = 3.2f;
    public float moveSpeed = 3.4f;
    public float turnSpeed = 8f;

    [Header("攻击节奏（秒）")]
    public float windupTime = 0.75f;        // 蓄力：留给玩家的反应窗口
    public float strikeTime = 0.22f;
    public float recoverTime = 1.35f;       // 收招硬直：玩家的输出窗口

    [Header("伤害")]
    // 玩家 100 血：12 点 ≈ 挨 8 下才死，配合 0.75 秒蓄力预警，够学会躲又不至于站着就赢
    public float attackDamage = 12f;
    public float strikeRadius = 2.6f;
    public float strikeReach = 2.0f;        // 判定球心在身前多远

    [Header("躯干值（连续挨打会被打出硬直）")]
    public float poiseMax = 75f;
    public float poiseRecoverPerSec = 18f;
    public float staggerTime = 1.6f;

    [Header("第二阶段")]
    public float phase2At = 0.5f;           // 血量低于此比例时进入
    public float phase2SpeedMul = 1.35f;
    public float phase2WindupMul = 0.7f;    // 蓄力更短 = 更难躲

    public State Current { get; private set; } = State.Idle;
    public int Phase { get; private set; } = 1;

    Health health;
    Transform player;
    Renderer[] renderers;
    readonly List<Color> baseColors = new List<Color>();
    Vector3 initialScale;

    float stateTimer;
    float poise;
    float lastHealth;
    float hitFlash;         // 受击白闪剩余时间
    float phaseFlash;       // 转阶段金闪剩余时间
    bool aggroed;
    Vector3 lungeDir;

    // ===================== 生命周期 =====================

    void Awake()
    {
        health = GetComponent<Health>();
        initialScale = transform.localScale;
        renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers) baseColors.Add(r.material.color);
    }

    void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;

        lastHealth = health.Current;
        health.OnHealthChanged += OnDamaged;
        health.OnDied += OnBossDied;
    }

    void OnDestroy()
    {
        if (health == null) return;
        health.OnHealthChanged -= OnDamaged;
        health.OnDied -= OnBossDied;
    }

    // ===================== 状态机主循环 =====================

    void Update()
    {
        if (Current == State.Dead || player == null) return;

        stateTimer -= Time.deltaTime;
        if (hitFlash > 0f) hitFlash -= Time.deltaTime;
        if (phaseFlash > 0f) phaseFlash -= Time.deltaTime;

        RecoverPoise();
        CheckPhase();

        switch (Current)
        {
            case State.Idle:    TickIdle();    break;
            case State.Chase:   TickChase();   break;
            case State.Windup:  TickWindup();  break;
            case State.Strike:  TickStrike();  break;
            case State.Recover: TickRecover(); break;
            case State.Stagger: TickStagger(); break;
        }

        UpdateLook();      // 表现层唯一的写入点
    }

    float DistToPlayer => Vector3.Distance(transform.position, player.position);

    void TickIdle()
    {
        if (DistToPlayer > detectRange) return;
        Aggro();
        Enter(State.Chase);
    }

    void TickChase()
    {
        if (DistToPlayer > detectRange * 1.3f) { Enter(State.Idle); return; }

        FacePlayer();
        if (DistToPlayer > attackRange)
        {
            Vector3 dir = Flat(player.position - transform.position).normalized;
            transform.position += dir * CurrentSpeed * Time.deltaTime;
        }
        else Enter(State.Windup);
    }

    void TickWindup()
    {
        FacePlayer(0.35f);                  // 蓄力时转身变慢 → 玩家可以绕后躲
        if (stateTimer <= 0f)
        {
            lungeDir = Flat(player.position - transform.position).normalized;
            Enter(State.Strike);
        }
    }

    void TickStrike()
    {
        transform.position += lungeDir * (CurrentSpeed * 2.4f) * Time.deltaTime;
        if (stateTimer <= 0f) Enter(State.Recover);
    }

    void TickRecover()
    {
        if (stateTimer <= 0f) Enter(DistToPlayer <= detectRange ? State.Chase : State.Idle);
    }

    void TickStagger()
    {
        if (stateTimer <= 0f) { poise = 0f; Enter(State.Chase); }
    }

    // ===================== 状态切换 =====================

    void Enter(State next)
    {
        Current = next;
        switch (next)
        {
            case State.Idle:
            case State.Chase:   stateTimer = 0f;             break;
            case State.Windup:  stateTimer = CurrentWindup;  break;
            case State.Strike:  stateTimer = strikeTime; DoStrike(); break;
            case State.Recover: stateTimer = recoverTime;    break;
            case State.Stagger: stateTimer = staggerTime;    break;
        }
    }

    void OnBossDied(Health _)
    {
        Current = State.Dead;
        CombatHUD.Instance?.HideBoss();
        ResetLook();
        enabled = false;
    }

    // 一次挥击只判定一次；只打玩家，不误伤别的敌人
    void DoStrike()
    {
        Vector3 center = transform.position + transform.forward * strikeReach + Vector3.up * 0.8f;
        foreach (var col in Physics.OverlapSphere(center, strikeRadius))
        {
            if (!col.CompareTag("Player")) continue;
            var h = col.GetComponent<Health>();
            if (h != null) h.TakeDamage(attackDamage);   // 闪避无敌帧由 Health.invulnerable 拦截
            break;
        }
    }

    // ===================== 受击 / 躯干值 / 阶段 =====================

    void OnDamaged(Health h)
    {
        float delta = lastHealth - h.Current;
        lastHealth = h.Current;
        if (delta <= 0f || Current == State.Dead) return;

        hitFlash = 0.09f;
        if (!aggroed) Aggro();              // 被远程偷袭也会激活

        poise += delta;
        // 抢在它出手前打满躯干值就能打断蓄力 —— 这是这套战斗最关键的正反馈
        if (poise >= poiseMax && Current != State.Stagger)
        {
            poise = 0f;
            Enter(State.Stagger);
        }
    }

    void RecoverPoise()
    {
        if (Current == State.Stagger) return;
        poise = Mathf.Max(0f, poise - poiseRecoverPerSec * Time.deltaTime);
    }

    void CheckPhase()
    {
        if (Phase >= 2 || health.Normalized > phase2At) return;
        Phase = 2;
        phaseFlash = 0.8f;
        StartCoroutine(PhaseInvulnerability());
        Debug.Log($"[Boss] {displayName} entered phase 2");
    }

    // 转阶段给一小段无敌，避免被一套连死、看不到二阶段
    System.Collections.IEnumerator PhaseInvulnerability()
    {
        health.invulnerable = true;
        yield return new WaitForSeconds(0.8f);
        health.invulnerable = false;
    }

    float CurrentSpeed  => Phase == 2 ? moveSpeed * phase2SpeedMul : moveSpeed;
    float CurrentWindup => Phase == 2 ? windupTime * phase2WindupMul : windupTime;

    // ===================== 表现层（集中在这一处） =====================

    // 必须防重入：ShowBoss 会往 Health 的事件上挂一个匿名 lambda（退订不掉），
    // 玩家每「跑远再跑回」一次就多挂一个，订阅无限累积、血条残影还会被重置。
    void Aggro()
    {
        if (aggroed) return;
        aggroed = true;
        CombatHUD.Instance?.ShowBoss(health, displayName);
    }

    void FacePlayer(float mul = 1f)
    {
        Vector3 dir = Flat(player.position - transform.position);
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(dir), turnSpeed * mul * Time.deltaTime);
    }

    // 由状态决定颜色与体型；受击闪与转阶段闪优先级更高，覆盖在上面。
    void UpdateLook()
    {
        Color tint = Color.white;
        float emission = 0f, xz = 1f, y = 1f;

        switch (Current)
        {
            case State.Windup:
            {
                float k = 1f - Mathf.Clamp01(stateTimer / Mathf.Max(0.0001f, CurrentWindup));
                tint = Color.Lerp(Color.white, new Color(1f, 0.25f, 0.15f), k);
                emission = k * 0.9f;
                xz = 1f + k * 0.10f;
                y  = 1f - k * 0.08f;
                break;
            }
            case State.Recover:
                tint = new Color(0.7f, 0.7f, 0.75f); xz = 0.97f; y = 0.94f; break;
            case State.Stagger:
                tint = new Color(0.55f, 0.6f, 0.9f); xz = 1.05f; y = 0.82f; break;
        }

        if (phaseFlash > 0f)
        {
            tint = Color.Lerp(Color.white, new Color(1f, 0.85f, 0.2f),
                              Mathf.PingPong(phaseFlash * 6f, 1f));
            emission = 1f;
        }
        else if (hitFlash > 0f)
        {
            tint = Color.white * 1.8f;
            emission = Mathf.Max(emission, 0.7f);
        }

        Tint(tint, emission);
        transform.localScale = new Vector3(initialScale.x * xz, initialScale.y * y, initialScale.z * xz);
    }

    void Tint(Color c, float emission)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            var m = renderers[i].material;
            m.color = baseColors[i] * c;
            if (emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * emission);
            }
            else m.SetColor("_EmissionColor", Color.black);
        }
    }

    void ResetLook()
    {
        Tint(Color.white, 0f);
        transform.localScale = initialScale;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
