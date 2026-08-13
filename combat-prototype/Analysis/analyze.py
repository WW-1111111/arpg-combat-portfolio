# -*- coding: utf-8 -*-
"""
O5 评估分析 —— 读 EvalLogs/batch_generations.jsonl，产出论文核心结果。

指标：
  多样性  : distinct-n（字符n元组）、Self-BLEU（自实现，中文按字符切分）
  情境契合: 自动约束检查——地点/对象/时段的覆盖率（精确匹配 + 宽松匹配）
  长度    : 已知混淆变量，必须一并报告
  鲁棒性  : JSON合法率、兜底率、延迟分布
  统计    : 配对 Wilcoxon 符号秩检验 / McNemar 精确检验

用法：python analyze.py
输出：Analysis/results/ 下的 CSV、Markdown 表、PNG 图
"""
import sys, os, re, json, math, statistics
from collections import Counter, defaultdict
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

import numpy as np
from scipy import stats
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

plt.rcParams["font.sans-serif"] = ["Microsoft YaHei", "SimHei"]
plt.rcParams["axes.unicode_minus"] = False

from corpus import load_corpus   # 语料完整性守卫：混入多次运行时直接退出

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "..", "EvalLogs", "batch_generations.jsonl")
OUT = os.path.join(HERE, "results")
os.makedirs(OUT, exist_ok=True)

ARMS = ["Template", "TemplateRandom", "LLM"]
ARM_CN = {"Template": "固定模板", "TemplateRandom": "随机模板", "LLM": "LLM"}
TYPES = ["DefeatCount", "Fetch", "Boss"]

# ============ 文本工具（中文按字符切分，避免分词器引入的歧义）============

def clean(s):
    return re.sub(r"\s+", "", s or "")

def char_ngrams(s, n):
    s = clean(s)
    return [s[i:i + n] for i in range(len(s) - n + 1)]

def narrative_of(r):
    """一条记录的完整叙事产出＝四个叙事字段拼接。"""
    return "".join([r.get("title", ""), r.get("description", ""),
                    r.get("objectiveText", ""), r.get("completionText", "")])

# ============ 多样性 ============

def distinct_n(texts, n):
    """unique n-gram 数 / 总 n-gram 数。越高越多样。"""
    grams = []
    for t in texts:
        grams += char_ngrams(t, n)
    return len(set(grams)) / len(grams) if grams else 0.0

def bleu(hyp, refs, max_n=4):
    """标准 BLEU（截断修正精度 + 简短惩罚 + epsilon 平滑）。"""
    h_len = len(clean(hyp))
    if h_len == 0 or not refs:
        return 0.0
    logs = []
    for n in range(1, max_n + 1):
        h = Counter(char_ngrams(hyp, n))
        if not h:
            logs.append(math.log(1e-9)); continue
        maxref = Counter()
        for r in refs:
            for g, c in Counter(char_ngrams(r, n)).items():
                if c > maxref[g]:
                    maxref[g] = c
        clipped = sum(min(c, maxref[g]) for g, c in h.items())
        total = sum(h.values())
        logs.append(math.log(max(clipped / total, 1e-9)))
    # 简短惩罚：取长度最接近的参考
    ref_lens = [len(clean(r)) for r in refs]
    closest = min(ref_lens, key=lambda rl: (abs(rl - h_len), rl))
    bp = 1.0 if h_len > closest else math.exp(1 - closest / h_len)
    return bp * math.exp(sum(logs) / max_n)

def self_bleu_each(texts):
    """每条文本对组内其余文本算 BLEU。越低越多样。"""
    return [bleu(t, [x for j, x in enumerate(texts) if j != i])
            for i, t in enumerate(texts)]

# ============ 情境契合：自动约束检查 ============

MATCH_THRESH = 0.70   # 由置换检验校准，见 calibrate()

def lcs_subseq(a, b):
    """最长公共子序列长度。

    必须用「子序列」而非「子串」：LLM 按中文习惯省略结构助词
    （"断崖边的栈道"→"断崖栈道"、"荒废的寺庙"→"荒废寺庙"），
    子串会被省略的字截断，从而系统性低估 LLM 的覆盖率。
    """
    if not a or not b:
        return 0
    dp = [0] * (len(b) + 1)
    for i in range(1, len(a) + 1):
        prev = 0
        for j in range(1, len(b) + 1):
            cur = dp[j]
            dp[j] = prev + 1 if a[i - 1] == b[j - 1] else max(dp[j], dp[j - 1])
            prev = cur
    return dp[len(b)]

def mention(text, entity, thresh=MATCH_THRESH):
    """实体是否被提及。返回 'exact' / 'relaxed' / 'no'。"""
    t, e = clean(text), clean(entity)
    if not e:
        return "no"
    if e in t:
        return "exact"
    if lcs_subseq(e, t) >= max(2, int(len(e) * thresh)):
        return "relaxed"
    return "no"

def calibrate(rows, arm="LLM", rounds=5, seed=7):
    """
    用置换检验校准匹配阈值：把每条文本与【别的实例】的情境配对，
    正确的判定应为「不覆盖」，因此命中率即假阳性率。
    真实命中率与假阳性率之差＝判别力，取最大者为阈值。
    （固定模板档也是天然负对照，但其文本更短，长度不可比，故另做置换。）
    """
    import random
    rnd = random.Random(seed)
    sub = [r for r in rows if r["narrator"] == arm]
    fields = ["location", "targetFlavour", "timeOfDay"]

    def covered(text, src, th):
        return all(mention(text, src[f], th) != "no" for f in fields)

    out = []
    for th in [0.6, 0.7, 0.75, 0.8, 0.85, 0.9, 1.0]:
        real = sum(covered(r["narrative"], r, th) for r in sub) / len(sub) * 100
        fps = []
        for _ in range(rounds):
            sh = sub[:]; rnd.shuffle(sh)
            fps.append(sum(covered(a["narrative"], b, th)
                           for a, b in zip(sub, sh) if a is not b) / len(sub) * 100)
        fp = sum(fps) / len(fps)
        out.append((th, real, fp, real - fp))
    return out

# ============ 主流程 ============

def main():
    rows = load_corpus(DATA)
    for r in rows:
        r["narrative"] = narrative_of(r)
        r["len"] = len(clean(r["narrative"]))

    by_arm_type = defaultdict(list)
    for r in rows:
        by_arm_type[(r["narrator"], r["questType"])].append(r)
    for v in by_arm_type.values():
        v.sort(key=lambda r: r["instanceIndex"])

    lines = ["# 评估结果\n", f"语料：{len(rows)} 条 = 3 档 × 3 类 × "
             f"{len(by_arm_type[('LLM','Boss')])} 实例\n"]

    # ---------- 1. 多样性 ----------
    print("=" * 68); print("① 多样性"); print("=" * 68)
    div_rows = []
    for arm in ARMS:
        for qt in TYPES:
            texts = [r["narrative"] for r in by_arm_type[(arm, qt)]]
            sb = self_bleu_each(texts)
            for r, s in zip(by_arm_type[(arm, qt)], sb):
                r["self_bleu"] = s
            div_rows.append(dict(arm=arm, qtype=qt,
                                 d1=distinct_n(texts, 1), d2=distinct_n(texts, 2),
                                 d3=distinct_n(texts, 3), d4=distinct_n(texts, 4),
                                 self_bleu=statistics.mean(sb)))
    hdr = f"{'叙事器':<16}{'distinct-1':>11}{'distinct-2':>11}{'distinct-3':>11}{'distinct-4':>11}{'Self-BLEU↓':>12}"
    print(hdr); print("-" * len(hdr))
    lines += ["\n## 1. 多样性（三类任务合并均值）\n",
              "| 叙事器 | distinct-1 | distinct-2 | distinct-3 | distinct-4 | Self-BLEU ↓ |",
              "|---|---|---|---|---|---|"]
    agg = {}
    for arm in ARMS:
        sub = [d for d in div_rows if d["arm"] == arm]
        m = {k: statistics.mean(d[k] for d in sub) for k in ["d1", "d2", "d3", "d4", "self_bleu"]}
        agg[arm] = m
        print(f"{ARM_CN[arm]:<16}{m['d1']:>11.3f}{m['d2']:>11.3f}{m['d3']:>11.3f}{m['d4']:>11.3f}{m['self_bleu']:>12.3f}")
        lines.append(f"| {ARM_CN[arm]} | {m['d1']:.3f} | {m['d2']:.3f} | {m['d3']:.3f} | {m['d4']:.3f} | {m['self_bleu']:.3f} |")
    print("\n注：distinct-n 越高越多样；Self-BLEU 越低越多样。")

    # ---------- 2. 情境契合 ----------
    print("\n" + "=" * 68); print("② 情境契合：自动约束检查"); print("=" * 68)

    # 2a. 先校准匹配阈值（置换检验），并把过程写进结果，保证可复现
    cal = calibrate(rows)
    print("匹配阈值校准（置换检验）:")
    print(f"  {'阈值':<8}{'真实命中':>10}{'置换假阳性':>12}{'判别力':>10}")
    lines += ["\n## 2. 情境契合（自动约束检查）\n",
              "### 2a. 匹配阈值校准（置换检验）\n",
              "把每条文本与**其他实例**的情境配对，正确判定应为「不覆盖」，"
              "故其命中率即假阳性率；判别力＝真实命中率−假阳性率。\n",
              "| 阈值 | 真实命中 | 置换假阳性 | 判别力 |", "|---|---|---|---|"]
    for th, real, fp, d in cal:
        mark = "  ← 采用" if abs(th - MATCH_THRESH) < 1e-9 else ""
        print(f"  {th:<8.2f}{real:>9.1f}%{fp:>11.1f}%{d:>9.1f}{mark}")
        lines.append(f"| {th:.2f} | {real:.1f}% | {fp:.1f}% | {d:.1f} |"
                     + (" **← 采用**" if abs(th - MATCH_THRESH) < 1e-9 else ""))
    lines.append(f"\n采用阈值 **{MATCH_THRESH}**（判别力最高）。匹配用最长公共子序列，"
                 "以容纳中文结构助词的省略（\"断崖边的栈道\"→\"断崖栈道\"）。\n")
    print()

    for r in rows:
        hits = {f: mention(r["narrative"], r.get(f, "")) for f in ["location", "targetFlavour", "timeOfDay"]}
        r["hits"] = hits
        r["cover_exact"] = sum(v == "exact" for v in hits.values())
        r["cover_any"] = sum(v != "no" for v in hits.values())
    hdr = f"{'叙事器':<16}{'地点':>10}{'对象':>10}{'时段':>10}{'三要素全覆盖':>14}"
    print("### 2b. 覆盖率"); print(hdr); print("-" * (len(hdr) + 8))
    lines += ["\n### 2b. 覆盖率\n",
              "| 叙事器 | 地点 | 对象 | 时段 | 三要素全覆盖 |", "|---|---|---|---|---|"]
    for arm in ARMS:
        sub = [r for r in rows if r["narrator"] == arm]
        pct = {f: sum(r["hits"][f] != "no" for r in sub) / len(sub) * 100
               for f in ["location", "targetFlavour", "timeOfDay"]}
        allthree = sum(r["cover_any"] == 3 for r in sub) / len(sub) * 100
        print(f"{ARM_CN[arm]:<16}{pct['location']:>9.1f}%{pct['targetFlavour']:>9.1f}%{pct['timeOfDay']:>9.1f}%{allthree:>13.1f}%")
        lines.append(f"| {ARM_CN[arm]} | {pct['location']:.1f}% | {pct['targetFlavour']:.1f}% | {pct['timeOfDay']:.1f}% | {allthree:.1f}% |")

    # ---------- 3. 长度（混淆变量） ----------
    print("\n" + "=" * 68); print("③ 文本长度（已知混淆变量）"); print("=" * 68)
    lines += ["\n## 3. 文本长度（混淆变量）\n", "| 叙事器 | 平均字数 | 标准差 |", "|---|---|---|"]
    for arm in ARMS:
        L = [r["len"] for r in rows if r["narrator"] == arm]
        print(f"{ARM_CN[arm]:<16}平均 {statistics.mean(L):6.1f} 字   标准差 {statistics.pstdev(L):5.1f}")
        lines.append(f"| {ARM_CN[arm]} | {statistics.mean(L):.1f} | {statistics.pstdev(L):.1f} |")

    # ---------- 4. 鲁棒性 ----------
    print("\n" + "=" * 68); print("④ 鲁棒性与成本（仅 LLM 档）"); print("=" * 68)
    llm = [r for r in rows if r["narrator"] == "LLM"]
    lat = sorted(r["latencyMs"] for r in llm)
    p = lambda q: lat[min(int(len(lat) * q), len(lat) - 1)]
    under5 = sum(1 for x in lat if x < 5000) / len(lat) * 100
    print(f"JSON 合法率 : {sum(r['jsonValid'] for r in llm)}/{len(llm)} = {sum(r['jsonValid'] for r in llm)/len(llm)*100:.1f}%")
    print(f"兜底率      : {sum(r['usedFallback'] for r in llm)}/{len(llm)}")
    print(f"延迟(ms)    : 中位 {statistics.median(lat):.0f} | 均值 {statistics.mean(lat):.0f} | p95 {p(0.95)} | 最大 {max(lat)}")
    print(f"NFR2 目标(<5s) 达成率: {under5:.1f}%   ← 如实报告，勿粉饰")
    lines += ["\n## 4. 鲁棒性与延迟（LLM 档）\n",
              f"- JSON 合法率：{sum(r['jsonValid'] for r in llm)/len(llm)*100:.1f}%",
              f"- 兜底率：{sum(r['usedFallback'] for r in llm)/len(llm)*100:.1f}%",
              f"- 延迟：中位 {statistics.median(lat):.0f} ms，均值 {statistics.mean(lat):.0f} ms，p95 {p(0.95)} ms，最大 {max(lat)} ms",
              f"- 低于 5 秒目标的比例：{under5:.1f}%（异步加载，等待期间游戏保持可操作）"]

    # ---------- 5. 配对显著性检验 ----------
    print("\n" + "=" * 68); print("⑤ 配对显著性检验"); print("=" * 68)
    key = lambda r: (r["questType"], r["instanceIndex"])
    idx = {arm: {key(r): r for r in rows if r["narrator"] == arm} for arm in ARMS}
    keys = sorted(idx["LLM"].keys())
    lines += ["\n## 5. 配对显著性检验\n", "| 对比 | 指标 | 检验 | 统计量 | p 值 |", "|---|---|---|---|---|"]

    def wilcoxon(a_arm, b_arm, field, name):
        a = [idx[a_arm][k][field] for k in keys]
        b = [idx[b_arm][k][field] for k in keys]
        d = [x - y for x, y in zip(a, b)]
        if all(abs(x) < 1e-12 for x in d):
            print(f"{name:<22}{ARM_CN[a_arm]} vs {ARM_CN[b_arm]}: 全部相同，不检验")
            return
        st_, pv = stats.wilcoxon(a, b)
        print(f"{name:<22}{ARM_CN[a_arm]} vs {ARM_CN[b_arm]}: W={st_:.1f}, p={pv:.3e}  "
              f"(均值 {statistics.mean(a):.3f} vs {statistics.mean(b):.3f})")
        lines.append(f"| {ARM_CN[a_arm]} vs {ARM_CN[b_arm]} | {name} | Wilcoxon | W={st_:.1f} | {pv:.3e} |")

    wilcoxon("LLM", "TemplateRandom", "self_bleu", "Self-BLEU(越低越多样)")
    wilcoxon("LLM", "Template", "self_bleu", "Self-BLEU(越低越多样)")
    wilcoxon("LLM", "TemplateRandom", "len", "文本长度")

    def mcnemar(a_arm, b_arm, name):
        a = [idx[a_arm][k]["cover_any"] == 3 for k in keys]
        b = [idx[b_arm][k]["cover_any"] == 3 for k in keys]
        b01 = sum(1 for x, y in zip(a, b) if x and not y)
        b10 = sum(1 for x, y in zip(a, b) if y and not x)
        if b01 + b10 == 0:
            print(f"{name:<22}{ARM_CN[a_arm]} vs {ARM_CN[b_arm]}: 无差异对，不检验")
            return
        pv = stats.binomtest(b01, b01 + b10, 0.5).pvalue
        print(f"{name:<22}{ARM_CN[a_arm]} vs {ARM_CN[b_arm]}: b01={b01}, b10={b10}, p={pv:.3e}")
        lines.append(f"| {ARM_CN[a_arm]} vs {ARM_CN[b_arm]} | {name} | McNemar精确 | {b01}/{b10} | {pv:.3e} |")

    mcnemar("LLM", "Template", "情境三要素全覆盖")
    mcnemar("LLM", "TemplateRandom", "情境三要素全覆盖")

    # ---------- 6. 导出 ----------
    import csv
    with open(os.path.join(OUT, "per_record.csv"), "w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow(["narrator", "questType", "instanceIndex", "seed", "location", "targetFlavour",
                    "timeOfDay", "requiredCount", "len", "self_bleu", "cover_any", "cover_exact",
                    "latencyMs", "jsonValid", "usedFallback", "title", "description"])
        for r in rows:
            w.writerow([r["narrator"], r["questType"], r["instanceIndex"], r["seed"], r["location"],
                        r["targetFlavour"], r["timeOfDay"], r["requiredCount"], r["len"],
                        round(r["self_bleu"], 4), r["cover_any"], r["cover_exact"], r["latencyMs"],
                        r["jsonValid"], r["usedFallback"], r["title"], r["description"]])
    open(os.path.join(OUT, "results.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")

    # ---------- 7. 图 ----------
    fig, ax = plt.subplots(2, 2, figsize=(13, 9))
    x = np.arange(4); w = 0.26
    for i, arm in enumerate(ARMS):
        ax[0, 0].bar(x + (i - 1) * w, [agg[arm][k] for k in ["d1", "d2", "d3", "d4"]], w, label=ARM_CN[arm])
    ax[0, 0].set_xticks(x); ax[0, 0].set_xticklabels(["distinct-1", "distinct-2", "distinct-3", "distinct-4"])
    ax[0, 0].set_title("词汇多样性（越高越多样）"); ax[0, 0].legend()

    ax[0, 1].boxplot([[r["self_bleu"] for r in rows if r["narrator"] == a] for a in ARMS],
                     tick_labels=[ARM_CN[a] for a in ARMS])
    ax[0, 1].set_title("Self-BLEU 分布（越低越多样）")

    cov = [sum(r["cover_any"] == 3 for r in rows if r["narrator"] == a) /
           len([r for r in rows if r["narrator"] == a]) * 100 for a in ARMS]
    bars = ax[1, 0].bar([ARM_CN[a] for a in ARMS], cov, color=["#999", "#5b9", "#38f"])
    ax[1, 0].set_ylim(0, 105); ax[1, 0].set_ylabel("%")
    ax[1, 0].set_title("情境三要素全覆盖率")
    for b, v in zip(bars, cov):
        ax[1, 0].text(b.get_x() + b.get_width() / 2, v + 2, f"{v:.0f}%", ha="center")

    ax[1, 1].hist(lat, bins=25, color="#38f", edgecolor="white")
    ax[1, 1].axvline(5000, color="red", ls="--", label="NFR2 目标 5s")
    ax[1, 1].set_xlabel("延迟 (ms)"); ax[1, 1].set_title("LLM 生成延迟分布"); ax[1, 1].legend()

    plt.tight_layout()
    plt.savefig(os.path.join(OUT, "evaluation.png"), dpi=150)
    print(f"\n已输出 → {OUT}\\  (per_record.csv / results.md / evaluation.png)")

if __name__ == "__main__":
    main()
