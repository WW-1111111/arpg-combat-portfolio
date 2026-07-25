using System;
using UnityEngine;

// B3：模板基线。不调用 LLM，用固定模板填叙事，作为与 B2(LLM) 对比的对照组。
// 关键特征（正是要凸显的基线短板）：
//   1) 模板结构固定 → 同一机制每次生成几乎相同 → 多样性(distinct-n/Self-BLEU)很低
//   2) 忽略 worldContext → 不随世界背景变化 → 情境契合度低
// 论文用它来证明：LLM 在多样性和情境契合度上优于固定模板，而机制始终安全不变。
public class TemplateNarrator : MonoBehaviour, IQuestNarrator
{
    public void GenerateNarrative(Quest quest, string worldContext, Action<Quest> onDone)
    {
        // 注意：worldContext 被故意忽略——模板不适应世界背景，这是与 LLM 的核心差异。
        switch (quest.type)
        {
            // 文案有世界味（真实游戏的写死文案也是如此），但【完全静态】：
            // 同一类型永远同一段文字，且一律不使用情境三要素 → 这是"地板"的真正含义。
            case QuestType.DefeatCount:
                quest.title          = "讨伐任务";
                quest.description    = $"芦苇之国内忧外患，境内又现敌踪。共 {quest.requiredCount} 名，将其全部消灭，以免祸及村民。";
                quest.objectiveText  = $"消灭敌人 0/{quest.requiredCount}";
                quest.completionText = "敌人已被清除，任务完成。";
                break;
            case QuestType.Fetch:
                quest.title          = "取物任务";
                quest.description    = $"芦苇之国物资匮乏，眼下需要 {quest.requiredCount} 件补给，请设法取得。";
                quest.objectiveText  = $"收集物品 0/{quest.requiredCount}";
                quest.completionText = "物资已备齐，任务完成。";
                break;
            case QuestType.Boss:
                quest.title          = "头目讨伐";
                quest.description    = "一名强敌出现在芦苇之国境内，将其击败。";
                quest.objectiveText  = "击败头目";
                quest.completionText = "头目已被击败，任务完成。";
                break;
        }
        // FR7：基线也要记日志，才能和 LLM 做同口径对比
        QuestLogger.Log(new GenerationRecord
        {
            narrator        = "Template",
            model           = "none",
            temperature     = 0f,
            questType       = quest.type.ToString(),
            targetId        = quest.targetId,
            requiredCount   = quest.requiredCount,
            location        = quest.location,        // 记下来，但固定模板一律不用
            targetFlavour   = quest.targetFlavour,
            timeOfDay       = quest.timeOfDay,
            seed            = quest.seed,
            instanceIndex   = quest.instanceIndex,
            worldContext    = worldContext,   // 记下来，但模板并未使用它
            systemPrompt    = "",
            userPrompt      = "",
            rawResponse     = "",
            title           = quest.title,
            description     = quest.description,
            objectiveText   = quest.objectiveText,
            completionText  = quest.completionText,
            latencyMs       = 0,
            jsonValid       = true,
            usedFallback    = false
        });

        Debug.Log($"[模板生成] {quest.title} | {quest.description}");
        onDone?.Invoke(quest);   // 同步返回，不走网络
    }
}
