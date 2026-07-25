using UnityEngine;
using UnityEngine.UI;

// FR6：任务栏 UI。全部用代码在运行时构建，避免手动编辑器操作。
//
// 设计要点（对应论文的核心主张）：
//   面板上的文字来自【叙事层】(LLM或模板生成)，
//   而进度计数 "2/3" 来自【机制层】(currentCount/requiredCount)。
//   两者在 UI 上拼合 —— 这正是"机制固定、叙事可换"的可视化证据：
//   无论叙事怎么变，进度与完成判定始终由系统掌控。
//
// 视觉：无底板，仅文字 + 顶部一条横线，屏幕左侧垂直居中。
//   因无底板，所有文字加黑色描边，保证在明亮场景上依然可读。
public class QuestLogUI : MonoBehaviour
{
    [Header("布局")]
    public int panelWidth = 460;
    public float leftMargin = 32f;

    [Header("配色")]
    public Color titleColor = new Color(1f, 0.85f, 0.45f);     // 金
    public Color bodyColor = new Color(0.96f, 0.96f, 0.96f);
    public Color accentColor = new Color(0.5f, 0.8f, 1f);      // 蓝
    public Color doneColor = new Color(0.55f, 1f, 0.55f);      // 绿

    static Font uiFont;
    Text titleText, descText, objText, progressText, badgeText, statusText;
    Quest current;

    static Font GetFont()
    {
        if (uiFont == null)
            uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "SimSun", "Arial" }, 18);
        return uiFont;
    }

    void Start()
    {
        Build();
        var qm = QuestManager.Instance;
        if (qm == null) { SetStatus("未找到 QuestManager"); return; }

        qm.OnQuestUpdated   += Refresh;
        qm.OnQuestCompleted += OnCompleted;

        // 徽标：显示当前用的是哪个叙事器（演示时一眼看出三档差异）
        badgeText.text = qm.narratorMode switch
        {
            QuestManager.NarratorMode.LLM            => "LLM 生成",
            QuestManager.NarratorMode.TemplateRandom => "随机模板",
            _                                        => "固定模板"
        };

        // 若任务已经生成好了(模板是同步的,可能早于本Start)，补显示一次
        if (qm.currentQuest != null) Refresh(qm.currentQuest);
        else SetStatus("正在生成任务…");   // LLM异步约2-3秒，此时游戏仍可操作(NFR2)
    }

    void OnDestroy()
    {
        var qm = QuestManager.Instance;
        if (qm == null) return;
        qm.OnQuestUpdated   -= Refresh;
        qm.OnQuestCompleted -= OnCompleted;
    }

    // ===================== 数据 → 界面 =====================

    void Refresh(Quest q)
    {
        current = q;
        statusText.gameObject.SetActive(false);

        titleText.text = q.title;
        descText.text  = q.description;

        // 叙事层给文字，机制层给数字 —— 在这里拼起来
        objText.text      = q.objectiveText;
        progressText.text = $"{q.currentCount} / {q.requiredCount}";
        progressText.color = bodyColor;
    }

    void OnCompleted(Quest q)
    {
        // 完成时把描述换成【叙事层】的完成语
        descText.text      = q.completionText;
        descText.color     = doneColor;
        objText.text       = "任务完成";
        objText.color      = doneColor;
        progressText.color = doneColor;
    }

    void SetStatus(string msg)
    {
        statusText.gameObject.SetActive(true);
        statusText.text = msg;
        titleText.text = "—";
        descText.text = "";
        objText.text = "";
        progressText.text = "";
    }

    // ===================== 界面构建 =====================

    void Build()
    {
        // --- Canvas ---
        var canvasGO = new GameObject("QuestLogCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;

        // --- 左侧垂直居中，无底板 ---
        var panel = MakeRect("QuestPanel", canvasGO.transform);
        panel.anchorMin = new Vector2(0f, 0.5f);
        panel.anchorMax = new Vector2(0f, 0.5f);
        panel.pivot     = new Vector2(0f, 0.5f);
        panel.anchoredPosition = new Vector2(leftMargin, 0f);
        panel.sizeDelta = new Vector2(panelWidth, 0f);

        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // --- 顶部横线（唯一的装饰）---
        MakeTopLine(panel);

        // --- 栏目名 + 叙事器徽标 ---
        var header = MakeRect("Header", panel);
        var hLayout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hLayout.childControlWidth = true; hLayout.childControlHeight = true;
        hLayout.childForceExpandWidth = true;
        header.gameObject.AddComponent<LayoutElement>().minHeight = 26;

        MakeText("Label", header, "任 务", 20, accentColor, TextAnchor.MiddleLeft, FontStyle.Bold);
        badgeText = MakeText("Badge", header, "", 16, new Color(0.75f, 0.75f, 0.75f), TextAnchor.MiddleRight);

        // --- 标题（叙事层）---
        titleText = MakeText("Title", panel, "—", 24, titleColor, TextAnchor.UpperLeft, FontStyle.Bold);

        // --- 背景描述（叙事层）---
        descText = MakeText("Desc", panel, "", 17, bodyColor, TextAnchor.UpperLeft);

        // --- 生成中提示 ---
        statusText = MakeText("Status", panel, "", 17, new Color(0.95f, 0.9f, 0.6f), TextAnchor.UpperLeft);
        statusText.gameObject.SetActive(false);

        // --- 目标行：叙事文字 + 机制计数 ---
        var objRow = MakeRect("ObjRow", panel);
        var oLayout = objRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        oLayout.childControlWidth = true; oLayout.childControlHeight = true;
        oLayout.childForceExpandWidth = true;
        objRow.gameObject.AddComponent<LayoutElement>().minHeight = 26;

        objText      = MakeText("Objective", objRow, "", 17, accentColor, TextAnchor.MiddleLeft);
        progressText = MakeText("Progress",  objRow, "", 18, bodyColor,  TextAnchor.MiddleRight, FontStyle.Bold);
        progressText.gameObject.AddComponent<LayoutElement>().preferredWidth = 80;
    }

    RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    Text MakeText(string name, Transform parent, string content, int size,
                  Color color, TextAnchor anchor, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = GetFont();
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.fontStyle = style;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;

        // 无底板 → 靠描边保证在明亮场景上也读得清
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1.6f, -1.6f);

        var fit = go.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return t;
    }

    // 顶部横线：无底板时用来界定任务栏的范围
    void MakeTopLine(Transform parent)
    {
        var line = MakeRect("TopLine", parent);
        var img = line.gameObject.AddComponent<Image>();
        img.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.9f);
        line.gameObject.AddComponent<LayoutElement>().minHeight = 2;
        // 线自身也加一点暗影，避免在纯白背景上刺眼/糊掉
        var sh = line.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.5f);
        sh.effectDistance = new Vector2(1f, -1f);
    }
}
