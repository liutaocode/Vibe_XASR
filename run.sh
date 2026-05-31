#!/bin/sh
# Dev launcher — run from Terminal so macOS can attach Microphone / Accessibility
# / Input Monitoring permissions. Menu-bar 🎙 appears; hold Right ⌘ to dictate.
cd "$(dirname "$0")"
exec /opt/anaconda3/bin/python -m app.shell
