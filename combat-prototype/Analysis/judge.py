# -*- coding: utf-8 -*-
"""
LLM-as-judge —— 对 270 条叙事做盲评打分。

方法学要点（写论文时照这里描述）：
  1) 不同模型家族：生成用 DeepSeek，评审用 Google Gemini（CA1 要求，避免自评偏袒）
  2) 盲评：裁判只看到「世界背景 + 机制 + 情境 + 一段叙事」，
          绝不知道该条出自哪一档；且整体顺序被打乱，杜绝位置线索
  3) 可复现：temperature=0，固定提示词与评分表，固定打乱种子
  4) 结构化输出：responseSchema 强制 JSON，避免解析失败
  5) 断点续跑：结果逐条追加写盘，中断后重跑会自动跳过已完成的

用法：python judge.py            # 全量（约 20 分钟，受免费层 15 RPM 限速）
     python judge.py --limit 12  # 先小样本试跑
输出：results/judge_results.jsonl
"""
import os, sys, json, time, random, argparse, urllib.request, urllib.error
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..")
DATA = os.path.join(ROOT, "EvalLogs", "batch_generations.jsonl")
OUT_DIR = os.path.join(HERE, "results")
OUT = os.path.join(OUT_DIR, "judge_results.jsonl")
KEY_FILE = os.path.join(ROOT, "gemini_key.txt")

MODEL = "gemini-3.6-flash"     # 免费层可用的最新 Flash
SHUFFLE_SEED = 20260726        # 固定打乱种子 → 可复现
MIN_INTERVAL = 4.2             # 秒；免费层 15 RPM，留余量

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
    "type": "OBJECT",
    "properties": {
        "element_coverage": {"type": "INTEGER"},
        "integration": {"type": "INTEGER"},
        "world_fit": {"type": "INTEGER"},
        "fluency": {"type": "INTEGER"},
        "contradicts_mechanic": {"type": "BOOLEAN"},
        "reason": {"type": "STRING"},
    },
    "required": ["element_coverage", "integration", "world_fit",
                 "fluency", "contradicts_mechanic", "reason"],
}


def judge_once(key, world, mech, situ, narrative, retries=4):
    """调用裁判一次，带指数退避重试。返回 (结果dict, 错误字符串)。"""
    prompt = (f"【世界背景】{world}\n"
              f"【任务机制】{mech}\n"
              f"【当前情境】{situ}\n"
              f"【待评叙事】\n{narrative}\n\n请按评分表打分。")
    body = {
        "systemInstruction": {"parts": [{"text": RUBRIC}]},
        "contents": [{"role": "user", "parts": [{"text": prompt}]}],
        "generationConfig": {"temperature": 0, "responseMimeType": "application/json",
                             "responseSchema": SCHEMA},
    }
    url = (f"https://generativelanguage.googleapis.com/v1beta/models/"
           f"{MODEL}:generateContent?key={key}")
    for attempt in range(retries):
        try:
            req = urllib.request.Request(url, data=json.dumps(body).encode(),
                                         headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=120) as resp:
                r = json.load(resp)
            txt = r["candidates"][0]["content"]["parts"][0]["text"]
            out = json.loads(txt)
            out["_usage"] = r.get("usageMetadata", {}).get("totalTokenCount")
            return out, None
        except urllib.error.HTTPError as e:
            code = e.code
            msg = e.read().decode(errors="ignore")[:200]
            if code in (429, 500, 502, 503, 504) and attempt < retries - 1:
                wait = 10 * (2 ** attempt)      # 10s, 20s, 40s
                print(f"    [{code}] 退避 {wait}s 后重试…")
                time.sleep(wait)
                continue
            return None, f"HTTP{code}: {msg}"
        except Exception as e:
            if attempt < retries - 1:
                time.sleep(5)
                continue
            return None, str(e)
    return None, "重试耗尽"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--limit", type=int, default=0, help="只评前 N 条（试跑用）")
    args = ap.parse_args()

    os.makedirs(OUT_DIR, exist_ok=True)
    key = load_key()
    rows = [json.loads(l) for l in open(DATA, encoding="utf-8") if l.strip()]

    # 盲评：只留裁判该看的内容；rid 用于事后接回身份，裁判永远看不到它
    items = []
    for r in rows:
        items.append({
            "rid": f"{r['narrator']}|{r['questType']}|{r['instanceIndex']}",
            "world": r["worldContext"],
            "mech": mechanic_desc(r),
            "situ": f"地点：{r['location']}；对象：{r['targetFlavour']}；时机：{r['timeOfDay']}",
            "narrative": narrative_of(r),
        })
    random.Random(SHUFFLE_SEED).shuffle(items)      # 打乱，杜绝位置线索

    done = set()
    if os.path.exists(OUT):
        for l in open(OUT, encoding="utf-8"):
            if l.strip():
                done.add(json.loads(l)["rid"])
    todo = [it for it in items if it["rid"] not in done]
    if args.limit:
        todo = todo[:args.limit]

    print(f"待评 {len(todo)} 条（已完成 {len(done)}，总 {len(items)}）")
    print(f"裁判模型 {MODEL}｜temperature=0｜盲评｜限速 {MIN_INTERVAL}s/次")
    print(f"预计耗时 {len(todo)*MIN_INTERVAL/60:.1f} 分钟\n")

    ok = fail = 0
    t0 = time.time()
    with open(OUT, "a", encoding="utf-8") as f:
        for i, it in enumerate(todo, 1):
            t_call = time.time()
            res, err = judge_once(key, it["world"], it["mech"], it["situ"], it["narrative"])
            if res is None:
                fail += 1
                print(f"[{i}/{len(todo)}] ❌ {it['rid']}: {err}")
            else:
                ok += 1
                rec = {"rid": it["rid"], **{k: v for k, v in res.items()}}
                f.write(json.dumps(rec, ensure_ascii=False) + "\n")
                f.flush()                       # 逐条落盘 → 中断可续
                if i % 10 == 0 or i == 1:
                    el = (time.time() - t0) / 60
                    print(f"[{i}/{len(todo)}] ✅ 已用 {el:.1f} 分钟，"
                          f"预计剩余 {el/i*(len(todo)-i):.1f} 分钟")
            gap = MIN_INTERVAL - (time.time() - t_call)
            if gap > 0:
                time.sleep(gap)

    print(f"\n完成：成功 {ok}，失败 {fail}，耗时 {(time.time()-t0)/60:.1f} 分钟")
    print(f"→ {OUT}")
    if fail:
        print("有失败条目：直接重跑本脚本即可，已完成的会自动跳过。")


if __name__ == "__main__":
    main()
