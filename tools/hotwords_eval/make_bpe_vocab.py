#!/usr/bin/env python3
"""Reconstruct an (augmented) sentencepiece-style bpe.vocab from tokens.txt, for
hotword tokenization, when the model's real bpe.model/bpe.vocab isn't published.

Two reasons this is needed for the X-ASR zh-en model specifically:

1.  sherpa-onnx tokenizes English hotwords with greedy BPE, merging the adjacent
    pair with the highest score. SentencePiece BPE merge scores are monotonic in
    piece id (icefall's tokens.txt preserves piece order), so `score = -id`
    reproduces the *same relative ordering* — hence the same segmentation — as the
    original model. (Verified: English hotwords bias correctly with this.)

2.  This model encodes EVERY CJK char as its own ▁-prefixed piece (`▁深`) and has
    NO bare-char tokens, so ssentencepiece can't bootstrap Chinese from characters.
    We therefore ALSO emit each bare CJK char (`深`) plus a standalone `▁` at very
    low scores — purely so BPE can build `[▁,深] → ▁深`. These bootstrap entries
    are only used for *encoding* hotwords; the output piece (`▁深`) still maps to
    the real tokens.txt id. Callers must space-separate CJK chars in the hotword
    (each char is its own ▁word) — see HotwordsStore.spaceCJK.

Usage: make_bpe_vocab.py <tokens.txt> <out bpe.vocab>
"""
import sys


def is_cjk(ch: str) -> bool:
    o = ord(ch)
    return (0x3400 <= o <= 0x4DBF) or (0x4E00 <= o <= 0x9FFF) or (0xF900 <= o <= 0xFAFF)


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    toks, out = sys.argv[1], sys.argv[2]
    rows, have = [], set()
    with open(toks, encoding="utf-8") as f:
        for ln in f:
            ln = ln.rstrip("\n")
            if not ln:
                continue
            i = ln.rfind(" ")  # "<piece> <id>"; id has no spaces, piece may
            if i < 0:
                continue
            piece, ids = ln[:i], ln[i + 1:]
            try:
                idx = int(ids)
            except ValueError:
                continue
            rows.append((piece, -idx))
            have.add(piece)

    # Bootstrap entries so ssentencepiece can build ▁X from [▁, X] for CJK.
    extra = []
    if "▁" not in have:
        extra.append(("▁", -1))
    bare = {p[1] for p in rows if len(p[0]) == 2 and p[0][0] == "▁" and is_cjk(p[0][1])}
    for c in sorted(bare):
        if c not in have:
            extra.append((c, -999999))  # bootstrap-only; never preferred as a piece

    with open(out, "w", encoding="utf-8") as f:
        for piece, score in rows + extra:
            f.write(f"{piece}\t{score}\n")
    print(f"wrote {len(rows)} pieces + {len(extra)} bootstrap "
          f"({len(bare)} bare CJK) -> {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
