using System;
using UnityEngine;

// 强基线（strong baseline）：随机模板。
// 与固定模板的区别：每个槽位有多种人工撰写的写法，随机组合 → 用词有多样性。
// 与 LLM 的区别：只能把情境要素【机械地填进槽位】，无法把它们编织成有机的叙事，
//   也无法适应世界设定的变化（词池是人工写死、绑定在芦苇之国世界上的）。
//
// 公平性立场（重要，写进论文方法学）：
//   本基线与 LLM 收到【完全相同】的输入（世界背景 + 机制 + 情境三要素），
//   词池也刻意写得用心并使用了全部情境要素，是一个尽力而为的传统方案。
//   因此两者的差异可归因于"同样的信息，谁用得更好"，而非信息量或配置程度的差异。
public class RandomTemplateNarrator : MonoBehaviour, IQuestNarrator
{
    [Tooltip("<0 = 按时间随机(交互游玩)；>=0 = 固定种子(批量实验可复现, NFR5)")]
    public int seed = -1;

    System.Random rng;

    void EnsureRng()
    {
        if (rng == null) rng = seed >= 0 ? new System.Random(seed) : new System.Random();
    }

    // 批量实验时由 BatchRunner 调用：每个实例给不同种子，既随机又可复现
    public void SetSeed(int s) { seed = s; rng = new System.Random(s); }

    string Pick(string[] pool) => pool[rng.Next(pool.Length)];

    // ===== 击杀类词池 =====
    static readonly string[] KillTitles = {
        "Subjugation Order", "Order to Purge", "Trouble in the Province", "Repel the Incursion",
        "Enemy Cull", "Border Alarm", "Bandit Clearance", "Quell the Unrest"
    };
    static readonly string[] KillOpenings = {
        "Word reached the border of the Reedlands {time}, ", "A scout returned from the front {time}, ",
        "The village elder sent a message {time}, ", "Signal fires rose from the outpost {time}, ",
        "An attendant of the Divine Heir passed on an order {time}, ",
        "There was a commotion below the walls of Reedhold {time}, "
    };
    static readonly string[] KillBodies = {
        "and {n} {tf} have dug in around {loc}.", "and {n} {tf} are threatening the villagers at {loc}.",
        "and {n} {tf} have gathered near {loc}.", "and {n} {tf} have slipped into {loc}.",
        "and {n} {tf} are preying on the countryside at {loc}.",
        "and {n} {tf} were found lying in ambush at {loc}."
    };
    static readonly string[] KillObjectives = {
        "Destroy the {tf}", "Clear the {tf} from {loc}", "Sweep the enemies from {loc}",
        "Repel the {tf}", "Remove the threat at {loc}"
    };
    static readonly string[] KillCompletions = {
        "The {tf} have been cleared, and peace returns to {loc}.",
        "The threat is gone. The villagers may sleep.", "The {tf} who came have all been put down.",
        "There is quiet at {loc}, for now.", "The trouble is settled. The elder sends word of thanks.",
        "Silence has returned to {loc}."
    };

    // ===== 取物类词池 =====
    static readonly string[] FetchTitles = {
        "Gathering Errand", "A Request for Goods", "Needed in the Reedlands",
        "Retrieval Task", "Supply Levy", "Urgently Required"
    };
    static readonly string[] FetchOpenings = {
        "The village healer sent word {time}, ", "The elder dispatched a messenger {time}, ",
        "A monk from the temple passed on a message {time}, ",
        "An attendant of the Divine Heir gave an order {time}, ",
        "An old woman stopped you at the village gate {time}, ",
        "The apothecary's assistant looked troubled {time}, "
    };
    static readonly string[] FetchBodies = {
        "saying that {n} {tf} are needed, and that some may still be found at {loc}.",
        "saying that {n} {tf} are urgently short, and that {loc} is said to have them.",
        "asking that you go to {loc} and bring back {n} {tf}.",
        "asking that you obtain {n} {tf} at {loc}.",
        "saying it would be well if {n} {tf} could be gathered from {loc}."
    };
    static readonly string[] FetchObjectives = {
        "Bring back the {tf}", "Collect the {tf} at {loc}", "Obtain the {tf}", "Search {loc} for the {tf}"
    };
    static readonly string[] FetchCompletions = {
        "The {tf} have been gathered.", "The goods were delivered, with thanks.",
        "The errand is done. There will be other days.",
        "The {tf} changed hands, and the village's need is met."
    };

    // ===== Boss 类词池 =====
    static readonly string[] BossTitles = {
        "Writ of Subjugation", "Banish the Fiend", "A Powerful Foe",
        "A Pact of Life and Death", "Decapitation Order", "Old Enemy"
    };
    static readonly string[] BossOpenings = {
        "Word came from across the Reedlands {time}, ",
        "A scout brought back intelligence at the cost of their life {time}, ",
        "The Divine Heir told you gravely {time}, ",
        "A hunter came down from the mountains in terror {time}, ",
        "The temple bell rang out {time}, "
    };
    static readonly string[] BossBodies = {
        "that {tf} has appeared at {loc}.", "that a powerful foe called {tf} has taken {loc}.",
        "that traces of {tf} were found at {loc}.",
        "that {tf} now blocks the road deeper into {loc}.",
        "that {tf} waits at {loc} for a challenger."
    };
    static readonly string[] BossObjectives = {
        "Defeat {tf}", "Subjugate {tf} at {loc}", "Cut down {tf}", "Finish the fight with {tf}"
    };
    static readonly string[] BossCompletions = {
        "You have struck down {tf}.", "The foe is dead. It was no easy fight.",
        "At {loc}, {tf} fell, and silence returned.", "The fight is over, and the blade is unbent."
    };

    public void GenerateNarrative(Quest quest, string worldContext, Action<Quest> onDone)
    {
        EnsureRng();
        // 情境三要素会被填进槽位；但 worldContext 仍未被使用 —— 词池写死在芦苇之国世界，无法迁移。
        switch (quest.type)
        {
            case QuestType.DefeatCount:
                quest.title          = Pick(KillTitles);
                quest.description    = Fill(Pick(KillOpenings) + Pick(KillBodies), quest);
                quest.objectiveText  = Fill(Pick(KillObjectives), quest);
                quest.completionText = Fill(Pick(KillCompletions), quest);
                break;
            case QuestType.Fetch:
                quest.title          = Pick(FetchTitles);
                quest.description    = Fill(Pick(FetchOpenings) + Pick(FetchBodies), quest);
                quest.objectiveText  = Fill(Pick(FetchObjectives), quest);
                quest.completionText = Fill(Pick(FetchCompletions), quest);
                break;
            case QuestType.Boss:
                quest.title          = Pick(BossTitles);
                quest.description    = Fill(Pick(BossOpenings) + Pick(BossBodies), quest);
                quest.objectiveText  = Fill(Pick(BossObjectives), quest);
                quest.completionText = Fill(Pick(BossCompletions), quest);
                break;
        }

        QuestLogger.Log(new GenerationRecord
        {
            narrator       = "TemplateRandom",
            model          = "none",
            temperature    = 0f,
            seed           = quest.seed,
            instanceIndex  = quest.instanceIndex,
            questType      = quest.type.ToString(),
            targetId       = quest.targetId,
            requiredCount  = quest.requiredCount,
            location       = quest.location,
            targetFlavour  = quest.targetFlavour,
            timeOfDay      = quest.timeOfDay,
            worldContext   = worldContext,   // 记下来，但并未使用
            systemPrompt   = "",
            userPrompt     = "",
            rawResponse    = "",
            title          = quest.title,
            description    = quest.description,
            objectiveText  = quest.objectiveText,
            completionText = quest.completionText,
            latencyMs      = 0,
            jsonValid      = true,
            usedFallback   = false
        });

        Debug.Log($"[随机模板] {quest.title} | {quest.description}");
        onDone?.Invoke(quest);
    }

    // 把占位符换成机制层/情境层的真实内容
    string Fill(string s, Quest q) => s.Replace("{n}", q.requiredCount.ToString())
                                       .Replace("{tf}", q.targetFlavour)
                                       .Replace("{loc}", q.location)
                                       .Replace("{time}", q.timeOfDay);
}
