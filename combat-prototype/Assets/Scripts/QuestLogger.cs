using System;
using System.IO;
using System.Text;
using UnityEngine;

// FR7：一次生成尝试的完整记录。这些字段就是后续评估要用的原始数据。
[Serializable]
public class GenerationRecord
{
    // --- 运行标识 ---
    public string timestamp;      // ISO 时间
    public string runId;          // 本次运行的编号(一次Play=一个runId)
    public int    seed;           // 生成该机制实例用的种子(可复现, NFR5)
    public int    instanceIndex;  // 该实例在批次中的序号

    // --- 机制层(固定, LLM碰不到) ---
    public string questType;
    public string targetId;
    public int    requiredCount;

    // --- 情境层(系统产出, 三个叙事器收到的输入完全相同) ---
    public string location;
    public string targetFlavour;
    public string timeOfDay;

    // --- 生成配置 ---
    public string narrator;       // "LLM" 或 "Template"
    public string model;
    public float  temperature;
    public string worldContext;

    // --- 提示词与原始返回(可复现 + 附录证据) ---
    public string systemPrompt;
    public string userPrompt;
    public string rawResponse;

    // --- 产出的叙事层 ---
    public string title;
    public string description;
    public string objectiveText;
    public string completionText;

    // --- 鲁棒性指标 ---
    public long   latencyMs;      // 延迟(NFR2 要证明 <5s)
    public bool   jsonValid;      // JSON 是否合法且字段齐全
    public int    retryCount;     // 重试次数(O3 实现后才会 >0)
    public bool   usedFallback;   // 是否退回了兜底模板
    public string error;          // 失败原因
}

// 把每次生成追加写进 EvalLogs/generations.jsonl（JSON Lines：一行一条记录）
// 注意目录叫 EvalLogs 而不是 Logs —— .gitignore 里 /[Ll]ogs/ 是给 Unity 编辑器日志的，
// 而研究日志需要提交进 Git（CA1 风险表 R6：定期提交源码与日志）。
public static class QuestLogger
{
    static string runId;
    static string filePath;
    static string fileName = "generations.jsonl";

    public static string RunId { get { EnsureInit(); return runId; } }
    public static string FilePath { get { EnsureInit(); return filePath; } }

    // 关闭 Enter Play Mode 的 Reload Domain 后，static 不再在两次 Play 之间自动重置。
    // 若不手动清，BatchRunner 设过的 fileName 会泄漏到下一次普通游玩，
    // 把交互记录静默追加进 batch_generations.jsonl，污染评估语料；runId 也会跨会话不变（违反 NFR5）。
    // SubsystemRegistration 在每次进入 Play 前都会执行，即使 Domain Reload 已关闭。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        runId    = null;
        filePath = null;
        fileName = "generations.jsonl";
    }

    // 批量评估时切到独立文件，避免和交互游玩的记录混在一起
    public static void SetFileName(string name)
    {
        fileName = name;
        filePath = null;   // 强制重新初始化（同时会生成新的 runId）
    }

    static void EnsureInit()
    {
        if (filePath != null) return;
        runId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string dir = Path.Combine(Application.dataPath, "..", "EvalLogs");
        Directory.CreateDirectory(dir);
        filePath = Path.Combine(dir, fileName);
    }

    public static void Log(GenerationRecord r)
    {
        EnsureInit();
        r.runId = runId;
        r.timestamp = DateTime.Now.ToString("o");
        try
        {
            // UTF8Encoding(false) = 不写BOM，否则 Python 读第一行会报错
            File.AppendAllText(filePath, JsonUtility.ToJson(r) + "\n", new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Logger] 写日志失败: " + e.Message);
        }
    }
}
