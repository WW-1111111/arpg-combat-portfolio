# -*- coding: utf-8 -*-
"""
生成人工校准盲评表（CA1 要求的 calibration subset）。

设计：
  - 分层抽样：三档叙事器 × 三种任务类型各若干，保证覆盖面
  - 盲评：表里不含来源标签，顺序随机；答案对照单独存到 _key 文件，标注者看不到
  - 标注者使用与 LLM 裁判**完全相同**的评分表 → 两者可直接比对

用法：python make_calibration.py [--n 25]
输出：results/calibration_sheet.md   （给人看，不含来源）
     results/calibration_key.json   （对照表，标注完成前不要看）
"""
import os, sys, json, random, argparse
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "..", "EvalLogs", "batch_generations.jsonl")
OUT_DIR = os.path.join(HERE, "results")
SEED = 424242

ARMS = ["Template", "TemplateRandom", "LLM"]
TYPES = ["DefeatCount", "Fetch", "Boss"]

RUBRIC_SHORT = """## 评分表（与 LLM 裁判使用的完全一致）

对每段叙事，按四个维度各打 **1-5 分**：

| 维度 | 5 分 | 3 分 | 1 分 |
|---|---|---|---|
| **覆盖** 情境要素覆盖 | 地点/对象/时机三个都自然出现 | 出现两个 | 一个都没有 |
| **整合** 有机整合 | 要素相互关联、有场景感与因果 | 要素都在但基本是并列罗列 | 生硬拼接或与情境无关 |
| **世界观** 世界观契合 | 明确借用了设定的特有元素或因果 | 语气契合但没用到设定特有内容 | 与设定无关 |
| **语言** 语言自然度 | 自然流畅 | 可读但有瑕疵 | 明显不通顺 |

**重要规则**：
- 生成方被要求**不写死具体数字**，因此**文本没提数量不算缺陷，不要因此扣分**。
- 改写或近义表达同样算「覆盖」（例如情境是「断崖边的栈道」，文本写「断崖栈道」→ 算覆盖）。
- 只看文本本身，不要猜它是人写的还是模型写的。
"""


def narrative_block(r):
    return (f"> **{r['title']}**\n>\n"
            f"> {r['description']}\n>\n"
            f"> 目标：{r['objectiveText']}\n>\n"
            f"> 完成：{r['completionText']}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--n", type=int, default=25)
    args = ap.parse_args()
    os.makedirs(OUT_DIR, exist_ok=True)

    rows = [json.loads(l) for l in open(DATA, encoding="utf-8") if l.strip()]
    rnd = random.Random(SEED)

    # 分层抽样：9 个 (档 × 类型) 单元均匀取，凑够 n 条
    picked, per_cell = [], max(1, args.n // 9)
    for arm in ARMS:
        for qt in TYPES:
            cell = [r for r in rows if r["narrator"] == arm and r["questType"] == qt]
            picked += rnd.sample(cell, min(per_cell, len(cell)))
    rest = [r for r in rows if r not in picked]
    picked += rnd.sample(rest, max(0, args.n - len(picked)))
    rnd.shuffle(picked)                      # 打乱顺序，杜绝按档位分组的线索

    world = picked[0]["worldContext"]
    lines = ["# 人工校准盲评表\n",
             f"共 **{len(picked)}** 段叙事。请按评分表逐条打分——",
             "**这些文本混合了三种不同的生成方式，但表里不告诉你哪条是哪种，请只依据文本本身评分。**\n",
             "## 世界背景（所有条目共用）\n", f"> {world}\n", RUBRIC_SHORT,
             "\n---\n\n# 待评条目\n"]

    key = []
    for i, r in enumerate(picked, 1):
        mech = {"DefeatCount": f"击败 {r['requiredCount']} 名敌人",
                "Fetch": f"收集 {r['requiredCount']} 件物品",
                "Boss": "击败一名头目"}[r["questType"]]
        lines += [f"### 第 {i} 条\n",
                  f"- 任务机制：{mech}",
                  f"- 情境：地点「{r['location']}」／对象「{r['targetFlavour']}」／时机「{r['timeOfDay']}」\n",
                  narrative_block(r), "\n"]
        key.append({"no": i, "rid": f"{r['narrator']}|{r['questType']}|{r['instanceIndex']}"})

    lines += ["\n---\n\n## 你的打分（填完把这一段发回即可）\n", "```"]
    for i in range(1, len(picked) + 1):
        lines.append(f"#{i:<3} 覆盖=_  整合=_  世界观=_  语言=_")
    lines += ["```\n"]

    open(os.path.join(OUT_DIR, "calibration_sheet.md"), "w", encoding="utf-8").write("\n".join(lines))
    json.dump(key, open(os.path.join(OUT_DIR, "calibration_key.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)

    # 供网页标注工具使用的条目数据（同样不含来源标签）
    items = []
    for i, r in enumerate(picked, 1):
        mech = {"DefeatCount": f"击败 {r['requiredCount']} 名敌人",
                "Fetch": f"收集 {r['requiredCount']} 件物品",
                "Boss": "击败一名头目"}[r["questType"]]
        items.append({"no": i, "mech": mech, "loc": r["location"],
                      "target": r["targetFlavour"], "time": r["timeOfDay"],
                      "title": r["title"], "desc": r["description"],
                      "obj": r["objectiveText"], "done": r["completionText"]})
    json.dump({"world": world, "items": items},
              open(os.path.join(OUT_DIR, "calibration_items.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)

    print(f"已生成 {len(picked)} 条盲评表 → results/calibration_sheet.md")
    print("网页用数据 → results/calibration_items.json")
    print("对照表 → results/calibration_key.json（标注完成前不要打开）")


if __name__ == "__main__":
    main()
