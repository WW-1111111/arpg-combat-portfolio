using System.Collections;
using UnityEngine;

// O5 评估的数据生成器：一次跑完整个配对语料。
//
// 流程：ScenarioGenerator 种子化产出 N×3 个机制实例 →
//       每个实例分别交给三档叙事器（固定模板 / 随机模板 / LLM）→
//       全部写进 EvalLogs/batch_generations.jsonl，供 Python 分析。
//
// 配对设计：同一个实例(同种子、同情境)被三档各渲染一次，
//   所以三组数据在机制与情境上完全对齐，可做配对显著性检验。
public class BatchRunner : MonoBehaviour
{
    [Header("规模")]
    [Tooltip("每种任务类型生成多少个实例（3种类型 × N × 3档 = 总条数）")]
    public int instancesPerType = 30;

    [Header("节流")]
    [Tooltip("每个实例之间的间隔秒数，避免连续请求打满 API")]
    public float delayBetweenInstances = 0.4f;
    [Tooltip("单次生成的最长等待秒数，超时则跳过，防止协程卡死")]
    public float perCallTimeout = 60f;

    [Header("输出")]
    public string outputFileName = "batch_generations.jsonl";

    [Header("运行")]
    public bool runOnStart = true;
    [Tooltip("批量运行时禁用 QuestManager，避免它额外生成一条无关记录")]
    public bool disableQuestManager = true;

    [Tooltip("批量运行时关闭敌人AI并让玩家无敌。必须开启——否则玩家被打死会重载场景，" +
             "导致协程中断、批量从头重跑并写入重复数据")]
    public bool freezeCombatDuringBatch = true;

    [Header("世界背景（必须与 QuestManager 里的一致）")]
    [TextArea]
    public string worldContext =
        "芦苇之国——战国乱世中最弱小的国家。国人迷恋瀑布之水，只因那水代表“不死”的力量；" +
        "纵然饮下它会使人失去理智、化为怪物，人们为求永生仍甘愿如此。国力因而凋敝，" +
        "芦苇之国屡遭强邻侵略，内忧外患。主角是一名忍者，名为“影子”，侍奉其主“神子”。" +
        "影子的目标是救出主人，神子的目标是斩断“不死”，影子亦愿助其达成。";

    void Awake()
    {
        if (disableQuestManager)
        {
            var qm = FindAnyObjectByType<QuestManager>();   // 只需任意一个，不依赖实例ID排序
            if (qm != null) qm.enabled = false;            // 阻止它在 Start 里生成任务
        }

        if (freezeCombatDuringBatch) FreezeCombat();
    }

    // 关键保护：玩家死亡会 LoadScene 重载场景，直接杀死本协程并让批量从头重跑、
    // 向同一文件追加重复数据。批量期间关掉战斗，确保 5 分钟能安全跑完。
    void FreezeCombat()
    {
        int n = 0;
        foreach (var ai in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            ai.enabled = false;   // 敌人不再追击/攻击
            n++;
        }

        var player = GameObject.FindGameObjectWithTag("Player");
        var hp = player != null ? player.GetComponent<Health>() : null;
        if (hp != null) hp.invulnerable = true;   // 双保险：即使有别的伤害来源也打不死

        Debug.Log($"[批量] 已冻结战斗：关闭 {n} 个敌人AI，玩家无敌={(hp != null)}");
    }

    void Start()
    {
        if (runOnStart) StartCoroutine(RunBatch());
    }

    public IEnumerator RunBatch()
    {
        QuestLogger.SetFileName(outputFileName);

        var scenario = GetComponent<ScenarioGenerator>();
        if (scenario == null) scenario = gameObject.AddComponent<ScenarioGenerator>();
        var fixedTpl = GetComponent<TemplateNarrator>();
        if (fixedTpl == null) fixedTpl = gameObject.AddComponent<TemplateNarrator>();
        var randTpl = GetComponent<RandomTemplateNarrator>();
        if (randTpl == null) randTpl = gameObject.AddComponent<RandomTemplateNarrator>();
        var llm = GetComponent<QuestGenerator>();
        if (llm == null) llm = gameObject.AddComponent<QuestGenerator>();

        var types = new[] { QuestType.DefeatCount, QuestType.Fetch, QuestType.Boss };
        int total = types.Length * instancesPerType * 3;
        int done = 0, timeouts = 0, index = 0;
        float startedAt = Time.realtimeSinceStartup;

        Debug.Log($"[批量] 开始：{types.Length}类 × {instancesPerType}实例 × 3档 = {total} 条 " +
                  $"→ EvalLogs/{outputFileName}（baseSeed={scenario.baseSeed}）");

        foreach (var type in types)
        {
            for (int i = 0; i < instancesPerType; i++, index++)
            {
                // ① 固定模板（地板参照）
                yield return RunOne(fixedTpl, scenario.Generate(type, index), r => { done += r; timeouts += 1 - r; });

                // ② 随机模板（强基线）—— 用实例种子，保证可复现
                var q2 = scenario.Generate(type, index);
                randTpl.SetSeed(q2.seed);
                yield return RunOne(randTpl, q2, r => { done += r; timeouts += 1 - r; });

                // ③ LLM（本项目方法）
                yield return RunOne(llm, scenario.Generate(type, index), r => { done += r; timeouts += 1 - r; });

                if (index % 5 == 0 || done >= total)
                    Debug.Log($"[批量] 进度 {done}/{total}（{type} 第{i + 1}/{instancesPerType}个）");

                if (delayBetweenInstances > 0f)
                    yield return new WaitForSeconds(delayBetweenInstances);
            }
        }

        float mins = (Time.realtimeSinceStartup - startedAt) / 60f;
        Debug.Log($"[批量] 完成：成功 {done}/{total}，超时 {timeouts}，耗时 {mins:F1} 分钟。" +
                  $"数据在 EvalLogs/{outputFileName}");
    }

    // 跑一次生成并等它回调；超时则放弃这一条（回调 0），避免整个批次卡死
    IEnumerator RunOne(IQuestNarrator narrator, Quest quest, System.Action<int> onFinished)
    {
        bool finished = false;
        narrator.GenerateNarrative(quest, worldContext, _ => finished = true);

        float t0 = Time.realtimeSinceStartup;
        while (!finished && Time.realtimeSinceStartup - t0 < perCallTimeout)
            yield return null;

        if (!finished)
            Debug.LogWarning($"[批量] 超时跳过：{quest.type} instance={quest.instanceIndex}");
        onFinished?.Invoke(finished ? 1 : 0);
    }
}
