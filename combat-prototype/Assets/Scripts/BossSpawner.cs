using UnityEngine;

// Boss 类任务开始时，在玩家前方生成一个 Boss（FR2 / FR3 的 Boss 分支）。
//
// 名字取自 Quest.targetFlavour —— 那是 ScenarioGenerator 从固定实体表里种子化选出的，
// 不是 LLM 生成的。所以顶部血条上的「沉泉将军」属于机制层，
// 叙事层再怎么换，这个名字和这场战斗都不会变。
public class BossSpawner : MonoBehaviour
{
    public float spawnDistance = 16f;
    public bool clearEnemiesOnBossSpawn = true;   // Boss 战清场，避免杂兵在旁边干扰
    public float bossMaxHealth = 280f;      // 玩家一刀 25 → 约 11 刀，一场 25-30 秒，够打出两个阶段又不拖
    public Color bossColor = new Color(0.32f, 0.10f, 0.14f);

    bool spawned;

    // 由 Bootstrap 负责创建

    void Start()
    {
        var qm = QuestManager.Instance;
        if (qm == null) { enabled = false; return; }     // 批量评估时 QuestManager 被禁用
        qm.OnQuestUpdated += OnQuest;
        if (qm.currentQuest != null) OnQuest(qm.currentQuest);
    }

    void OnDestroy()
    {
        if (QuestManager.Instance != null) QuestManager.Instance.OnQuestUpdated -= OnQuest;
    }

    void OnQuest(Quest q)
    {
        // 空 Quest 的判别见 PickupSpawner.IsRealQuest —— Unity 不会把 [Serializable] 字段反序列化成 null
        if (spawned || !PickupSpawner.IsRealQuest(q) || q.type != QuestType.Boss) return;
        spawned = true;

        var player = GameObject.FindGameObjectWithTag("Player");
        Vector3 origin = player != null ? player.transform.position : Vector3.zero;
        Vector3 fwd = player != null ? Flat(player.transform.forward) : Vector3.forward;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;

        if (clearEnemiesOnBossSpawn) ClearRegularEnemies();

        Vector3 pos = origin + fwd.normalized * spawnDistance;
        pos.y = GroundY(pos);

        Create(pos, q.targetFlavour, bossMaxHealth, bossColor);
        Debug.Log($"[Boss] Spawned \"{q.targetFlavour}\" (tag=Boss, mechanic targetId={q.targetId})");
    }

    // Boss 战清场：把场景里原有的普通敌人移走，避免它们在旁边打冷枪。
    // 用 Destroy 而不是 Health.TakeDamage —— 后者会走 Health.Die() 上报 QuestManager，
    // 虽然 Boss 任务的 targetId 是 "Boss" 不会被误计入，但绕开上报链路更干净。
    // 场景重载（玩家死亡）后这些敌人会自动回来，不影响击杀类任务。
    void ClearRegularEnemies()
    {
        var enemies = GameObject.FindGameObjectsWithTag("Enemy");
        foreach (var e in enemies) Destroy(e);
        if (enemies.Length > 0) Debug.Log($"[Boss] Arena cleared: removed {enemies.Length} regular enemies");
    }

    /// <summary>运行时造一个 Boss，不需要预制体或任何编辑器操作。</summary>
    public static BossController Create(Vector3 groundPos, string displayName,
                                        float maxHealth, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        // 先关掉再配置：AddComponent 会立刻跑 Awake，
        // 若此时 maxHealth 还是默认值 100，Health 会把当前血量锁死在 100。
        go.SetActive(false);

        go.name = "Boss_" + displayName;
        go.tag = "Boss";                                  // Health.Die() 用它上报 QuestManager
        go.transform.localScale = new Vector3(1.7f, 1.9f, 1.7f);
        go.transform.position = groundPos + Vector3.up * 1.9f;   // 胶囊半高 = scale.y

        var rend = go.GetComponent<Renderer>();
        rend.material.color = color;

        // 运动学刚体：BossController 用 transform.position 推动它。若它只是个静态碰撞体，
        // 冲刺时会被物理引擎当成"瞬移的墙"，把玩家的动态刚体直接顶飞出地图。
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        var health = go.AddComponent<Health>();
        health.maxHealth = maxHealth;
        health.destroyOnDeath = true;
        health.squashScale = 1f;      // 关掉通用受击缩放：BossController 自己管体型，否则两边打架

        var boss = go.AddComponent<BossController>();
        boss.displayName = string.IsNullOrEmpty(displayName) ? "Champion" : displayName;

        go.SetActive(true);           // 此刻各组件的 Awake 才跑，读到的是配置好的值
        return boss;
    }

    static float GroundY(Vector3 pos)
    {
        Vector3 from = new Vector3(pos.x, pos.y + 50f, pos.z);
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 200f,
                            ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return pos.y;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
