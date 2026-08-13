using UnityEngine;

// 取物类任务开始时，在玩家周围撒下可拾取物品（FR3）。
//
// 进场景自动创建，等任务生成好再动手 —— 因为要读 requiredCount，
// 而 LLM 叙事是异步的，任务不会在第一帧就绪。
// 多撒两个：玩家不必把地图上每一个都捡完，找起来不至于烦躁。
public class PickupSpawner : MonoBehaviour
{
    public float minRadius = 8f;
    public float maxRadius = 22f;
    public int extraItems = 2;
    public Color itemColor = new Color(1f, 0.82f, 0.35f);

    bool spawned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (GameObject.FindGameObjectWithTag("Player") == null) return;
        if (FindAnyObjectByType<PickupSpawner>() != null) return;
        new GameObject("PickupSpawner", typeof(PickupSpawner));
    }

    void Start()
    {
        var qm = QuestManager.Instance;
        if (qm == null) { enabled = false; return; }     // 批量评估时 QuestManager 被禁用
        qm.OnQuestUpdated += OnQuest;
        if (qm.currentQuest != null) OnQuest(qm.currentQuest);   // 模板是同步的，可能已经好了
    }

    void OnDestroy()
    {
        if (QuestManager.Instance != null) QuestManager.Instance.OnQuestUpdated -= OnQuest;
    }

    void OnQuest(Quest q)
    {
        if (spawned || q == null || q.type != QuestType.Fetch) return;
        spawned = true;

        var player = GameObject.FindGameObjectWithTag("Player");
        Vector3 origin = player != null ? player.transform.position : Vector3.zero;

        int n = q.requiredCount + extraItems;
        for (int i = 0; i < n; i++)
        {
            // 均分一圈再加抖动，避免全挤在一侧
            float angle = (360f / n) * i + Random.Range(-18f, 18f);
            float r = Random.Range(minRadius, maxRadius);
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 pos = origin + dir * r;
            pos.y = GroundY(pos) + 1.1f;
            PickupItem.Create(pos, q.targetId, itemColor);
        }
        Debug.Log($"[拾取] 已生成 {n} 个「{q.targetFlavour}」（需要 {q.requiredCount} 个）");
    }

    // 从高处往下打一条射线找地面；打不到就退回原高度
    static float GroundY(Vector3 pos)
    {
        Vector3 from = new Vector3(pos.x, pos.y + 50f, pos.z);
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 200f,
                            ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return pos.y;
    }
}
