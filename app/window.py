# -*- coding: utf-8 -*-
"""pywebview window launcher — runs in its OWN process so its GUI run loop does
not collide with the rumps menu-bar loop in shell.py.

    python -m app.window settings|hud|overview|menubar
"""
import os
import sys

import webview

from . import config

PAGES = {
    "settings": ("settings.html", "Vibe IME — 设置", 760, 640),
    "hud":      ("hud.html",      "Vibe IME — HUD 预览", 1000, 640),
    "overview": ("index.html",    "Vibe IME — 设计概览", 1120, 820),
    "menubar":  ("menubar.html",  "Vibe IME — 菜单栏", 420, 560),
}


def main():
    page = sys.argv[1] if len(sys.argv) > 1 else "settings"
    fname, title, w, h = PAGES.get(page, PAGES["settings"])
    url = os.path.join(config.UI_DIR, fname)
    webview.create_window(title, url=url, width=w, height=h,
                          background_color="#0E0E12")
    webview.start()


if __name__ == "__main__":
    main()
