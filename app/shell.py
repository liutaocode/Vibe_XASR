# -*- coding: utf-8 -*-
"""X-ASR menu-bar shell (main process).

Owns: the menu-bar status item, the global push-to-talk hotkey, the X-ASR
streaming engine, and clipboard text injection. The design pages (Settings / HUD
/ overview) open as separate pywebview processes (see window.py) so their GUI run
loop doesn't collide with this rumps loop.
"""
import os
import queue
import subprocess
import sys
import threading

import numpy as np
import rumps
import sounddevice as sd
from pynput import keyboard

from . import config
from .engine import DictationEngine, SAMPLE_RATE, VAD_WINDOW
from .inject import paste_text

_ICONS = {"loading": "⏳", "idle": "🎙", "listening": "🔴", "working": "✍️", "error": "⚠️"}
_PRETTY = {"cmd_r": "Right ⌘", "cmd_l": "Left ⌘", "alt_r": "Right ⌥", "f5": "F5"}


def _hotkey():
    return getattr(keyboard.Key, config.HOTKEY)


class XAsrApp(rumps.App):
    def __init__(self):
        super().__init__("⏳", quit_button=None)
        self.engine = None
        self._status = "loading"
        self._last_partial = ""
        self._collected = []
        self.listening = False
        self.audio_q = None
        self.stop_event = None
        self.worker = None
        self.stream = None

        self.status_item = rumps.MenuItem("加载模型中…")
        self.partial_item = rumps.MenuItem("—")
        self.menu = [
            self.status_item,
            rumps.MenuItem(f"按住 {_PRETTY.get(config.HOTKEY, config.HOTKEY)} 说话"),
            None,
            self.partial_item,
            None,
            rumps.MenuItem("设置…", callback=lambda _: self._spawn("settings")),
            rumps.MenuItem("HUD 预览", callback=lambda _: self._spawn("hud")),
            rumps.MenuItem("设计概览", callback=lambda _: self._spawn("overview")),
            None,
            rumps.MenuItem("退出", callback=rumps.quit_application),
        ]

        rumps.Timer(self._tick, 0.15).start()
        threading.Thread(target=self._load_engine, daemon=True).start()
        self._listener = keyboard.Listener(on_press=self._on_press, on_release=self._on_release)
        self._listener.start()

    # ---- spawn a design-page window in its own process ----
    def _spawn(self, page):
        subprocess.Popen([sys.executable, "-m", "app.window", page], cwd=config.BUILD_DIR)

    # ---- model load ----
    def _load_engine(self):
        try:
            self.engine = DictationEngine(
                config.ASR_DIR, config.FIRERED_DIR, config.SILERO_MODEL,
                provider=config.PROVIDER, vad=config.VAD,
                preroll_s=config.PREROLL_S, tail_pad=config.TAIL_PAD,
                min_silence=config.VAD_MIN_SILENCE)
            self._status = "idle"
        except Exception as e:
            self._status = "error"
            self._last_partial = f"加载失败: {e}"

    # ---- main-thread UI refresh ----
    def _tick(self, _):
        self.title = _ICONS.get(self._status, "🎙")
        labels = {"loading": "加载模型中…", "idle": "就绪", "listening": "聆听中…",
                  "working": "插入中…", "error": "错误"}
        vad = getattr(self.engine, "active_vad_name", None) if self.engine else None
        self.status_item.title = labels.get(self._status, "就绪") + (f"  ({vad})" if vad else "")
        self.partial_item.title = (self._last_partial[-48:] or "—")

    # ---- hotkey ----
    def _on_press(self, key):
        if key == _hotkey() and not self.listening and self._status == "idle":
            self._start()

    def _on_release(self, key):
        if key == _hotkey() and self.listening:
            self._stop()

    # ---- session ----
    def _start(self):
        self.listening = True
        self._status = "listening"
        self._last_partial = ""
        self._collected = []
        self.audio_q = queue.Queue()
        self.stop_event = threading.Event()

        def cb(indata, frames, t, status):
            self.audio_q.put(indata[:, 0].copy())

        self.stream = sd.InputStream(samplerate=SAMPLE_RATE, channels=1, dtype="float32",
                                     blocksize=VAD_WINDOW, callback=cb)
        self.stream.start()
        self.worker = threading.Thread(target=self._run_engine, daemon=True)
        self.worker.start()

    def _run_engine(self):
        self.engine.run(self.audio_q, self.stop_event,
                        on_partial=lambda t: setattr(self, "_last_partial", t),
                        on_final=lambda t: self._collected.append(t))

    def _stop(self):
        self.listening = False
        self._status = "working"
        try:
            self.stream.stop()
            self.stream.close()
        except Exception:
            pass
        self.stop_event.set()

        def _finish():
            if self.worker:
                self.worker.join(timeout=8)
            text = config.JOIN_SEP.join(self._collected).strip()
            if text:
                paste_text(text)
            self._last_partial = ""
            self._status = "idle"

        threading.Thread(target=_finish, daemon=True).start()


def main():
    XAsrApp().run()


if __name__ == "__main__":
    main()
