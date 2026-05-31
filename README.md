# Vibe XASR — 本地语音输入法

按住热键说话,文字直接落到光标处。**100% 本地、离线、不上云**。
内置 [X-ASR](https://github.com/Gilgamesh-J/X-ASR) 流式中英识别 + VAD;识别内核(sherpa-onnx +
ONNX 模型)跨平台,各平台用各自原生外壳。

仓库按平台分两个目录:

| 目录 | 平台 | 状态 | 技术栈 |
|---|---|---|---|
| [`macos_build/`](macos_build/) | macOS 13+ · **Apple Silicon + Intel** | ✅ 已发布 v1.1.0(签名+公证) | Swift · SwiftUI/AppKit · sherpa-onnx |
| [`windows_build/`](windows_build/) | Windows 10/11 · x64 + arm64 | 🚧 框架骨架(在 Windows 上完成) | C# · .NET 8 · WinForms · sherpa-onnx |

## 共用的"识别内核"(两端一致)

- **流式 ASR**:sherpa-onnx 在线 zipformer2 transducer(贪心),中英 code-switch
- **模型**:X-ASR zh-en(`encoder/decoder/joiner-<tier>ms.onnx` + `tokens.txt`,档位 160/480/960/1920ms)+ VAD(FireRed / silero)
- **来源**:HuggingFace [`GilgameshWind/X-ASR-zh-en`](https://huggingface.co/GilgameshWind/X-ASR-zh-en)(模型不入仓库,按需下载)

平台差异只在"外壳":菜单栏/托盘、全局热键、麦克风采集、文本插入、悬浮窗、权限。

## 构建(各在本平台)

- **macOS**:`cd macos_build && ./package_release.sh`(构建 universal2 → 签名 → 公证 → dmg);
  快速自测 `./package_release.sh dev`。详见 [macos_build/README.md](macos_build/README.md)。
- **Windows**:`cd windows_build && ./build.ps1`(`dotnet publish -r win-x64` / `win-arm64`)。
  详见 [windows_build/README.md](windows_build/README.md)。

> **关于交叉编译**:macOS 侧在一台 Mac 上即可同时产出 arm64+x86_64 的 universal2。
> Windows 侧可在 Windows 上交叉产出 win-x64 / win-arm64,但 **Windows GUI 无法在 macOS 上构建**
> (WinForms 需要 Windows Desktop SDK),需在 Windows 机器/虚拟机/CI 上编译。

## 下载

已签名公证的 macOS 安装包见 [Releases](https://github.com/liutaocode/x_asr_local_build/releases)。
