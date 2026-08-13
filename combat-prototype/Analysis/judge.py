# -*- coding: utf-8 -*-
"""
LLM-as-judge —— 对 270 条叙事做盲评打分。

方法学要点（写论文时照这里描述）：
  1) 不同模型家族：生成用 DeepSeek，评审用 Anthropic Claude（CA1 要求，避免自评偏袒）
  2) 盲评：裁判只看到「世界背景 + 机制 + 情境 + 一段叙事」，
          绝不知道该条出自哪一档；且整体顺序被打乱，杜绝位置线索
  3) 结构化输出：output_config.format 强制 JSON schema，避免解析失败
  4) 断点续跑：结果逐条追加写盘，中断后重跑会自动跳过已完成的
  5) 可复现性：Claude Opus 5 不再接受 temperature/top_p（传了会 400），
     因此无法用 temperature=0 声称确定性。改用**重测稳定性检验**：
     对随机子集二次评分并报告一致率（--stability N），这比声称确定性更诚实。

用法：python judge.py                # 全量 270 条（约 15 分钟）
     python judge.py --limit 6      # 小样本试跑
     python judge.py --stability 20 # 对已评过的随机 20 条重测，报告一致率
输出：results/judge_results.jsonl
"""
import os, sys, json, time, random, argparse, threading
from concurrent.futures import ThreadPoolExecutor
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass
import anthropic

from corpus import load_corpus   # 语料完整性守卫：混入多次运行时直接退出

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..")
DATA = os.path.join(ROOT, "EvalLogs", "batch_generations.jsonl")
OUT_DIR = os.path.join(HERE, "results")
OUT = os.path.join(OUT_DIR, "judge_results.jsonl")
KEY_FILE = os.path.join(ROOT, "claude_key.txt")

MODEL = "claude-opus-5"
SHUFFLE_SEED = 20260726        # 固定打乱种子 → 盲评顺序可复现
WORKERS = 3                    # 并发数；单条约 6-16 秒，3 路并发既提速又不撞限流
MAX_TOKENS = 8000

# ---------------- 评分表 ----------------
# 关键：明确禁止因「未写具体数字」扣分——那是生成端提示词主动要求的行为，
#      若不声明，裁判会系统性地惩罚 LLM 档（实测确会发生）。
RUBRIC = """你是游戏叙事的评审专家。你会看到一款动作RPG的世界背景、一个任务的机制与情境参数，
以及一段为该任务生成的叙事文本。请严格按下列四个维度各打 1-5 分。

【element_coverage 情境要素覆盖】文本是否提及给定的三个情境要素（地点、对象、时机）。
  5=三个都自然地出现；3=出现两个；1=一个都没有。改写或近义表达同样算覆盖。

【integration 有机整合】要素是被编织进有因果、有画面的叙事，还是仅仅并列罗列。
  5=要素相互关联、有场景感与因果；3=要素都在但基本是并列陈述；1=生硬拼接或与情境无关。

【world_fit 世界观契合】文本是否体现该世界设定的内在逻辑（而非任何奇幻世界都能套用的通用描述）。
  5=明确借用了世界设定的特有元素或因果；3=语气契合但未用到设定特有内容；1=与设定无关。

【fluency 语言自然度】中文是否通顺、无语法瑕疵（如重复助词、生硬拼接）。
  5=自然流畅；3=可读但有瑕疵；1=明显不通顺。

【contradicts_mechanic】文本是否与固定机制矛盾（例如机制为击败3名，文本却说"大军压境"）。

重要评分规则：
- 生成方被明确要求「不要写死具体数字」，因此**文本未提及数量不算缺陷，不得因此扣分**；
  只有当文本给出的数量或目标与机制**相互矛盾**时，才在 contradicts_mechanic 标记为 true。
- 只依据文本本身评分，不要推测它由什么方法生成。
- reason 用一句中文说明主要依据。"""


def load_key():
    return open(KEY_FILE, encoding="utf-8").read().strip()


def narrative_of(r):
    return "\n".join([
        f"标题：{r.get('title','')}",
        f"背景：{r.get('description','')}",
        f"目标：{r.get('objectiveText','')}",
        f"完成：{r.get('completionText','')}",
    ])


def mechanic_desc(r):
    t = {"DefeatCount": f"击败 {r['requiredCount']} 名敌人",
         "Fetch": f"收集 {r['requiredCount']} 件物品",
         "Boss": "击败一名头目"}
    return t.get(r["questType"], r["questType"])


SCHEMA = {
    "type": "object",
    "properties": {
        "element_coverage": {"type": "integer"},
        "integration": {"type": "integer"},
        "world_fit": {"type": "integer"},
        "fluency": {"type": "integer"},
        "contradicts_mechanic": {"type": "boolean"},
        "reason": {"type": "string"},
    },
    "required": ["element_coverage", "integration", "world_fit",
                 "fluency", "contradicts_mechanic", "reason"],
    "additionalProperties": False,
}


def judge_once(client, world, mech, situ, narrative):
    """调用裁判一次。SDK 自带 429/5xx 重试。返回 (结果dict, 错误字符串)。"""
    prompt = (f"【世界背景】{world}\n"
              f"【任务机制】{mech}\n"
              f"【当前情境】{situ}\n"
              f"【待评叙事】\n{narrative}\n\n请按评分表打分。")
    try:
        # 注意：Claude Opus 5 已移除 temperature/top_p/top_k，传入会返回 400。
        # 结构化输出用 output_config.format，保证返回可直接 json.loads。
        msg = client.messages.create(
            model=MODEL,
            max_tokens=MAX_TOKENS,
            system=RUBRIC,
            output_config={"format": {"type": "json_schema", "schema": SCHEMA}},
            messages=[{"role": "user", "content": prompt}],
        )
        if msg.stop_reason == "refusal":
            return None, "refusal"
        txt = next(b.text for b in msg.content if b.type == "text")
        out = json.loads(txt)
        out["_in"] = msg.usage.input_tokens
        out["_out"] = msg.usage.output_tokens
        return out, None
    except Exception as e:
        return None, f"{type(e).__name__}: {str(e)[:160]}"


DIMS = ["element_coverage", "integration", "world_fit", "fluency"]


def build_items(rows):
    """盲评条目：只留裁判该看的内容。rid 仅用于事后接回身份，裁判永远看不到。"""
    items = [{
        "rid": f"{r['narrator']}|{r['questType']}|{r['instanceIndex']}",
        "world": r["worldContext"],
        "mech": mechanic_desc(r),
        "situ": f"地点：{r['location']}；对象：{r['targetFlavour']}；时机：{r['timeOfDay']}",
        "narrative": narrative_of(r),
    } for r in rows]
    random.Random(SHUFFLE_SEED).shuffle(items)   # 打乱，杜绝位置线索
    return items


def run_stability(client, rows, n):
    """重测稳定性：对已评过的随机 n 条二次评分，报告一致率。
    Opus 5 不接受 temperature，故用这个替代「temperature=0 即确定性」的说法。"""
    prev = {}
    for l in open(OUT, encoding="utf-8"):
        if l.strip():
            d = json.loads(l); prev[d["rid"]] = d
    items = [it for it in build_items(rows) if it["rid"] in prev]
    picked = random.Random(999).sample(items, min(n, len(items)))
    print(f"重测 {len(picked)} 条以检验评分稳定性…\n")

    exact = same_dim = total_dim = 0
    diffs = []
    for it in picked:
        res, err = judge_once(client, it["world"], it["mech"], it["situ"], it["narrative"])
        if res is None:
            print(f"  ❌ {it['rid']}: {err}"); continue
        a, b = prev[it["rid"]], res
        agree = [a[d] == b[d] for d in DIMS]
        same_dim += sum(agree); total_dim += len(DIMS)
        if all(agree):
            exact += 1
        else:
            diffs.append((it["rid"], [(d, a[d], b[d]) for d in DIMS if a[d] != b[d]]))
        d1 = sum(abs(a[d] - b[d]) for d in DIMS)
        print(f"  {it['rid']:38} 四维总偏差 {d1}")
    print(f"\n完全一致: {exact}/{len(picked)} 条 ({exact/len(picked)*100:.0f}%)")
    print(f"逐维一致: {same_dim}/{total_dim} ({same_dim/total_dim*100:.1f}%)")
    if diffs:
        print("\n不一致明细:")
        for rid, ds in diffs[:8]:
            print(f"  {rid}: " + ", ".join(f"{d} {x}→{y}" for d, x, y in ds))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--limit", type=int, default=0, help="只评前 N 条（试跑用）")
    ap.add_argument("--stability", type=int, default=0, help="对已评的随机 N 条重测一致性")
    args = ap.parse_args()

    os.makedirs(OUT_DIR, exist_ok=True)
    client = anthropic.Anthropic(api_key=load_key())
    rows = load_corpus(DATA)

    if args.stability:
        run_stability(client, rows, args.stability)
        return

    items = build_items(rows)

    done = set()
    if os.path.exists(OUT):
        for l in open(OUT, encoding="utf-8"):
            if l.strip():
                done.add(json.loads(l)["rid"])
    todo = [it for it in items if it["rid"] not in done]
    if args.limit:
        todo = todo[:args.limit]

    print(f"待评 {len(todo)} 条（已完成 {len(done)}，总 {len(items)}）")
    print(f"裁判 {MODEL}｜盲评｜固定打乱种子 {SHUFFLE_SEED}｜{WORKERS} 路并发\n")

    lock = threading.Lock()
    state = {"ok": 0, "fail": 0, "tin": 0, "tout": 0}
    t0 = time.time()
    f = open(OUT, "a", encoding="utf-8")

    def work(it):
        res, err = judge_once(client, it["world"], it["mech"], it["situ"], it["narrative"])
        with lock:
            if res is None:
                state["fail"] += 1
                print(f"  ❌ {it['rid']}: {err}")
            else:
                state["ok"] += 1
                state["tin"] += res.pop("_in", 0)
                state["tout"] += res.pop("_out", 0)
                f.write(json.dumps({"rid": it["rid"], **res}, ensure_ascii=False) + "\n")
                f.flush()                    # 逐条落盘 → 中断可续
                n = state["ok"] + state["fail"]
                if n % 20 == 0 or n == 1:
                    el = (time.time() - t0) / 60
                    print(f"[{n}/{len(todo)}] 已用 {el:.1f} 分钟，"
                          f"预计剩余 {el/n*(len(todo)-n):.1f} 分钟")

    with ThreadPoolExecutor(max_workers=WORKERS) as ex:
        list(ex.map(work, todo))
    f.close()

    cost = state["tin"] / 1e6 * 5 + state["tout"] / 1e6 * 25
    print(f"\n完成：成功 {state['ok']}，失败 {state['fail']}，"
          f"耗时 {(time.time()-t0)/60:.1f} 分钟")
    print(f"token: 输入 {state['tin']:,} 输出 {state['tout']:,} → 本次约 ${cost:.2f}")
    print(f"→ {OUT}")
    if state["fail"]:
        print("有失败条目：直接重跑本脚本即可，已完成的会自动跳过。")


if __name__ == "__main__":
    main()
