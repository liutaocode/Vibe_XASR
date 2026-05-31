#!/bin/sh
# Build dist/Vibe IME.app with py2app (Apple Silicon).
cd "$(dirname "$0")"
rm -rf build dist
/opt/anaconda3/bin/python setup.py py2app "$@"
echo "→ dist/Vibe IME.app"
