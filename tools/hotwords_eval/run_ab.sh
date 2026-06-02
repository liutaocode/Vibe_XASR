#!/usr/bin/env bash
# A/B-decode a set of wavs through the SAME X-ASR model with hotwords off vs on,
# so eval.py can quantify the before/after (recall / CER / false-triggers).
#
# Configs:
#   greedy       — greedy_search           (= today's production path)
#   beam         — modified_beam_search    (no hotwords; isolates pure biasing)
#   hw@<S>       — beam + hotwords @ score S (one per value in $SCORES)
#
# Inputs:
#   manifest.tsv   wav <TAB> reference <TAB> targets(comma-sep)   (targets unused here)
#   hotwords.txt   global hotword list (raw; normalized via prep_hotwords.py)
# Output:  $OUT/out_<config>.tsv   with   wav <TAB> hypothesis
#
# Env overrides:  ASRDIR (model dir w/ encoder/decoder/joiner-<TIER>ms.onnx+tokens.txt),
#                 BIN (sherpa dist root), TIER (default 960), SCORES (default "2 3 6"),
#                 OUT (default ./out), MANIFEST, HOTWORDS.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"

BIN="${BIN:-$REPO/macos_build/native/sherpa/dist/sherpa-onnx-v1.13.2-osx-arm64-shared}"
ASRDIR="${ASRDIR:?set ASRDIR to the model dir (has tokens.txt + encoder/decoder/joiner-<TIER>ms.onnx)}"
TIER="${TIER:-960}"
SCORES="${SCORES:-2 3 6}"
OUT="${OUT:-$HERE/out}"
MANIFEST="${MANIFEST:-$HERE/manifest.tsv}"
HOTWORDS="${HOTWORDS:-$HERE/hotwords.txt}"

mkdir -p "$OUT"
export DYLD_LIBRARY_PATH="$BIN/lib"
SX="$BIN/bin/sherpa-onnx"
ENC="$ASRDIR/encoder-${TIER}ms.onnx"; DEC="$ASRDIR/decoder-${TIER}ms.onnx"
JOI="$ASRDIR/joiner-${TIER}ms.onnx"; TOK="$ASRDIR/tokens.txt"
VOCAB="$ASRDIR/bpe.vocab"

# Augmented bpe.vocab is required for hotword tokenization on this model.
if [ ! -f "$VOCAB" ]; then
  echo "[run_ab] generating $VOCAB from tokens.txt"
  python3 "$HERE/make_bpe_vocab.py" "$TOK" "$VOCAB"
fi
# Normalize hotwords the same way the app does (CJK char-spacing + per-word boost
# at each swept score: CJK=score, English capped ≤2.5).
for s in $SCORES; do
  python3 "$HERE/prep_hotwords.py" "$HOTWORDS" "$s" > "$OUT/hotwords.$s.txt"
done
echo "[run_ab] prepared hotwords per score: $SCORES"

# Extract the recognized text from sherpa's JSON (printed on stderr).
decode() {  # $@ = sherpa args incl. wav ; prints the text field
  "$SX" "$@" 2>&1 | python3 -c '
import sys, json
text = ""
for ln in sys.stdin:
    ln = ln.strip()
    if ln.startswith("{") and "\"text\"" in ln:
        try: text = json.loads(ln).get("text", "")
        except Exception: pass
print(" ".join(text.split()))'
}

COMMON=(--tokens="$TOK" --encoder="$ENC" --decoder="$DEC" --joiner="$JOI")
configs=("greedy" "beam")
for s in $SCORES; do configs+=("hw@$s"); done
for c in "${configs[@]}"; do : > "$OUT/out_$c.tsv"; done

n=0
while IFS=$'\t' read -r wav ref targets; do
  [ -z "${wav:-}" ] && continue
  case "$wav" in \#*) continue;; esac
  [ -f "$wav" ] || { echo "[run_ab] MISSING wav: $wav" >&2; continue; }
  n=$((n+1))
  printf '%s\t%s\n' "$wav" "$(decode "${COMMON[@]}" --decoding-method=greedy_search "$wav")" >> "$OUT/out_greedy.tsv"
  printf '%s\t%s\n' "$wav" "$(decode "${COMMON[@]}" --decoding-method=modified_beam_search "$wav")" >> "$OUT/out_beam.tsv"
  for s in $SCORES; do
    printf '%s\t%s\n' "$wav" "$(decode "${COMMON[@]}" --decoding-method=modified_beam_search \
        --hotwords-file="$OUT/hotwords.$s.txt" --hotwords-score="$s" --modeling-unit=bpe --bpe-vocab="$VOCAB" "$wav")" >> "$OUT/out_hw@$s.tsv"
  done
  echo "[run_ab] $n  $(basename "$wav")"
done < "$MANIFEST"

echo "[run_ab] done: $n utterances × ${#configs[@]} configs -> $OUT/"
echo "[run_ab] next: python3 $HERE/eval.py $MANIFEST $OUT"
