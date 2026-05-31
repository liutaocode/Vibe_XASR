# -*- coding: utf-8 -*-
"""py2app build config — produces dist/Vibe IME.app (Apple Silicon).

Bundles the design UI (ui/) and the models (models/). NOTE: FireRedVAD pulls
PyTorch, which py2app must collect carefully (large; see README "Packaging").
For a first packaging pass, build with VAD=silero (no torch) to validate the
.app, then add torch.
"""
import os
from setuptools import setup

BUILD = os.path.dirname(os.path.abspath(__file__))


def tree(rel):
    out = []
    for root, _, files in os.walk(os.path.join(BUILD, rel)):
        files = [f for f in files if not f.startswith(".")]
        if files:
            dst = os.path.relpath(root, BUILD)
            out.append((dst, [os.path.join(root, f) for f in files]))
    return out


DATA_FILES = tree("ui")
if os.path.isdir(os.path.join(BUILD, "models")):
    DATA_FILES += tree("models")

OPTIONS = {
    "argv_emulation": False,
    "packages": ["app"],
    "includes": ["sherpa_onnx", "sounddevice", "soundfile", "numpy",
                 "rumps", "pynput", "webview", "objc", "Quartz", "AppKit"],
    "plist": {
        "CFBundleName": "Vibe IME",
        "CFBundleDisplayName": "Vibe IME",
        "CFBundleIdentifier": "com.xasr.vibeime",
        "CFBundleShortVersionString": "0.1.0",
        "LSUIElement": True,                 # menu-bar only, no Dock icon
        "LSMinimumSystemVersion": "13.0",
        "NSMicrophoneUsageDescription": "Vibe IME 需要麦克风来做本地语音听写。",
        "NSHighResolutionCapable": True,
    },
}

setup(
    name="VibeIME",
    app=["app/shell.py"],
    data_files=DATA_FILES,
    options={"py2app": OPTIONS},
    setup_requires=["py2app"],
)
