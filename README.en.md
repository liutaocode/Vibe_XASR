<!-- 语言 / Language --> **English** · [中文](README.md)

# Vibe XASR — Local Voice Input Method

Hold a hotkey, speak, and the text lands right at your cursor. **100% local and offline — your
data is always kept on-device and never goes to the cloud.**

Built on [X-ASR](https://github.com/Gilgamesh-J/X-ASR) streaming Chinese/English recognition + VAD.
The recognition core (sherpa-onnx + ONNX models) is cross-platform; each OS gets its own native shell.

> 🌐 Homepage: <https://liutaocode.github.io/Vibe_XASR/>

## Features

- **Three dictation modes**: insert-on-finish / streaming character-by-character / OnCall stand-by
  (live floating overlay with copy & export)
- **Push-to-talk**: a global hotkey dictates while held and inserts on release, straight to the cursor
- **Built-in pad + history**: optionally keep full history grouped by date; per-entry copy/edit and
  one-click export of all data
- **Localized UI**: Chinese / English / Japanese / Korean — follows the system by default, switchable
- **Selectable latency tiers**: 160 / 480 / 960 / 1920 ms, models downloaded on demand
- **Selectable VAD**: FireRedVAD / silero-vad
- **Auto-update**: powered by [Sparkle](https://sparkle-project.org) — checks the appcast in the
  background and updates in place with EdDSA signature verification ("Check for Updates" in About / menu)
- **Privacy**: fully offline; optional "overwrite clipboard on each utterance" (off by default);
  an ephemeral buffer when history saving is disabled

## Download

Signed & notarized macOS builds are on **[Releases](https://github.com/liutaocode/Vibe_XASR/releases/latest)**.

- Universal2 (Apple Silicon + Intel), Developer ID signed + Apple notarized
- Once installed, future versions **update automatically** inside the app — no more manual dmg downloads

> ⚠️ Requires **macOS 15 (Sequoia)** or later. This is a limitation of the underlying onnxruntime build
> (it strong-links a macOS 15 CoreML symbol), so it fails to launch on macOS 13/14. Intel Macs must be
> on macOS 15.

## Repository layout

Split into one directory per platform:

| Directory | Platform | Status | Stack |
|---|---|---|---|
| [`macos_build/`](macos_build/) | **macOS 15+** · Apple Silicon + Intel | ✅ Released (signed + notarized + auto-update) | Swift · SwiftUI/AppKit · sherpa-onnx |
| [`windows_build/`](windows_build/) | Windows 10/11 · x64 + arm64 | 🚧 Skeleton (finish on Windows) | C# · .NET 8 · WinForms · sherpa-onnx |

## The shared "recognition core" (identical on both ends)

- **Streaming ASR**: sherpa-onnx online zipformer2 transducer (greedy), Chinese/English code-switch
- **Models**: X-ASR zh-en (`encoder/decoder/joiner-<tier>ms.onnx` + `tokens.txt`, tiers 160/480/960/1920 ms)
  + VAD (FireRed / silero)
- **Source**: HuggingFace [`GilgameshWind/X-ASR-zh-en`](https://huggingface.co/GilgameshWind/X-ASR-zh-en)
  (models are not committed; downloaded on demand)

Only the "shell" differs per platform: menu bar / tray, global hotkey, mic capture, text insertion,
floating overlay, permissions.

## Building (each on its own platform)

- **macOS**: `cd macos_build && ./package_release.sh` (build universal2 → sign → notarize → dmg →
  generate the Sparkle update archive + appcast). Quick local test: `./package_release.sh dev`.
  See [macos_build/README.md](macos_build/README.md).
- **Windows**: `cd windows_build && ./build.ps1` (`dotnet publish -r win-x64` / `win-arm64`).
  See [windows_build/README.md](windows_build/README.md).

> **On cross-compilation**: on macOS, a single Mac produces the arm64 + x86_64 universal2 build.
> On Windows you can cross-build win-x64 / win-arm64, but the **Windows GUI cannot be built on macOS**
> (WinForms needs the Windows Desktop SDK) — build it on a Windows machine / VM / CI.

## How auto-update works

The app embeds Sparkle, which periodically (and on demand) fetches `appcast.xml` from GitHub Pages and
compares it to the running version. If a newer build exists, it downloads the update archive, verifies
the EdDSA signature against the bundled public key, then replaces the app in place and relaunches. The
archive is a zip of the **notarized** app (the notarization ticket is stapled inside, so it passes
Gatekeeper after extraction), distributed as a Releases asset.

## Credits

[X-ASR](https://github.com/Gilgamesh-J/X-ASR) · [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) ·
[FireRedVAD](https://github.com/FireRedTeam/FireRedVAD) · [silero-vad](https://github.com/snakers4/silero-vad) ·
[onnxruntime](https://github.com/microsoft/onnxruntime) · [Sparkle](https://github.com/sparkle-project/Sparkle)
