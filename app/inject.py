# -*- coding: utf-8 -*-
"""Insert text into the focused app via clipboard + Cmd-V (reliable for CJK).
Saves and restores the previous clipboard so the user's copy buffer survives.
Requires Accessibility permission for the synthesized key events.
"""
import threading
import time

from AppKit import NSPasteboard, NSPasteboardTypeString
import Quartz

_V_KEYCODE = 9  # ANSI 'v'


def _send_cmd_v():
    src = Quartz.CGEventSourceCreate(Quartz.kCGEventSourceStateHIDSystemState)
    down = Quartz.CGEventCreateKeyboardEvent(src, _V_KEYCODE, True)
    Quartz.CGEventSetFlags(down, Quartz.kCGEventFlagMaskCommand)
    up = Quartz.CGEventCreateKeyboardEvent(src, _V_KEYCODE, False)
    Quartz.CGEventSetFlags(up, Quartz.kCGEventFlagMaskCommand)
    Quartz.CGEventPost(Quartz.kCGHIDEventTap, down)
    Quartz.CGEventPost(Quartz.kCGHIDEventTap, up)


def paste_text(text, restore_delay=0.5):
    """Place `text` on the pasteboard, send Cmd-V, then restore prior clipboard."""
    if not text:
        return
    pb = NSPasteboard.generalPasteboard()
    old = pb.stringForType_(NSPasteboardTypeString)
    pb.clearContents()
    pb.setString_forType_(text, NSPasteboardTypeString)
    # tiny settle so the target app sees the new pasteboard before Cmd-V
    time.sleep(0.02)
    _send_cmd_v()
    if old is not None:
        def _restore():
            time.sleep(restore_delay)
            p = NSPasteboard.generalPasteboard()
            p.clearContents()
            p.setString_forType_(old, NSPasteboardTypeString)
        threading.Thread(target=_restore, daemon=True).start()
