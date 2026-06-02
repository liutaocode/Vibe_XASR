#!/usr/bin/env python3
"""Normalize a raw hotword list into the form sherpa-onnx expects for THIS model,
mirroring the app's HotwordsStore (Swift) so the eval matches runtime behaviour:

  * trim each line, drop blanks and `#` comments;
  * space-separate every CJK char (each is its own ▁-prefixed piece in this model),
    leaving English words intact;
  * if a score is given, append a per-word boost — CJK terms use `score`, pure
    English terms are capped at 2.5 (over-boosting English distorts). An explicit
    trailing " :N" is respected as-is;
  * for pure-English words, ALSO emit a capitalized-first variant (the model emits
    capitalized proper nouns, so a lowercase "pytorch" would match nothing).

Usage: prep_hotwords.py <raw hotwords.txt> [score]   # normalized list -> stdout
"""
import re
import sys


def is_cjk(ch: str) -> bool:
    o = ord(ch)
    return (0x3400 <= o <= 0x4DBF) or (0x4E00 <= o <= 0x9FFF) or (0xF900 <= o <= 0xFAFF)


def space_cjk(s: str) -> str:
    out = []
    for ch in s:
        out.append(f" {ch} " if is_cjk(ch) else ch)
    return " ".join("".join(out).split())


def fmt(s: float) -> str:
    return str(int(s)) if s == int(s) else f"{s:g}"


def with_boost(line: str, score: float) -> str:
    m = re.search(r"\s:\d+(\.\d+)?$", line)
    if m:
        return space_cjk(line[:m.start()]) + " " + line[m.start():].strip()
    s = score if any(is_cjk(c) for c in line) else min(score, 2.5)
    return space_cjk(line) + " :" + fmt(s)


def expand(line: str, score: float):
    if re.search(r"\s:\d+(\.\d+)?$", line) or any(is_cjk(c) for c in line):
        return [with_boost(line, score)]
    cap = line[:1].upper() + line[1:]
    variants = [line] if cap == line else [line, cap]
    return [with_boost(v, score) for v in variants]


def main() -> int:
    if len(sys.argv) not in (2, 3):
        print(__doc__)
        return 2
    score = float(sys.argv[2]) if len(sys.argv) == 3 else None
    for ln in open(sys.argv[1], encoding="utf-8"):
        ln = ln.strip()
        if not ln or ln.startswith("#"):
            continue
        if score is None:
            print(space_cjk(ln))
        else:
            for v in expand(ln, score):
                print(v)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
