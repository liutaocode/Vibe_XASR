# -*- coding: utf-8 -*-
"""Export FireRedVAD's DFSMN DetectModel to ONNX (streaming, cache I/O) and
verify frame-exact parity vs the torch model. Also dumps cmvn.json + vad_meta.json
so the native (Swift) side can replicate features + postprocessing.

The model is causal (N2=0), so a zero-init cache is exactly equivalent to torch's
cache=None — ONNX streaming should match torch to ~fp32 epsilon.
"""
import json
import os

import numpy as np
import soundfile as sf
import torch
import torch.nn as nn
import onnxruntime as ort

from fireredvad.core.detect_model import DetectModel
from fireredvad.core.audio_feat import AudioFeat
from fireredvad.core.stream_vad_postprocessor import StreamVadPostprocessor
from fireredvad.stream_vad import FireRedStreamVad, FireRedStreamVadConfig

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))      # xasr_macos_build
MODEL_DIR = os.path.normpath(os.path.join(ROOT, "..", "vad_asr_demo", "models", "firered_vad"))
OUT = os.path.join(ROOT, "models", "firered")
WAV = "/tmp/mic_test.wav"
os.makedirs(OUT, exist_ok=True)

model = DetectModel.from_pretrained(MODEL_DIR).eval()
R = 1 + len(model.dfsmn.fsmns)          # 8 caches
P = model.dfsmn.fsmn1.lookback_filter.in_channels    # 128
Co = model.dfsmn.fsmn1.lookback_padding              # 19
print(f"R={R} P={P} Co={Co}")


class Wrap(nn.Module):
    def __init__(self, m):
        super().__init__()
        self.m = m

    def forward(self, feat, cache_in):          # feat[1,T,80], cache_in[R,1,P,Co]
        caches = [cache_in[i] for i in range(cache_in.shape[0])]
        probs, new_caches = self.m(feat, caches=caches)   # probs[1,T,1]
        return probs, torch.stack(new_caches, dim=0)      # cache_out[R,1,P,Co]


wrap = Wrap(model).eval()
onnx_path = os.path.join(OUT, "firered_vad.onnx")
with torch.no_grad():
    torch.onnx.export(
        wrap, (torch.zeros(1, 30, 80), torch.zeros(R, 1, P, Co)), onnx_path,
        input_names=["feat", "cache_in"], output_names=["probs", "cache_out"],
        dynamic_axes={"feat": {1: "T"}, "probs": {1: "T"}},
        opset_version=17, do_constant_folding=True)
print("exported", onnx_path, os.path.getsize(onnx_path), "bytes")

# ---- features ----
af = AudioFeat(os.path.join(MODEL_DIR, "cmvn.ark"))
wav, sr = sf.read(WAV, dtype="int16")
feats, _ = af.extract(wav)                       # [T,80] torch
feats_np = feats.numpy().astype(np.float32)
T = feats_np.shape[0]

with torch.no_grad():
    probs_torch = model(feats.unsqueeze(0))[0].squeeze().numpy()   # [T]

sess = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
zero = np.zeros((R, 1, P, Co), np.float32)
p_full = sess.run(None, {"feat": feats_np[None], "cache_in": zero})[0].squeeze()

# streaming in 30-frame chunks, carrying cache
cache = zero.copy(); chunks = []
for i in range(0, T, 30):
    pc, cache = sess.run(None, {"feat": feats_np[None, i:i + 30], "cache_in": cache})
    chunks.append(pc.squeeze(0).squeeze(-1))
p_stream = np.concatenate(chunks)

print(f"T={T}")
print(f"max|torch - onnx_full|   = {np.max(np.abs(probs_torch - p_full)):.2e}")
print(f"max|torch - onnx_stream| = {np.max(np.abs(probs_torch - p_stream)):.2e}")

# ---- end-to-end VAD timestamps parity (same params) ----
cfg = FireRedStreamVadConfig(speech_threshold=0.5, min_speech_frame=20, min_silence_frame=70)
gold = FireRedStreamVad.from_pretrained(MODEL_DIR, cfg).detect_full(WAV)[1]["timestamps"]


def pp_timestamps(probs):
    pp = StreamVadPostprocessor(cfg.smooth_window_size, cfg.speech_threshold,
                                cfg.pad_start_frame, cfg.min_speech_frame,
                                cfg.max_speech_frame, cfg.min_silence_frame)
    frs = [pp.process_one_frame(float(x)) for x in probs]
    return FireRedStreamVad.results_to_timestamps(frs)


print("gold (torch)  timestamps:", [(round(a, 2), round(b, 2)) for a, b in gold])
print("onnx-stream   timestamps:", [(round(a, 2), round(b, 2)) for a, b in pp_timestamps(p_stream)])

# ---- dump cmvn + meta for Swift ----
json.dump({"dim": int(af.cmvn.dim), "means": af.cmvn.means.tolist(),
           "inverse_std": af.cmvn.inverse_std_variances.tolist()},
          open(os.path.join(OUT, "cmvn.json"), "w"))
json.dump({"num_caches": R, "P": P, "Co": Co, "idim": 80, "odim": 1,
           "sample_rate": 16000, "num_mel_bins": 80, "frame_length_ms": 25,
           "frame_shift_ms": 10, "snip_edges": True, "dither": 0,
           "postproc": {"smooth_window_size": 5, "speech_threshold": 0.5,
                        "pad_start_frame": 5, "min_speech_frame": 20,
                        "max_speech_frame": 2000, "min_silence_frame": 70}},
          open(os.path.join(OUT, "vad_meta.json"), "w"), indent=2)
print("wrote cmvn.json + vad_meta.json ->", OUT)
