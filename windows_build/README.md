# Vibe XASR — Windows port (skeleton)

A Windows tray app that mirrors the macOS **Vibe XASR** voice-dictation app: hold a
global hotkey, speak, and the recognized **zh-en** text is inserted at the caret in
whatever app currently has focus. **100% local / offline.**

> **This is a SKELETON.** It is written to compile and be finished **on Windows**.
> It cannot be built or run from macOS — the WinForms GUI and the Windows native
> sherpa-onnx runtime require a Windows toolchain (`net8.0-windows`). Every spot that
> needs real Windows wiring or a value to confirm is marked `// TODO(win):` in the source.

## What it is

- **Tray (NotifyIcon) app**, no main window. Context menu: `Mode ▸ paste/type/oncall`,
  `History…`, `Settings…`, `Quit`.
- **Engine:** streaming zh-en ASR — sherpa-onnx **online/streaming zipformer2 transducer**,
  greedy decoding — plus a **VAD** (silero or FireRed). 16 kHz mono.
- **Three dictation modes** (same behavior as macOS):
  - `paste` — insert the whole result once, on hotkey release.
  - `type` — stream char-by-char to the caret (with backspace diffing as the hypothesis revises).
  - `oncall` — always-on, VAD-segmented; a borderless top-most overlay shows live text; copy manually.
- **Local history** as JSON in `%APPDATA%\VibeXASR\history.json`.
- **Settings** in `%APPDATA%\VibeXASR\settings.json` (mode, tier, hotkey, VAD, language).
- 4-language UI is planned; the skeleton ships **English** only (with a language field stub).

## Prerequisites

- **Windows 10 or 11** (x64 or ARM64).
- **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
- A working microphone.

## NuGet packages (verify versions before building)

| Package | Referenced version | Purpose |
|---|---|---|
| `org.k2fsa.sherpa.onnx` | `1.10.32` | Streaming ASR + VAD; bundles the Windows native runtime (win-x64/arm64). |
| `NAudio` | `2.2.1` | WASAPI mic capture + resampling to 16 kHz mono. |

> **TODO(win):** run `dotnet restore` and confirm both versions resolve. If a version is
> unavailable, pick the latest stable on nuget.org and update `VibeXASR.Windows.csproj`.
> The sherpa-onnx C# **API field names** (config structs / type names) occasionally change
> between majors — if it doesn't compile, reconcile `Dictation/StreamingAsr.cs` and
> `Dictation/Vad.cs` against the installed version's samples.

## Fetch the models

Models are **not** in the repo. They are fetched on first launch from HuggingFace:

```
https://huggingface.co/GilgameshWind/X-ASR-zh-en/resolve/main/deployment/models/chunk-<T>ms-model/<file>
```

Per tier `<T>` ∈ {160, 480, 960, 1920} the files are:
`encoder-<T>ms.onnx`, `decoder-<T>ms.onnx`, `joiner-<T>ms.onnx`, `tokens.txt`.
Plus a VAD model (`silero_vad.onnx` or `fireredvad.onnx`).
**Default tier = 960 ms.** They are cached under `%APPDATA%\VibeXASR\models\`.

`Models/ModelDownloader.cs` does this with progress. You can also pre-download manually
into `%APPDATA%\VibeXASR\models\chunk-960ms-model\` to skip the first-run download.

> **TODO(win):** confirm the exact VAD asset path within the HF repo (the downloader assumes
> `.../models/vad/<file>`); adjust `VadFileUrl` if the layout differs.

## Build

From `windows_build/` in PowerShell:

```powershell
# both architectures into dist/
powershell -ExecutionPolicy Bypass -File .\build.ps1

# or a single RID
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Rids win-x64
```

Or by hand:

```powershell
dotnet publish src/VibeXASR.Windows -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -o dist/win-x64
dotnet publish src/VibeXASR.Windows -c Release -r win-arm64 --self-contained -p:PublishSingleFile=true -o dist/win-arm64
```

The output is a self-contained single-file `VibeXASR.exe` (bundled .NET runtime + native
ONNX DLLs). Run it; a tray icon appears.

## Parity vs macOS

| Concern | macOS app | This Windows skeleton |
|---|---|---|
| Shell | Menu-bar status item | Tray `NotifyIcon` + context menu |
| Global hotkey (push-to-talk hold) | CGEventTap / global monitor | `WH_KEYBOARD_LL` low-level keyboard hook (`GlobalHotkey.cs`) |
| Text insertion | Type Unicode, clear modifiers | `SendInput` + `KEYEVENTF_UNICODE`, clear modifiers (`Input/TextInserter.cs`) |
| Mic capture | AVAudioEngine → 16 kHz mono | NAudio WASAPI → mono → 16 kHz resample (`MicCapture.cs`) |
| ASR | sherpa-onnx online zipformer2 transducer, greedy | same, via `org.k2fsa.sherpa.onnx` (`Dictation/StreamingAsr.cs`) |
| VAD | silero / FireRed | same (`Dictation/Vad.cs`) |
| Modes | paste / type / oncall | paste / type / oncall (`Dictation/DictationEngine.cs`, `TrayApp.cs`) |
| Edge-endpointing + preroll | yes | ported in `DictationEngine.cs` (300 ms preroll, VAD + ASR endpoint) |
| CJK de-spacing | drop spaces between CJK glyphs | `StreamingAsr.DeSpaceCjk` |
| Overlay | floating caption / oncall panel | borderless top-most `OverlayForm` (NOACTIVATE, click-through) |
| History | JSON | `%APPDATA%\VibeXASR\history.json` (`Storage/HistoryStore.cs`) |
| Settings | plist/JSON | `%APPDATA%\VibeXASR\settings.json` (`Storage/Settings.cs`) |
| Models | HF `GilgameshWind/X-ASR-zh-en` | same URL (`Models/ModelDownloader.cs`) |

## Source layout

```
windows_build/
├─ VibeXASR.Windows.sln
├─ build.ps1
├─ README.md
├─ .gitignore
└─ src/VibeXASR.Windows/
   ├─ VibeXASR.Windows.csproj      net8.0-windows, WinForms, win-x64;win-arm64, unsafe
   ├─ app.manifest                 DPI awareness + asInvoker
   ├─ Program.cs                   Main, [STAThread], single-instance, runs TrayApp
   ├─ TrayApp.cs                   tray menu; owns engine + hotkey + mic + overlay
   ├─ GlobalHotkey.cs              WH_KEYBOARD_LL push-to-talk hold (KeyDown/KeyUp)
   ├─ MicCapture.cs                NAudio WASAPI -> 16 kHz mono float frames
   ├─ Dictation/
   │  ├─ StreamingAsr.cs           sherpa OnlineRecognizer wrapper + CJK de-spacing
   │  ├─ Vad.cs                    sherpa VoiceActivityDetector wrapper
   │  └─ DictationEngine.cs        VAD->ASR orchestration, preroll, 3 modes, endpointing
   ├─ Input/
   │  └─ TextInserter.cs           SendInput KEYEVENTF_UNICODE + Backspace(n)
   ├─ Ui/
   │  └─ OverlayForm.cs            borderless top-most overlay; OnCall Copy/Stop
   ├─ Storage/
   │  ├─ Settings.cs               settings.json + enums + %APPDATA% paths
   │  └─ HistoryStore.cs           history.json append/list/clear
   └─ Models/
      ├─ ModelPaths.cs             resolve per-tier file paths
      └─ ModelDownloader.cs        download a tier from HF with progress
```

## Known TODOs (must finish on Windows)

Search the source for `// TODO(win):`. The big ones:

1. **Verify NuGet versions + sherpa-onnx C# API.** Reconcile config struct/field names in
   `StreamingAsr.cs` / `Vad.cs` with the installed `org.k2fsa.sherpa.onnx` version.
2. **Confirm the P/Invoke signatures** for `SetWindowsHookEx`, `SendInput`, and the
   `GetWindowLong`/`SetWindowLong` (use the `...Ptr` variants on 64-bit) calls; build and test
   that the hook fires and Unicode injection lands in a real editor.
3. **Decide hotkey suppression.** `GlobalHotkey` currently passes the key through; choose
   whether to swallow it while held (return `(IntPtr)1`) to match macOS.
4. **Real Settings + History windows** (tier/VAD/hotkey-capture/language; ListView + Clear).
   Changing tier/VAD must re-run model download and restart the engine.
5. **Overlay polish:** per-region click-through (text click-through, buttons clickable),
   rounded pill, and wiring `CopyRequested` to the actual live overlay text rather than the
   latest history entry.
