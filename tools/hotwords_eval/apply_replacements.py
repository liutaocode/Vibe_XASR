#!/usr/bin/env python3
"""Apply "from => to" replacement rules to stdin text — mirrors Replacements.swift
(the app's post-recognition corrections): longest `from` first, case-insensitive,
"=>" or "->" separator, lines starting with # ignored.

Usage: apply_replacements.py <rules.txt>   # text on stdin -> corrected on stdout
"""
import re
import sys


def parse(path):
    rules = []
    for ln in open(path, encoding="utf-8"):
        ln = ln.strip()
        if not ln or ln.startswith("#"):
            continue
        i = ln.find("=>")
        if i < 0:
            i = ln.find("->")
        if i < 0:
            continue
        frm, to = ln[:i].strip(), ln[i + 2:].strip()
        if frm:
            rules.append((frm, to))
    return sorted(rules, key=lambda r: -len(r[0]))   # longest first


def apply(text, rules):
    # Single left-to-right pass: alternate all `from` (longest first) so a
    # replacement's output is never re-matched by a later rule (which caused
    # cascades like "a penclaw" -> "OpenClaw" -> "OOpenClaw").
    if not rules:
        return text
    pat = "|".join(re.escape(f) for f, _ in rules)
    lut = {f.lower(): t for f, t in rules}
    return re.sub(pat, lambda m: lut.get(m.group(0).lower(), m.group(0)),
                  text, flags=re.IGNORECASE)


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    sys.stdout.write(apply(sys.stdin.read(), parse(sys.argv[1])))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
