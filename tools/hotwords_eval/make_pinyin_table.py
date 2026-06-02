#!/usr/bin/env python3
"""Generate a compact 汉字→拼音(无声调,多音) table for the app's homophone
normalizer. Format: one line per char  "字 yin1 yin2 ...".
Usage: make_pinyin_table.py <out pinyin.txt>"""
import sys
from pypinyin import pinyin, Style

def main():
    out = sys.argv[1]
    lines = []
    for cp in list(range(0x4E00, 0xA000)) + list(range(0x3400, 0x4DC0)):
        ch = chr(cp)
        r = pinyin(ch, style=Style.NORMAL, heteronym=True)
        if not r: continue
        reads = [x for x in dict.fromkeys(r[0]) if x and x != ch and all('a' <= c <= 'z' for c in x)]
        if reads:
            lines.append(ch + " " + " ".join(reads))
    open(out, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print(f"{len(lines)} chars -> {out}")

if __name__ == "__main__":
    raise SystemExit(main())
