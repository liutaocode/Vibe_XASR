# Hotword A/B evaluation (macOS)

Quantify the effect of hotword biasing on the X-ASR streaming model: decode a set
of wavs with hotwords **off vs on** (and across boost scores) through the *same*
model, then score recall / CER / false-triggers. No app build needed — it drives
the bundled `sherpa-onnx` CLI directly.

## Run

```bash
# 1) Put 16 kHz mono wavs under ./wav and list them in manifest.tsv
#    (wav <TAB> reference <TAB> target hotwords). Edit hotwords.txt too.
# 2) Point ASRDIR at the model dir (encoder/decoder/joiner-960ms.onnx + tokens.txt)
export ASRDIR=/path/to/models/asr
export SCORES="3 5 7"            # boost sweep (CJK boost; English auto-capped ≤2.5)
bash run_ab.sh                   # decodes greedy / beam / hw@<score> → ./out
python3 eval.py manifest.tsv out # prints the comparison table
```

`make_bpe_vocab.py` auto-generates `bpe.vocab` next to the model if missing.

## Metrics

| metric | meaning |
|---|---|
| **recall** | of the target hotwords expected in each reference, how many appear in the hypothesis (the headline biasing metric) |
| **CER** | char-level error rate vs reference (spaceless, lowercased) — guards against biasing hurting overall accuracy |
| **false-trig** | hotwords appearing in a hypothesis but NOT its reference (over-biasing / hallucination) |

## Configs

- `greedy` — `greedy_search` = today's production path.
- `beam` — `modified_beam_search`, no hotwords (isolates the decoder change).
- `hw@<S>` — beam + hotwords, CJK terms boosted at S, English capped at 2.5.

## Key findings (this model)

This X-ASR zh-en model is **non-standard for hotwords**:

- Every CJK char is its own `▁`-prefixed BPE piece; there are **no bare-char
  tokens**, and the official `bpe.model` / `bpe.vocab` is unpublished. So:
  - hotwords are encoded via `modeling_unit=bpe` (not `cjkchar`), with a
    **reconstructed augmented `bpe.vocab`** (`-id` scores reproduce BPE merge order
    for English; bare CJK chars added so BPE can bootstrap `[▁,深]→▁深`);
  - CJK hotwords must be **char-spaced** ("李沐"→"李 沐") so each maps to its `▁X`
    piece. The app (`HotwordsStore.spaceCJK`) and `prep_hotwords.py` both do this.
- **English hotwords work** at low boost (≈2): e.g. `pie torch → PyTorch`,
  `anthropic → Anthropic`. Over-boosting English distorts, so it's auto-capped ≤2.5.
- **Chinese hotwords work for multi-char names/terms** at higher boost (≈5–7):
  e.g. `假洋青 → 贾扬清`, `沈向阳 → 沈向洋`. Single-char **homophones with a
  dominant alternative stay hard** (e.g. `李牧`↔`李沐`) — biasing weights a path,
  it can't invent acoustic evidence.
- `modified_beam_search` can itself differ slightly from `greedy_search` on some
  words independent of hotwords (e.g. `Kubernetes→Kuberneids`), so enabling
  hotwords carries a small whole-utterance variance. We only switch to beam when
  a non-empty hotword list is set (off ⇒ identical greedy path, zero regression).

Demo run (7 `say`-synthesized utterances, hw@7): recall 56%→**67%**, CER
9.6%→**8.8%**, false-trig **0**. TTS audio is cleaner/less representative than a
real mic — **record your own** hotword-heavy clips for a trustworthy number.
