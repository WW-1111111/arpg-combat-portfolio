using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 战斗 HUD：玩家血条、Boss 血条、敌人头顶血条，三者合一。
//
// 与 QuestLogUI 相同的做法：全部用代码在运行时构建，不需要在编辑器里挂任何东西、
// 也不需要预制体。进场景自动出现；场景里没有 Player 标签则自动退出，不干扰批量评估。
//
// 视觉：血量瞬时下降，其后跟一条白色"残影条"缓慢追上 —— 这样在录像里能清楚看到
//      每一次命中打掉了多少，比单纯一条会动的绿条可读得多。
public class CombatHUD : MonoBehaviour
{
    public static CombatHUD Instance { get; private set; }

    [Header("配色")]
    public Color playerFill = new Color(0.45f, 0.85f, 0.45f);
    public Color enemyFill  = new Color(0.90f, 0.35f, 0.30f);
    public Color bossFill   = new Color(0.95f, 0.25f, 0.25f);
    public Color chipColor  = new Color(1f, 1f, 1f, 0.75f);        // 残影
    public Color backColor  = new Color(0f, 0f, 0f, 0.55f);

    [Header("残影追赶速度（每秒占比）")]
    public float chipSpeed = 0.6f;

    static Font uiFont;
    Canvas canvas;
    RectTransform canvasRect;
    Camera cam;

    Bar playerBar, bossBar;
    Text playerNum, bossName;
    readonly Dictionary<Health, Bar> enemyBars = new Dictionary<Health, Bar>();
    float nextScan;

    // 进场景自动创建，无需任何编辑器操作
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (GameObject.FindGameObjectWithTag("Player") == null) return;   // 非战斗场景不创建
        if (FindAnyObjectByType<CombatHUD>() != null) return;
        new GameObject("CombatHUD", typeof(CombatHUD));
    }

    // ===================== 生命周期 =====================

    void Awake()
    {
        Instance = this;      // 每次进 Play 都重新赋值，不需要静态重置
        Build();
    }

    void Start()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        var ph = player != null ? player.GetComponent<Health>() : null;
        if (ph != null)
        {
            ph.OnHealthChanged += OnPlayerHealth;
            OnPlayerHealth(ph);
        }
        else
        {
            playerBar.Root.gameObject.SetActive(false);
        }
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;      // 顿帧时血条也要继续追赶
        playerBar.Tick(dt, chipSpeed);
        bossBar.Tick(dt, chipSpeed);

        if (Time.time >= nextScan) { nextScan = Time.time + 0.4f; ScanEnemies(); }
        UpdateEnemyBars(dt);
    }

    // ===================== 玩家 =====================

    void OnPlayerHealth(Health h)
    {
        playerBar.Set(h.Normalized);
        playerNum.text = $"{Mathf.Max(0f, h.Current):0} / {h.Max:0}";
    }

    // ===================== Boss =====================

    /// <summary>Boss 进入战斗时调用，顶部出现大血条。BossController 负责调它。</summary>
    public void ShowBoss(Health boss, string displayName)
    {
        if (boss == null) return;
        bossName.text = displayName;
        bossBar.Root.gameObject.SetActive(true);
        bossBar.SetImmediate(boss.Normalized);
        boss.OnHealthChanged += h => bossBar.Set(h.Normalized);
        boss.OnDied          += _ => HideBoss();
    }

    public void HideBoss() => bossBar.Root.gameObject.SetActive(false);

    // ===================== 敌人头顶血条 =====================

    // 按标签扫描场景里的敌人，自动增删血条 —— 敌人不需要挂任何脚本
    void ScanEnemies()
    {
        foreach (var h in FindObjectsByType<Health>(FindObjectsSortMode.None))
        {
            if (h == null || h.IsDead) continue;
            if (!h.CompareTag("Enemy")) continue;          // 只给敌人加；玩家/Boss 有自己的条
            if (enemyBars.ContainsKey(h)) continue;

            var bar = MakeBar(canvasRect, 90, 8, enemyFill);
            bar.Root.anchorMin = bar.Root.anchorMax = new Vector2(0.5f, 0.5f);
            bar.Root.pivot = new Vector2(0.5f, 0.5f);
            bar.SetImmediate(h.Normalized);
            enemyBars[h] = bar;

            h.OnHealthChanged += hh => { if (enemyBars.TryGetValue(hh, out var b)) b.Set(hh.Normalized); };
            h.OnDied          += RemoveEnemyBar;
        }
    }

    void RemoveEnemyBar(Health h)
    {
        if (h != null && enemyBars.TryGetValue(h, out var bar))
        {
            if (bar.Root != null) Destroy(bar.Root.gameObject);
            enemyBars.Remove(h);
        }
    }

    // 把敌人的世界坐标换算成屏幕坐标，血条跟着头顶跑
    void UpdateEnemyBars(float dt)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        List<Health> gone = null;
        foreach (var kv in enemyBars)
        {
            Health h = kv.Key;
            Bar bar = kv.Value;
            if (h == null || h.IsDead)                     // 敌人已销毁 → 收掉血条
            {
                (gone ??= new List<Health>()).Add(h);
                continue;
            }

            bar.Tick(dt, chipSpeed);

            Vector3 world = h.transform.position + Vector3.up * HeadHeight(h);
            Vector3 sp = cam.WorldToScreenPoint(world);
            bool visible = sp.z > 0f;                      // 在相机背后就藏起来
            bar.Root.gameObject.SetActive(visible);
            if (!visible) continue;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, sp, null, out Vector2 local);
            bar.Root.anchoredPosition = local;
        }

        if (gone != null)
            foreach (var h in gone)
            {
                if (h != null && enemyBars.TryGetValue(h, out var b) && b.Root != null)
                    Destroy(b.Root.gameObject);
                enemyBars.Remove(h);
            }
    }

    // 头顶高度：优先用碰撞体顶部，没有就退回一个固定值
    static float HeadHeight(Health h)
    {
        var col = h.GetComponent<Collider>();
        if (col != null) return col.bounds.size.y * 0.5f + 0.45f;
        return 2.2f;
    }

    // ===================== 界面构建 =====================

    void Build()
    {
        var canvasGO = new GameObject("CombatHUDCanvas",
            typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1;                    // 压在任务栏底下，别遮住文字
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        canvasRect = canvasGO.GetComponent<RectTransform>();

        // --- 玩家：左下角 ---
        playerBar = MakeBar(canvasRect, 420, 26, playerFill);
        playerBar.Root.anchorMin = playerBar.Root.anchorMax = new Vector2(0f, 0f);
        playerBar.Root.pivot = new Vector2(0f, 0f);
        playerBar.Root.anchoredPosition = new Vector2(48f, 48f);
        MakeText("PlayerLabel", playerBar.Root, "影 子", 17,
                 new Color(0.9f, 0.9f, 0.9f), TextAnchor.LowerLeft)
            .rectTransform.anchoredPosition = new Vector2(2f, 30f);
        playerNum = MakeText("PlayerNum", playerBar.Root, "", 16,
                             new Color(0.92f, 0.92f, 0.92f), TextAnchor.LowerRight);
        playerNum.rectTransform.anchoredPosition = new Vector2(-2f, 30f);

        // --- Boss：顶部居中，默认隐藏 ---
        bossBar = MakeBar(canvasRect, 900, 22, bossFill);
        bossBar.Root.anchorMin = bossBar.Root.anchorMax = new Vector2(0.5f, 1f);
        bossBar.Root.pivot = new Vector2(0.5f, 1f);
        bossBar.Root.anchoredPosition = new Vector2(0f, -64f);
        bossName = MakeText("BossName", bossBar.Root, "", 24,
                            new Color(1f, 0.86f, 0.72f), TextAnchor.LowerCenter, FontStyle.Bold);
        bossName.rectTransform.anchoredPosition = new Vector2(0f, 26f);
        bossBar.Root.gameObject.SetActive(false);
    }

    // 一条血条 = 底板 + 残影 + 实血。用锚点做填充，不需要精灵图。
    Bar MakeBar(RectTransform parent, float width, float height, Color fillColor)
    {
        var root = MakeRect("Bar", parent);
        root.sizeDelta = new Vector2(width, height);

        var bg = root.gameObject.AddComponent<Image>();
        bg.color = backColor;

        var chip = MakeRect("Chip", root);
        Stretch(chip);
        chip.gameObject.AddComponent<Image>().color = chipColor;

        var fill = MakeRect("Fill", root);
        Stretch(fill);
        fill.gameObject.AddComponent<Image>().color = fillColor;

        return new Bar { Root = root, Chip = chip, Fill = fill };
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;

        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var outline = go.AddComponent<Outline>();     // 亮场景上也读得清
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    static Font GetFont()
    {
        if (uiFont == null)
            uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "SimSun", "Arial" }, 18);
        return uiFont;
    }

    // 一条血条的三层结构 + 残影追赶逻辑
    class Bar
    {
        public RectTransform Root, Chip, Fill;
        float chip = 1f;

        public void Set(float t)
        {
            t = Mathf.Clamp01(t);
            SetAnchor(Fill, t);
            if (t > chip) { chip = t; SetAnchor(Chip, t); }   // 回血时残影直接跟上
        }

        public void SetImmediate(float t)
        {
            t = Mathf.Clamp01(t);
            chip = t;
            SetAnchor(Fill, t);
            SetAnchor(Chip, t);
        }

        // 残影每帧向实血追一点，形成"掉了多少"的可读反馈
        public void Tick(float dt, float speed)
        {
            if (Fill == null || Chip == null) return;
            float target = Fill.anchorMax.x;
            if (chip <= target) return;
            chip = Mathf.Max(target, chip - speed * dt);
            SetAnchor(Chip, chip);
        }

        static void SetAnchor(RectTransform rt, float t)
        {
            if (rt == null) return;
            rt.anchorMax = new Vector2(t, 1f);
        }
    }
}
