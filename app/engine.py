# -*- coding: utf-8 -*-
"""
Streaming dictation engine: FireRedVAD (with silero fallback) + X-ASR streaming
zipformer. Fed audio chunks from a queue; emits live partials and committed
finals via callbacks. Reuses the proven VAD-edge endpointing from live_asr.

Designed for push-to-talk: a worker calls run(audio_q, stop_event, ...) while a
key is held; on key release stop_event is set and any in-flight sentence is
finalized.
"""
import os
import re
import sys
import queue

import numpy as np
import sherpa_onnx

SAMPLE_RATE = 16000
VAD_WINDOW = 512  # silero/firered window (32ms @ 16k)

# --- CJK de-spacing normalization (from X-ASR deployment) ---
_CJK = r"㐀-䶿一-鿿豈-﫿"
_CJK_PUNCT = re.escape("，。！？；：、（）《》〈〉【】「」『』“”‘’")
_ASCII_PUNCT = re.escape(",.!?;:%)]}")


def normalize_cjk(text):
    text = re.sub(rf"(?<=[{_CJK}])\s+(?=[{_CJK}])", "", text)
    text = re.sub(rf"(?<=[{_CJK}])\s+(?=[{_CJK_PUNCT}])", "", text)
    text = re.sub(rf"(?<=[{_CJK_PUNCT}])\s+(?=[{_CJK}])", "", text)
    text = re.sub(rf"(?<=[{_CJK_PUNCT}])\s+(?=[{_CJK_PUNCT}])", "", text)
    text = re.sub(rf"\s+(?=[{_ASCII_PUNCT}])", "", text)
    return text


# --------------------------------------------------------------------------- #
# FireRedVAD adapter (official `pip install fireredvad`), duck-typed to the 4
# methods the run loop needs.
# --------------------------------------------------------------------------- #
class FireRedVad:
    def __init__(self, model_dir, speech_threshold=0.5, min_silence=0.7,
                 min_speech=0.2, chunk_s=0.3):
        from fireredvad.stream_vad import FireRedStreamVad, FireRedStreamVadConfig
        FPS = 100
        cfg = FireRedStreamVadConfig(
            speech_threshold=speech_threshold,
            min_speech_frame=max(1, int(round(min_speech * FPS))),
            min_silence_frame=max(1, int(round(min_silence * FPS))),
        )
        self.vad = FireRedStreamVad.from_pretrained(model_dir, cfg)
        self.vad.reset()
        self.chunk = int(chunk_s * SAMPLE_RATE)
        self._buf = np.zeros(0, dtype=np.float32)
        self.in_speech = False
        self._pending = 0

    def _run(self, chunk_f32):
        i16 = (np.clip(chunk_f32, -1.0, 1.0) * 32767.0).astype(np.int16)
        for r in self.vad.detect_chunk(i16):
            if r.is_speech_start:
                self.in_speech = True
            if r.is_speech_end:
                self.in_speech = False
                self._pending += 1

    def accept_waveform(self, window):
        self._buf = np.concatenate([self._buf, np.asarray(window, dtype=np.float32)])
        while len(self._buf) >= self.chunk:
            self._run(self._buf[:self.chunk])
            self._buf = self._buf[self.chunk:]

    def is_speech_detected(self):
        return self.in_speech

    def empty(self):
        return self._pending == 0

    def pop(self):
        if self._pending > 0:
            self._pending -= 1


def _build_silero(silero_model, threshold, min_silence, min_speech, provider):
    cfg = sherpa_onnx.VadModelConfig()
    cfg.silero_vad.model = silero_model
    cfg.silero_vad.threshold = threshold
    cfg.silero_vad.min_silence_duration = min_silence
    cfg.silero_vad.min_speech_duration = min_speech
    cfg.silero_vad.window_size = VAD_WINDOW
    cfg.sample_rate = SAMPLE_RATE
    cfg.provider = provider
    return sherpa_onnx.VoiceActivityDetector(cfg, buffer_size_in_seconds=30)


def _find_asr_files(asr_dir):
    import glob
    onnx = sorted(glob.glob(os.path.join(asr_dir, "*.onnx")))
    onnx = [p for p in onnx if "vad" not in os.path.basename(p).lower()]
    if not onnx:
        raise SystemExit(f"[engine] no .onnx model under {asr_dir}")
    tokens = os.path.join(asr_dir, "tokens.txt")

    def pick(sub):
        cand = [p for p in onnx if sub in os.path.basename(p).lower()]
        noint8 = [p for p in cand if "int8" not in os.path.basename(p).lower()]
        return (noint8 or cand or [None])[0]

    enc, dec, join = pick("encoder"), pick("decoder"), pick("joiner")
    if enc and dec and join:
        return dict(tokens=tokens, encoder=enc, decoder=dec, joiner=join)
    raise SystemExit(f"[engine] expected transducer encoder/decoder/joiner in {asr_dir}")


class DictationEngine:
    """Loads the recognizer once; run() drives one push-to-talk session."""

    def __init__(self, asr_dir, firered_dir, silero_model, provider="cpu",
                 vad="firered", model_type="", tail_pad=1.0, preroll_s=0.7,
                 vad_threshold=0.5, min_silence=0.7, min_speech=0.25):
        files = _find_asr_files(asr_dir)
        self.recognizer = sherpa_onnx.OnlineRecognizer.from_transducer(
            **files, model_type=model_type, num_threads=2, provider=provider,
            decoding_method="greedy_search", enable_endpoint_detection=False)
        self.vad = vad
        self.firered_dir = firered_dir
        self.silero_model = silero_model
        self.provider = provider
        self.tail_pad = tail_pad
        self.preroll_s = preroll_s
        self.vad_threshold = vad_threshold
        self.min_silence = min_silence
        self.min_speech = min_speech
        self.active_vad_name = None

    def _new_vad(self):
        if self.vad == "firered":
            try:
                v = FireRedVad(self.firered_dir, speech_threshold=self.vad_threshold,
                               min_silence=self.min_silence, min_speech=self.min_speech)
                self.active_vad_name = "firered"
                return v
            except Exception as e:  # missing package/weights -> silero
                print(f"[engine] FireRedVAD unavailable ({type(e).__name__}: {e}); "
                      f"using silero.", file=sys.stderr)
        self.active_vad_name = "silero"
        return _build_silero(self.silero_model, self.vad_threshold,
                             self.min_silence, self.min_speech, self.provider)

    def run(self, audio_q, stop_event, on_partial=None, on_final=None):
        """Pull float32 chunks from audio_q until stop_event is set AND the queue
        is drained; emit on_partial(text) live and on_final(text) per sentence
        (VAD edge) plus a final flush of any in-flight sentence on stop."""
        rec = self.recognizer
        vad = self._new_vad()
        stream = None
        active = False
        seg_samples = 0
        preroll = []
        PREROLL = max(1, int(self.preroll_s * SAMPLE_RATE / VAD_WINDOW))
        buf = np.zeros(0, dtype=np.float32)

        def finalize():
            nonlocal active, stream
            stream.accept_waveform(SAMPLE_RATE,
                                   np.zeros(int(self.tail_pad * SAMPLE_RATE), dtype="float32"))
            stream.input_finished()
            while rec.is_ready(stream):
                rec.decode_stream(stream)
            text = normalize_cjk(rec.get_result(stream))
            active = False
            stream = None
            if text.strip() and on_final:
                on_final(text.strip())

        def process(w):
            nonlocal active, stream, seg_samples
            vad.accept_waveform(w)
            speech = vad.is_speech_detected()
            if speech and not active:
                active = True
                stream = rec.create_stream()
                seg_samples = 0
                for pw in preroll:
                    stream.accept_waveform(SAMPLE_RATE, pw)
                    seg_samples += len(pw)
            if active:
                stream.accept_waveform(SAMPLE_RATE, w)
                seg_samples += len(w)
                while rec.is_ready(stream):
                    rec.decode_stream(stream)
                if on_partial:
                    p = normalize_cjk(rec.get_result(stream))
                    if p:
                        on_partial(p)
            if active and not speech:
                finalize()
            preroll.append(w)
            if len(preroll) > PREROLL:
                preroll.pop(0)
            while not vad.empty():
                vad.pop()

        while True:
            try:
                chunk = audio_q.get(timeout=0.05)
            except queue.Empty:
                if stop_event.is_set():
                    break
                continue
            if chunk is None:
                break
            buf = np.concatenate([buf, np.asarray(chunk, dtype=np.float32)])
            while len(buf) >= VAD_WINDOW:
                process(buf[:VAD_WINDOW])
                buf = buf[VAD_WINDOW:]

        # key released / stream ended: finalize any in-flight sentence
        if active and stream is not None:
            finalize()
