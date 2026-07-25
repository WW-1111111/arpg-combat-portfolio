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
        "讨伐委托", "清剿之令", "境内除患", "肃清来犯", "剿敌任务", "边境警讯", "除贼", "平乱"
    };
    static readonly string[] KillOpenings = {
        "{time}，芦苇之国边境传来急报，", "{time}，探子自前线归来，", "{time}，村中长老递来口信，",
        "{time}，哨所燃起狼烟，", "{time}，神子身边的侍从传令，", "{time}，芦苇城下传来骚动，"
    };
    static readonly string[] KillBodies = {
        "{loc}一带有 {n} 名{tf}盘踞不去。", "{n} 名{tf}正在{loc}威胁着村民。",
        "{loc}附近聚集了 {n} 名{tf}。", "已有 {n} 名{tf}潜入{loc}。",
        "{n} 名{tf}正在{loc}为祸乡里。", "探得 {n} 名{tf}埋伏于{loc}。"
    };
    static readonly string[] KillObjectives = {
        "消灭{tf}", "清剿{loc}的{tf}", "肃清{loc}一带的敌人", "讨伐来犯的{tf}", "除去{loc}的威胁"
    };
    static readonly string[] KillCompletions = {
        "{tf}已被肃清，{loc}恢复了安宁。", "威胁已除，村民得以安眠。", "来犯的{tf}尽数伏诛。",
        "{loc}暂时太平了。", "敌患已平，长老托人道谢。", "{loc}重归寂静。"
    };

    // ===== 取物类词池 =====
    static readonly string[] FetchTitles = {
        "采买委托", "寻物之托", "芦苇之国所需", "取物任务", "补给征集", "急需之物"
    };
    static readonly string[] FetchOpenings = {
        "{time}，村中医者托人带话，", "{time}，长老差人前来，", "{time}，寺中僧人捎来口信，",
        "{time}，神子身边的侍从传令，", "{time}，村口的老妪拦住了你，", "{time}，药铺的伙计面露难色，"
    };
    static readonly string[] FetchBodies = {
        "需要 {n} 份{tf}，{loc}或许还能寻到。", "眼下急缺 {n} 个{tf}，听说{loc}一带有。",
        "望你前往{loc}，寻回 {n} 件{tf}。", "请设法在{loc}取得 {n} 份{tf}。",
        "若能从{loc}凑齐 {n} 个{tf}便好。"
    };
    static readonly string[] FetchObjectives = {
        "寻回{tf}", "在{loc}收集{tf}", "取得{tf}", "搜寻{loc}的{tf}"
    };
    static readonly string[] FetchCompletions = {
        "{tf}已备齐。", "东西送到了，对方连声道谢。", "委托已了，来日方长。", "{tf}交割完毕，村中之急得解。"
    };

    // ===== Boss 类词池 =====
    static readonly string[] BossTitles = {
        "讨伐令", "除魔", "强敌当前", "生死之约", "斩首任务", "宿敌"
    };
    static readonly string[] BossOpenings = {
        "{time}，芦苇之国传来消息，", "{time}，探子拼死带回情报，", "{time}，神子神色凝重地告知，",
        "{time}，山中猎户惊惶来报，", "{time}，寺中钟声骤响，"
    };
    static readonly string[] BossBodies = {
        "{tf}已现身于{loc}。", "名为{tf}的强敌正盘踞{loc}。", "{tf}的踪迹在{loc}被发现。",
        "{tf}挡在了通往{loc}深处的路上。", "{tf}正在{loc}等待着挑战者。"
    };
    static readonly string[] BossObjectives = {
        "击败{tf}", "讨伐{loc}的{tf}", "斩杀{tf}", "了结与{tf}的一战"
    };
    static readonly string[] BossCompletions = {
        "{tf}已被击倒。", "强敌伏诛，此战不易。", "{tf}倒在了{loc}，四周重归寂静。", "一战既了，刀锋未卷。"
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
