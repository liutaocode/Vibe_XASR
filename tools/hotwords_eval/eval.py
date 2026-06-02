#!/usr/bin/env python3
"""Score hotword A/B runs — zero dependencies (stdlib only).

Reads the manifest + every out_<config>.tsv produced by run_ab.sh and prints a
comparison table across configs:

  * Hotword recall — of the target terms expected in each reference, how many
    appear in the hypothesis (the headline biasing metric).
  * CER — char-level error rate vs the reference (spaceless, lowercased), to check
    that biasing doesn't hurt overall accuracy.
  * False-trig — hotwords that appear in a hypothesis but NOT in its reference
    (over-biasing / "hallucination"), summed over utterances.

Then per-utterance lines for cases where recall changed vs the greedy baseline.

Usage: eval.py <manifest.tsv> <out dir>
"""
import os
import sys
import glob


def norm(s: str) -> str:
    return "".join(s.split()).lower()


def lev(a: str, b: str) -> int:
    if a == b:
        return 0
    if not a:
        return len(b)
    if not b:
        return len(a)
    prev = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        cur = [i]
        for j, cb in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (ca != cb)))
        prev = cur
    return prev[-1]


def config_sort_key(c: str):
    if c == "greedy":
        return (0, 0.0)
    if c == "beam":
        return (1, 0.0)
    if c.startswith("hw@"):
        try:
            return (2, float(c[3:]))
        except ValueError:
            return (2, 0.0)
    return (3, 0.0)


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    manifest, outdir = sys.argv[1], sys.argv[2]

    # manifest: wav <TAB> reference <TAB> targets(comma-sep)
    ref, targets = {}, {}
    for ln in open(manifest, encoding="utf-8"):
        ln = ln.rstrip("\n")
        if not ln or ln.startswith("#"):
            continue
        parts = ln.split("\t")
        wav = parts[0]
        ref[wav] = parts[1] if len(parts) > 1 else ""
        targets[wav] = [t.strip() for t in (parts[2].split(",") if len(parts) > 2 else []) if t.strip()]
    all_hot = sorted({t for ts in targets.values() for t in ts})

    # discover configs
    hyps = {}
    for path in glob.glob(os.path.join(outdir, "out_*.tsv")):
        cfg = os.path.basename(path)[len("out_"):-len(".tsv")]
        d = {}
        for ln in open(path, encoding="utf-8"):
            ln = ln.rstrip("\n")
            if not ln:
                continue
            w, _, h = ln.partition("\t")
            d[w] = h
        hyps[cfg] = d
    if not hyps:
        print(f"no out_*.tsv in {outdir}")
        return 1
    configs = sorted(hyps, key=config_sort_key)
    wavs = [w for w in ref if any(w in hyps[c] for c in configs)]

    # aggregate
    rows = []
    per_recall = {}  # cfg -> {wav: hits}
    for c in configs:
        hit = tot = ftrig = ed = reflen = 0
        per_recall[c] = {}
        for w in wavs:
            h = hyps[c].get(w, "")
            hn, rn = norm(h), norm(ref[w])
            ed += lev(rn, hn)
            reflen += len(rn)
            wh = sum(1 for t in targets[w] if norm(t) in hn)
            hit += wh
            tot += len(targets[w])
            per_recall[c][w] = (wh, len(targets[w]))
            for t in all_hot:
                tn = norm(t)
                if tn not in rn and tn in hn:
                    ftrig += 1
        recall = 100.0 * hit / tot if tot else 0.0
        cer = 100.0 * ed / reflen if reflen else 0.0
        rows.append((c, recall, hit, tot, cer, ftrig))

    label = {"greedy": "greedy (today)", "beam": "beam (no hw)"}
    print(f"\n{len(wavs)} utterances · {len(all_hot)} distinct hotwords\n")
    print(f"{'config':<16}{'recall':>10}{'CER':>9}{'false-trig':>12}")
    print("-" * 47)
    for c, recall, hit, tot, cer, ftrig in rows:
        name = label.get(c, c)
        print(f"{name:<16}{f'{recall:.0f}% ({hit}/{tot})':>10}{f'{cer:.1f}%':>9}{ftrig:>12}")

    # per-utterance recall changes vs greedy (compare against the best hw config:
    # highest total recall, ties broken toward the LOWER score to avoid over-bias)
    base = per_recall.get("greedy", {})
    cand = [c for c in configs if c.startswith("hw@")] or [c for c in configs if c != "greedy"] or configs
    best = max(cand, key=lambda c: (sum(h for h, _ in per_recall[c].values()), -config_sort_key(c)[1]))
    print(f"\nper-utterance recall change  greedy → {label.get(best, best)}:")
    for w in wavs:
        b_hit, n = base.get(w, (0, 0))
        x_hit, _ = per_recall[best].get(w, (0, 0))
        if n == 0:
            continue
        flag = "↑" if x_hit > b_hit else ("↓" if x_hit < b_hit else " ")
        if flag != " " or x_hit < n:
            print(f"  [{flag}] {b_hit}/{n}→{x_hit}/{n}  ref: {ref[w]}")
            print(f"        greedy: {hyps['greedy'].get(w,'')}")
            print(f"        {best:>6}: {hyps[best].get(w,'')}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
