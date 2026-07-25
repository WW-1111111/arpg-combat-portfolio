using System.Collections.Generic;
using UnityEngine;

// 种子化的机制实例生成器（O2 + NFR5 + O5 的前置）。
// 职责：产出【机制层】+【情境层】完全由系统控制、且可复现的任务实例。
//   - 同一个 baseSeed → 每次跑出完全相同的一批实例 → 配对比较成立、实验可复现
//   - 三个叙事器拿到的是同一批实例，唯一变量是"谁来写叙事"
// 注意：这里产出的全部内容都不经过 LLM —— 这是"机制/叙事分离"的机制侧。
public class ScenarioGenerator : MonoBehaviour
{
    [Tooltip("实验主种子。固定它 → 整批实例完全可复现（NFR5）")]
    public int baseSeed = 42;

    // ===== 情境词表（系统控制，非 LLM 产出）=====
    static readonly string[] Locations = {
        "瀑布下的水潭", "芦苇城下", "荒废的寺庙", "竹林小径",
        "断崖边的栈道", "被焚毁的村落", "结冰的池畔", "敌军的哨所"
    };
    static readonly string[] Times = {
        "拂晓时分", "正午", "黄昏", "深夜", "雨夜", "大雪初霁"
    };
    static readonly string[] EnemyFlavours = {
        "侵略军的足轻", "饮过瀑布之水的怪物", "失去理智的武士",
        "敌国的斥候", "手持火铳的雇佣兵", "芦苇之国的叛兵"
    };
    static readonly string[] ItemFlavours = {
        "止血草", "火药壶", "供奉用的香油", "封印用的符纸", "干粮袋", "药引"
    };
    // 原创头目名（呼应"饮泉化怪"的设定，不使用任何商业游戏的专名）
    static readonly string[] BossNames = {
        "沉泉将军", "无首骑将", "白骨僧", "枯苇之主", "溺影"
    };

    // 生成第 index 个某类型的实例。种子由 baseSeed 与 index 决定 → 单个实例也可独立复现。
    public Quest Generate(QuestType type, int index)
    {
        int seed = baseSeed * 1000 + index;
        var rng = new System.Random(seed);

        var q = new Quest
        {
            type = type,
            currentCount = 0,
            seed = seed,
            instanceIndex = index,
            location = Locations[rng.Next(Locations.Length)],
            timeOfDay = Times[rng.Next(Times.Length)]
        };

        switch (type)
        {
            case QuestType.DefeatCount:
                q.targetId = "Enemy";                                  // 机制用：敌人Tag
                q.requiredCount = rng.Next(2, 9);                      // 2~8 个
                q.targetFlavour = EnemyFlavours[rng.Next(EnemyFlavours.Length)];
                break;
            case QuestType.Fetch:
                q.targetId = "Item";                                   // 机制用：物品ID
                q.requiredCount = rng.Next(2, 6);                      // 2~5 个
                q.targetFlavour = ItemFlavours[rng.Next(ItemFlavours.Length)];
                break;
            case QuestType.Boss:
                q.targetId = "Boss";                                   // 机制用：BossTag
                q.requiredCount = 1;
                q.targetFlavour = BossNames[rng.Next(BossNames.Length)];
                break;
        }
        return q;
    }

    // 评估用：每种类型各生成 perType 个实例（例如 30 → 共 90 个）
    public List<Quest> GenerateBatch(int perType)
    {
        var list = new List<Quest>();
        int idx = 0;
        foreach (QuestType t in new[] { QuestType.DefeatCount, QuestType.Fetch, QuestType.Boss })
            for (int i = 0; i < perType; i++)
                list.Add(Generate(t, idx++));
        return list;
    }
}
