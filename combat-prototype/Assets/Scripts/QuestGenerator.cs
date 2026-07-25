using System;
using System.IO;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

// B2：调用 DeepSeek，用提示词生成任务的【叙事层】文字，填进 Quest。
// 核心原则：只填叙事字段(title/description/objectiveText/completionText)，
//          机制字段(type/targetId/requiredCount)由外部固定传入，LLM 永远碰不到。
public class QuestGenerator : MonoBehaviour, IQuestNarrator
{
    [Header("DeepSeek 配置")]
    public string apiUrl = "https://api.deepseek.com/chat/completions";
    public string model = "deepseek-v4-flash";   // v4-flash便宜快，够用；要更强可换 deepseek-v4-pro
    [Range(0f, 2f)] public float temperature = 1.0f;   // 越高越多样，评估“多样性”时可调
    [TextArea] public string apiKeyInspector = "";     // 备用：也可直接粘到这里(但仓库是公开的,别提交!)

    // 从项目根目录的 deepseek_key.txt 读密钥(该文件要加进 .gitignore，不上传)
    string LoadApiKey()
    {
        try
        {
            string path = Path.Combine(Application.dataPath, "..", "deepseek_key.txt");
            if (File.Exists(path)) return File.ReadAllText(path).Trim();
        }
        catch (Exception e) { Debug.LogWarning("读密钥文件失败: " + e.Message); }
        return apiKeyInspector.Trim();
    }

    // 对外入口：传入“机制已固定的 Quest” + 世界背景，异步生成叙事后回调
    public void GenerateNarrative(Quest mechanicQuest, string worldContext, Action<Quest> onDone)
    {
        StartCoroutine(GenerateRoutine(mechanicQuest, worldContext, onDone));
    }

    IEnumerator GenerateRoutine(Quest quest, string worldContext, Action<Quest> onDone)
    {
        string apiKey = LoadApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("没有 DeepSeek 密钥，退回占位叙事。");
            var recNoKey = NewRecord(quest, worldContext, "", "");
            recNoKey.error = "missing_api_key";
            recNoKey.usedFallback = true;
            QuestLogger.Log(recNoKey);
            onDone?.Invoke(Fallback(quest));
            yield break;
        }

        // ---- 1) 构造提示词 ----
        string mechanicDesc = DescribeMechanic(quest);
        string systemPrompt =
            "你是一个动作RPG的任务叙事设计师。根据给定的【任务机制】和【世界背景】，" +
            "为任务写一段贴合情境的叙事包装。严格要求：不要改动任何机制数值或目标，" +
            "只输出叙事文本。必须只输出一个 JSON 对象，字段为：" +
            "title(标题)、description(背景描述)、objectiveText(目标提示)、completionText(完成语)。";
        string situationDesc =
            $"地点：{quest.location}；对象：{quest.targetFlavour}；时机：{quest.timeOfDay}";
        string userPrompt =
            $"【世界背景】{worldContext}\n【任务机制】{mechanicDesc}\n【当前情境】{situationDesc}\n" +
            "请用中文生成上述四个字段的 JSON。要求把情境要素（地点、对象、时机）自然地融入叙事，" +
            "而不是简单罗列。objectiveText 里可以体现进度语气，但不要写死具体数字。";

        // 准备日志记录 + 开始计时（FR7）
        var rec = NewRecord(quest, worldContext, systemPrompt, userPrompt);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // ---- 2) 组装请求体(JSON) ----
        var body = new ReqBody
        {
            model = model,
            temperature = temperature,
            response_format = new ResponseFormat { type = "json_object" }, // 让它必定返回JSON
            messages = new[]
            {
                new Msg { role = "system", content = systemPrompt },
                new Msg { role = "user",   content = userPrompt   }
            }
        };
        string json = JsonUtility.ToJson(body);

        // ---- 3) 发 HTTP POST 请求(异步,不卡游戏) ----
        using (UnityWebRequest req = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] raw = System.Text.Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(raw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            req.timeout = 30;   // 30秒超时，避免卡住时静默无输出

            yield return req.SendWebRequest();   // 协程在这里等服务器回复

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("DeepSeek 请求失败: " + req.error + "\n" + req.downloadHandler.text);
                rec.latencyMs = sw.ElapsedMilliseconds;
                rec.rawResponse = req.downloadHandler.text;
                rec.error = "request_failed: " + req.error;
                rec.usedFallback = true;
                QuestLogger.Log(rec);
                onDone?.Invoke(Fallback(quest));
                yield break;
            }

            // ---- 4) 解析返回 ----
            // 外层: {choices:[{message:{content:"<这里是我们要的JSON字符串>"}}]}
            rec.latencyMs = sw.ElapsedMilliseconds;
            rec.rawResponse = req.downloadHandler.text;

            RespBody resp = JsonUtility.FromJson<RespBody>(req.downloadHandler.text);
            if (resp == null || resp.choices == null || resp.choices.Length == 0)
            {
                Debug.LogError("返回结构异常: " + req.downloadHandler.text);
                rec.error = "bad_response_structure";
                rec.usedFallback = true;
                QuestLogger.Log(rec);
                onDone?.Invoke(Fallback(quest));
                yield break;
            }
            string content = resp.choices[0].message.content;   // 这是一段JSON字符串
            QuestNarrative n = JsonUtility.FromJson<QuestNarrative>(content);

            // 简易 schema 校验：四个叙事字段必须齐全（O3 完整版会加重试逻辑）
            bool valid = n != null
                      && !string.IsNullOrEmpty(n.title)
                      && !string.IsNullOrEmpty(n.description)
                      && !string.IsNullOrEmpty(n.objectiveText)
                      && !string.IsNullOrEmpty(n.completionText);
            rec.jsonValid = valid;
            if (!valid)
            {
                Debug.LogWarning("生成内容字段不全，退回兜底模板。");
                rec.error = "schema_invalid";
                rec.usedFallback = true;
                QuestLogger.Log(rec);
                onDone?.Invoke(Fallback(quest));
                yield break;
            }

            // ---- 5) 只把叙事字段填回 Quest，机制字段原封不动 ----
            quest.title          = n.title;
            quest.description    = n.description;
            quest.objectiveText  = n.objectiveText;
            quest.completionText = n.completionText;

            rec.title = quest.title;
            rec.description = quest.description;
            rec.objectiveText = quest.objectiveText;
            rec.completionText = quest.completionText;
            QuestLogger.Log(rec);

            Debug.Log("[LLM生成] " + quest.title + " | " + quest.description);
            onDone?.Invoke(quest);
        }
    }

    // 建一条日志记录，填好机制层与生成配置（FR7）
    GenerationRecord NewRecord(Quest q, string world, string sys, string user)
    {
        return new GenerationRecord
        {
            narrator      = "LLM",
            model         = model,
            temperature   = temperature,
            questType     = q.type.ToString(),
            targetId      = q.targetId,
            requiredCount = q.requiredCount,
            location      = q.location,
            targetFlavour = q.targetFlavour,
            timeOfDay     = q.timeOfDay,
            seed          = q.seed,
            instanceIndex = q.instanceIndex,
            worldContext  = world,
            systemPrompt  = sys,
            userPrompt    = user
        };
    }

    // 把机制翻译成自然语言，喂给 LLM（LLM 只读、不改）
    string DescribeMechanic(Quest q)
    {
        switch (q.type)
        {
            // 只描述"数量与类型"这类机制事实；具体样貌由【当前情境】提供，保持机制/表现分离
            case QuestType.DefeatCount: return $"击败 {q.requiredCount} 名敌人。";
            case QuestType.Fetch:       return $"收集 {q.requiredCount} 件物品。";
            case QuestType.Boss:        return "击败一名头目。";
            default: return "未知任务。";
        }
    }

    // 生成失败时的兜底，保证游戏不崩
    Quest Fallback(Quest q)
    {
        q.title = "[占位] 清剿威胁";
        q.description = "[占位] 敌人出现在附近。";
        q.objectiveText = "击败敌人";
        q.completionText = "[占位] 威胁已解除。";
        return q;
    }

    // ---- JsonUtility 用的可序列化结构 ----
    [Serializable] class Msg { public string role; public string content; }
    [Serializable] class ResponseFormat { public string type; }
    [Serializable] class ReqBody {
        public string model; public float temperature;
        public ResponseFormat response_format; public Msg[] messages;
    }
    [Serializable] class RespBody { public Choice[] choices; }
    [Serializable] class Choice { public Message message; }
    [Serializable] class Message { public string content; }
    [Serializable] class QuestNarrative {
        public string title; public string description;
        public string objectiveText; public string completionText;
    }
}
