# -*- coding: utf-8 -*-
"""
裁判结果分析 —— 产出论文第二部分核心结果（情境契合的「质量」维度）。

自动约束检查只能数「提没提到」，数不出「用得好不好」；
本脚本处理 LLM-as-judge 的四维打分，回答后者。

若 results/calibration_human.json 存在（人工标注结果），
额外计算人机一致性（Spearman / 逐维一致率 / 二次加权 kappa）。

用法：python analyze_judge.py
输出：results/judge_summary.md、results/judge.png
"""
import os, sys, json, statistics as st
from collections import defaultdict
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

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(HERE, "results")
JUDGE = os.path.join(OUT_DIR, "judge_results.jsonl")
HUMAN = os.path.join(OUT_DIR, "calibration_human.json")
KEY = os.path.join(OUT_DIR, "calibration_key.json")

DIMS = [("element_coverage", "覆盖"), ("integration", "整合"),
        ("world_fit", "世界观"), ("fluency", "语言")]
ARMS = [("Template", "固定模板"), ("TemplateRandom", "随机模板"), ("LLM", "LLM")]


def quadratic_weighted_kappa(a, b, lo=1, hi=5):
    """二次加权 kappa —— 有序评分的标准一致性指标（相差越远惩罚越重）。"""
    n = hi - lo + 1
    O = np.zeros((n, n))
    for x, y in zip(a, b):
        O[x - lo, y - lo] += 1
    ha = np.bincount([x - lo for x in a], minlength=n)
    hb = np.bincount([y - lo for y in b], minlength=n)
    E = np.outer(ha, hb) / len(a)
    W = np.array([[(i - j) ** 2 / (n - 1) ** 2 for j in range(n)] for i in range(n)])
    denom = (W * E).sum()
    return 1 - (W * O).sum() / denom if denom else float("nan")


def main():
    rows = [json.loads(l) for l in open(JUDGE, encoding="utf-8") if l.strip()]
    by = defaultdict(dict)
    for r in rows:
        arm, qt, idx = r["rid"].split("|")
        by[(qt, int(idx))][arm] = r
    keys = sorted(by.keys())

    L = ["# 裁判评分结果（LLM-as-judge）\n",
         f"语料 {len(rows)} 条，配对 {len(keys)} 组。裁判 claude-opus-5，盲评。\n",
         "\n## 1. 四维得分（1-5 分）\n",
         "| 叙事器 | 覆盖 | 整合 | 世界观 | 语言 | 四维均值 | 机制矛盾率 |",
         "|---|---|---|---|---|---|---|"]
    print(f"{'叙事器':<14}{'覆盖':>8}{'整合':>8}{'世界观':>9}{'语言':>8}{'均值':>8}{'矛盾':>8}")
    print("-" * 62)
    means = {}
    for a, cn in ARMS:
        sub = [r for r in rows if r["rid"].startswith(a + "|")]
        m = [st.mean(r[d] for r in sub) for d, _ in DIMS]
        means[a] = m
        contra = sum(r["contradicts_mechanic"] for r in sub) / len(sub) * 100
        print(f"{cn:<14}{m[0]:>8.2f}{m[1]:>8.2f}{m[2]:>9.2f}{m[3]:>8.2f}"
              f"{st.mean(m):>8.2f}{contra:>7.1f}%")
        L.append(f"| {cn} | {m[0]:.2f} | {m[1]:.2f} | {m[2]:.2f} | {m[3]:.2f} | "
                 f"{st.mean(m):.2f} | {contra:.1f}% |")

    L.append("\n## 2. 配对 Wilcoxon 符号秩检验\n")
    L.append("| 对比 | 维度 | LLM | 基线 | p |")
    L.append("|---|---|---|---|---|")
    print("\n配对 Wilcoxon:")
    for base, bcn in [("TemplateRandom", "随机模板"), ("Template", "固定模板")]:
        print(f"  — LLM vs {bcn}")
        for d, dcn in DIMS:
            a = [by[k]["LLM"][d] for k in keys]
            b = [by[k][base][d] for k in keys]
            if all(x == y for x, y in zip(a, b)):
                print(f"    {dcn:<8} 完全相同（均 {st.mean(a):.2f}）")
                L.append(f"| LLM vs {bcn} | {dcn} | {st.mean(a):.2f} | "
                         f"{st.mean(b):.2f} | 完全相同，不检验 |")
                continue
            w, p = stats.wilcoxon(a, b)
            print(f"    {dcn:<8} {st.mean(a):.2f} vs {st.mean(b):.2f}  p={p:.2e}")
            L.append(f"| LLM vs {bcn} | {dcn} | {st.mean(a):.2f} | {st.mean(b):.2f} | {p:.2e} |")

    # ---- 人机一致性（人工标注完成后自动启用）----
    if os.path.exists(HUMAN) and os.path.exists(KEY):
        key = {k["no"]: k["rid"] for k in json.load(open(KEY, encoding="utf-8"))}
        human = json.load(open(HUMAN, encoding="utf-8"))     # {"1": {dim: score}, ...}
        jd = {r["rid"]: r for r in rows}
        print("\n人机一致性:")
        L += ["\n## 3. 人机一致性（校准子集）\n",
              "| 维度 | Spearman ρ | p | 完全一致率 | 二次加权 kappa |", "|---|---|---|---|---|"]
        for d, dcn in DIMS:
            hs, js = [], []
            for no, sc in human.items():
                rid = key[int(no)]
                if rid in jd and d in sc:
                    hs.append(int(sc[d])); js.append(jd[rid][d])
            if len(hs) < 3 or len(set(hs)) < 2:
                print(f"  {dcn}: 样本不足或人工打分无变异，跳过"); continue
            rho, p = stats.spearmanr(hs, js)
            exact = sum(x == y for x, y in zip(hs, js)) / len(hs) * 100
            k = quadratic_weighted_kappa(hs, js)
            print(f"  {dcn:<8} ρ={rho:.3f} (p={p:.2e})  完全一致 {exact:.0f}%  kappa={k:.3f}")
            L.append(f"| {dcn} | {rho:.3f} | {p:.2e} | {exact:.0f}% | {k:.3f} |")
    else:
        L.append("\n## 3. 人机一致性\n\n待人工标注完成（results/calibration_human.json）后生成。\n")
        print("\n（人工标注尚未提供，跳过一致性计算）")

    open(os.path.join(OUT_DIR, "judge_summary.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")

    # ---- 图 ----
    fig, ax = plt.subplots(1, 2, figsize=(13, 4.6))
    x = np.arange(len(DIMS)); w = 0.26
    for i, (a, cn) in enumerate(ARMS):
        ax[0].bar(x + (i - 1) * w, means[a], w, label=cn)
    ax[0].set_xticks(x); ax[0].set_xticklabels([c for _, c in DIMS])
    ax[0].set_ylim(0, 5.4); ax[0].set_ylabel("裁判评分 (1-5)")
    ax[0].set_title("四维评分：覆盖打平，整合/世界观/语言分化"); ax[0].legend()
    for i, (a, cn) in enumerate(ARMS):
        for xi, v in zip(x + (i - 1) * w, means[a]):
            ax[0].text(xi, v + 0.08, f"{v:.2f}", ha="center", fontsize=7)

    data = [[st.mean(r[d] for d, _ in DIMS) for r in rows if r["rid"].startswith(a + "|")]
            for a, _ in ARMS]
    ax[1].boxplot(data, tick_labels=[c for _, c in ARMS])
    ax[1].set_ylabel("四维均值"); ax[1].set_title("总体质量分布")
    plt.tight_layout(); plt.savefig(os.path.join(OUT_DIR, "judge.png"), dpi=150)
    print(f"\n→ {OUT_DIR}\\  (judge_summary.md / judge.png)")


if __name__ == "__main__":
    main()
