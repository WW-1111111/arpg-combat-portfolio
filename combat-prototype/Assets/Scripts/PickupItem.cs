using UnityEngine;

// FR3 取物类任务的可拾取物品。
//
// 机制/叙事分离在这里同样成立：本脚本只认机制 ID（ScenarioGenerator 给的 "Item"），
// 玩家看到的名字来自叙事层的 targetFlavour，由 PickupSpawner 传进来只做显示，
// 拾取判定完全不看它。
[RequireComponent(typeof(SphereCollider))]
public class PickupItem : MonoBehaviour
{
    public string itemId = "Item";      // 机制 ID，必须与 Quest.targetId 一致
    public float bobHeight = 0.25f;     // 上下浮动幅度
    public float bobSpeed = 2f;
    public float spinSpeed = 90f;

    Vector3 basePos;
    float phase;
    bool taken;

    void Start()
    {
        basePos = transform.position;
        phase = Random.value * Mathf.PI * 2f;   // 错开相位，一堆物品不会整齐划一地上下动
    }

    void Update()
    {
        float t = Time.time * bobSpeed + phase;
        transform.position = basePos + Vector3.up * (Mathf.Sin(t) * bobHeight);
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    void OnTriggerEnter(Collider other)
    {
        if (taken || !other.CompareTag("Player")) return;
        taken = true;
        QuestManager.Instance?.ReportItemCollected(itemId);   // 通知任务系统：收集 +1
        Destroy(gameObject);
    }

    /// <summary>运行时造一个拾取物，不需要预制体。</summary>
    public static PickupItem Create(Vector3 pos, string itemId, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Pickup_" + itemId;
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.45f;

        var rend = go.GetComponent<Renderer>();
        rend.material.color = color;
        rend.material.EnableKeyword("_EMISSION");
        rend.material.SetColor("_EmissionColor", color * 1.6f);

        // 触发器 + 运动学刚体：无论玩家用 CharacterController 还是 Rigidbody 都能触发
        var col = go.GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 1.6f;                       // 判定比外观大，捡起来手感宽松
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // 一盏小灯，录像里更醒目（数量很少，开销可忽略）
        var lightGO = new GameObject("Glow");
        lightGO.transform.SetParent(go.transform, false);
        var lt = lightGO.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.color = color;
        lt.range = 4f;
        lt.intensity = 2.2f;

        var item = go.AddComponent<PickupItem>();
        item.itemId = itemId;
        return item;
    }
}
