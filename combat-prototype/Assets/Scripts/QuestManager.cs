using System;
using UnityEngine;

public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance;   // 简单单例，方便敌人/物品通知它

    public Quest currentQuest;

    // 三档基线：Template=固定模板(地板/现状参照) → TemplateRandom=随机模板(主要对手) → LLM=本项目方法
    public enum NarratorMode { LLM, Template, TemplateRandom }

    [Header("叙事来源：切这个开关对比三档基线")]
    public NarratorMode narratorMode = NarratorMode.LLM;

    [Header("交互游玩时用哪个实例（改 Index 可换一组情境）")]
    public QuestType playQuestType = QuestType.DefeatCount;
    public int playInstanceIndex = 0;

    [Header("是否显示任务栏 UI（FR6）")]
    public bool showQuestUI = true;
    [TextArea] public string worldContext =
        "The Reedlands — the weakest nation in an age of warring states. Its people are obsessed " +
        "with the Fallswater, for that water carries the power of undeath; though drinking it " +
        "strips away reason and turns a person into a monster, they take it gladly in exchange " +
        "for eternal life. The nation has withered as a result, and the Reedlands are invaded " +
        "again and again by stronger neighbours, beset from within and without. You are a shinobi " +
        "called Shade, sworn to your master, the Divine Heir. Shade's goal is to rescue the Divine " +
        "Heir; the Divine Heir's goal is to sever undeath itself, and Shade means to help.";

    public event Action<Quest> OnQuestUpdated;    // 进度变化 → 通知UI
    public event Action<Quest> OnQuestCompleted;  // 完成 → 通知UI

    void Awake()
    {
        Instance = this;
        // FR6：自动挂上任务栏UI（UI会在Start里补读currentQuest，不怕执行顺序）
        if (showQuestUI && GetComponent<QuestLogUI>() == null)
            gameObject.AddComponent<QuestLogUI>();
    }

    void Start()
    {
        // 机制层 + 情境层：全部由 ScenarioGenerator 种子化产出（系统控制，LLM碰不到）
        var scenario = GetComponent<ScenarioGenerator>();
        if (scenario == null) scenario = gameObject.AddComponent<ScenarioGenerator>();
        Quest mechanic = scenario.Generate(playQuestType, playInstanceIndex);

        // 按开关拿到对应叙事器(LLM或模板)，交它填叙事，完成后再正式开始任务
        IQuestNarrator narrator = ResolveNarrator();
        narrator.GenerateNarrative(mechanic, worldContext, quest => StartQuest(quest));
    }

    // 按 narratorMode 拿到对应叙事器；场景没挂就运行时自动挂上。两者都实现 IQuestNarrator。
    IQuestNarrator ResolveNarrator()
    {
        if (narratorMode == NarratorMode.Template)
        {
            var t = GetComponent<TemplateNarrator>();
            if (t == null) t = gameObject.AddComponent<TemplateNarrator>();
            return t;
        }
        else if (narratorMode == NarratorMode.TemplateRandom)
        {
            var r = GetComponent<RandomTemplateNarrator>();
            if (r == null) r = gameObject.AddComponent<RandomTemplateNarrator>();
            return r;
        }
        else
        {
            var g = GetComponent<QuestGenerator>();
            if (g == null) g = gameObject.AddComponent<QuestGenerator>();
            return g;
        }
    }

    public void StartQuest(Quest quest)
    {
        currentQuest = quest;
        OnQuestUpdated?.Invoke(currentQuest);
        Debug.Log($"Quest accepted: {quest.title}");
    }

    // 敌人死亡时调用（击杀/Boss类）
    public void ReportKill(string enemyId)
    {
        if (currentQuest == null || currentQuest.IsComplete) return;
        if (currentQuest.type != QuestType.DefeatCount && currentQuest.type != QuestType.Boss) return;
        if (currentQuest.targetId != enemyId) return;
        currentQuest.currentCount++;
        AfterProgress();
    }

    // 拾取物品时调用（取物类）
    public void ReportItemCollected(string itemId)
    {
        if (currentQuest == null || currentQuest.IsComplete) return;
        if (currentQuest.type != QuestType.Fetch) return;
        if (currentQuest.targetId != itemId) return;
        currentQuest.currentCount++;
        AfterProgress();
    }

    void AfterProgress()
    {
        OnQuestUpdated?.Invoke(currentQuest);
        Debug.Log($"Progress: {currentQuest.currentCount}/{currentQuest.requiredCount}");
        if (currentQuest.IsComplete)
        {
            Debug.Log($"Quest complete: {currentQuest.completionText}");
            OnQuestCompleted?.Invoke(currentQuest);
        }
    }
}
