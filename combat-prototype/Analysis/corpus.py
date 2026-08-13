# -*- coding: utf-8 -*-
"""
语料加载与完整性守卫。

起因是 2026-08-13 发现的一次真实事故：BatchRunner 每次运行都往同一个
batch_generations.jsonl 追加，两次中途中断的运行让文件从 270 条涨到 311 条
（多出的 41 条全是 DefeatCount，且与正式语料的实例重复）。
而 analyze.py / judge.py 都是整份读取、不按 runId 过滤——重跑会得出与
results.md 对不上的数字，且不报任何错。

所以语料加载一律走这里：混入多次运行时直接退出，而不是静默地算错。
宁可跑不动，也不要跑出一个看起来正常的错数字。
"""
import json
import sys
from collections import Counter


def load_corpus(path, expect_per_cell=None):
    """读取批量语料，并在结构不合法时直接退出。

    检查三件事：
      1. 只能来自一次批量运行（单一 runId）——否则不可复现，违反 NFR5
      2. 每个 (叙事器 × 任务类型) 单元条数相同——否则配对检验失效
      3. 若给了 expect_per_cell，还要与预期一致
    """
    rows = [json.loads(l) for l in open(path, encoding="utf-8") if l.strip()]
    if not rows:
        sys.exit(f"\n❌ 语料为空：{path}\n")

    runs = Counter(r.get("runId") for r in rows)
    if len(runs) > 1:
        detail = "\n".join(f"      {rid}: {n} 条" for rid, n in sorted(runs.items()))
        sys.exit(
            f"\n❌ 语料混入了 {len(runs)} 次运行的记录，拒绝分析：\n"
            f"   {path}\n{detail}\n\n"
            "   一份语料必须来自一次完整的批量运行，否则结果不可复现（NFR5）。\n"
            "   处理：保留正式那次的 runId，其余记录移到单独文件归档后重跑。\n"
        )

    cells = Counter((r["narrator"], r["questType"]) for r in rows)
    sizes = set(cells.values())
    if len(sizes) != 1:
        detail = "\n".join(f"      {a}/{t}: {n} 条" for (a, t), n in sorted(cells.items()))
        sys.exit(
            "\n❌ 各 (叙事器 × 任务类型) 单元条数不一致，配对检验会失效：\n"
            f"{detail}\n"
        )

    per_cell = sizes.pop()
    if expect_per_cell is not None and per_cell != expect_per_cell:
        sys.exit(f"\n❌ 每单元 {per_cell} 条，与预期的 {expect_per_cell} 条不符。\n")

    print(f"语料校验通过：{len(rows)} 条 = {len(cells)} 个单元 × {per_cell} 个实例"
          f"（runId={next(iter(runs))}）\n")
    return rows
