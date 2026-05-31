# -*- coding: utf-8 -*-
"""Paths & settings for the X-ASR macOS build. UI = the design pages in ui/;
models reuse the demo's in dev, or a bundled models/ when packaged."""
import os

_HERE = os.path.dirname(os.path.abspath(__file__))          # .../xasr_macos_build/app
BUILD_DIR = os.path.dirname(_HERE)                           # .../xasr_macos_build
UI_DIR = os.path.join(BUILD_DIR, "ui")

_BUNDLED_MODELS = os.path.join(BUILD_DIR, "models")
_DEV_MODELS = os.path.normpath(os.path.join(BUILD_DIR, "..", "vad_asr_demo", "models"))


def _model_root():
    env = os.environ.get("VIBE_MODELS_DIR")
    if env and os.path.isdir(env):
        return env
    if os.path.isdir(_BUNDLED_MODELS):
        return _BUNDLED_MODELS
    return _DEV_MODELS


MODELS_DIR = _model_root()
ASR_DIR = os.path.join(MODELS_DIR, "asr")
FIRERED_DIR = os.path.join(MODELS_DIR, "firered_vad")
SILERO_MODEL = os.path.join(MODELS_DIR, "silero_vad.onnx")

# behaviour
PROVIDER = "cpu"          # cpu / coreml
VAD = "firered"           # firered (falls back to silero) / silero
HOTKEY = "cmd_r"          # pynput Key name, hold for push-to-talk
PREROLL_S = 1.0
TAIL_PAD = 1.0
VAD_MIN_SILENCE = 0.7
JOIN_SEP = ""
