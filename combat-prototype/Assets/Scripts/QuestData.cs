using System;

// 三种任务类型
public enum QuestType { Fetch, DefeatCount, Boss }

[Serializable]
public class Quest
{
    // ---- 机制层（B1填，游戏逻辑用）----
    public QuestType type;
    public string targetId;       // 取物:物品ID / 击杀:敌人tag / Boss:bossID
    public int requiredCount;     // "击杀N个"的N；取物和Boss设1
    public int currentCount;      // 当前进度

    // ---- 情境层（由 ScenarioGenerator 种子化产出，系统控制）----
    // 关键：三个叙事器都会收到【完全相同】的这些信息，
    //      差异只来自"同样的信息，谁用得更好"，而非谁拿到的信息更多。
    public string location;       // 地点，如"废弃的寺庙"
    public string targetFlavour;  // 目标的具体样貌，如"侵略军的足轻"/"止血草"/"沉泉将军"
    public string timeOfDay;      // 时段/天气，如"雨夜"

    // ---- 实验元数据（可复现，NFR5）----
    public int seed;              // 生成本实例所用的种子
    public int instanceIndex;     // 批次内序号

    // ---- 叙事层（B1先占位；B2由LLM填；B3由模板填）----
    public string title;          // 标题
    public string description;    // 背景描述
    public string objectiveText;  // 目标提示
    public string completionText; // 完成语

    public bool IsComplete => currentCount >= requiredCount;
}
