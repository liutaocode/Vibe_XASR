[English](README.en.md) · **中文**

# Vibe XASR — 本地语音输入法

按住热键说话,文字直接落到光标处。**100% 本地、离线,数据永远保存在本地,绝不上云。**

内置 [X-ASR](https://github.com/Gilgamesh-J/X-ASR) 流式中英识别 + VAD;识别内核(sherpa-onnx +
ONNX 模型)跨平台,各平台用各自原生外壳。

> 🌐 项目主页:<https://liutaocode.github.io/Vibe_XASR/>

## 功能

- **三种听写模式**:说完一次性插入 / 逐字流式插入 / OnCall 持续候机(悬浮窗实时显示、可复制/导出)
- **按住即说**:全局热键按下听写、松开上屏,文字直接写到当前光标处
- **内置便签 + 历史记录**:可保存全部历史,按日期分段;支持逐条复制/编辑、一键导出全部数据
- **多语言界面**:中 / 英 / 日 / 韩,默认跟随系统,可手动切换
- **延迟档位可选**:160 / 480 / 960 / 1920ms,按需下载对应模型
- **VAD 可选**:FireRedVAD / silero-vad
- **自动更新**:基于 [Sparkle](https://sparkle-project.org),后台检查更新源,发现新版即可一键下载、校验签名、原地升级(关于页 / 菜单栏「检查更新」)
- **隐私**:全程离线,可选「每次说话覆盖剪贴板」(默认关闭);不保存历史时提供临时缓冲

## 下载

已签名公证的 macOS 安装包见 **[Releases](https://github.com/liutaocode/Vibe_XASR/releases/latest)**。

- Universal2(Apple Silicon + Intel),Developer ID 签名 + Apple 公证
- 装好后,后续版本可在 App 内**自动更新**,无需再手动下 dmg

> ⚠️ 最低 **macOS 15 (Sequoia)** —— 受底层 onnxruntime 构建限制(它强引用了 macOS 15 的 CoreML
> 符号),在 macOS 13/14 上会启动失败。Intel Mac 需升级到 macOS 15 才能运行。

## 仓库结构

仓库按平台分两个目录:

| 目录 | 平台 | 状态 | 技术栈 |
|---|---|---|---|
| [`macos_build/`](macos_build/) | **macOS 15+** · Apple Silicon + Intel | ✅ 已发布(签名 + 公证 + 自动更新) | Swift · SwiftUI/AppKit · sherpa-onnx |
| [`windows_build/`](windows_build/) | Windows 10/11 · x64 + arm64 | 🚧 框架骨架(在 Windows 上完成) | C# · .NET 8 · WinForms · sherpa-onnx |

## 共用的「识别内核」(两端一致)

- **流式 ASR**:sherpa-onnx 在线 zipformer2 transducer(贪心),中英 code-switch
- **模型**:X-ASR zh-en(`encoder/decoder/joiner-<tier>ms.onnx` + `tokens.txt`,档位 160/480/960/1920ms)+ VAD(FireRed / silero)
- **来源**:HuggingFace [`GilgameshWind/X-ASR-zh-en`](https://huggingface.co/GilgameshWind/X-ASR-zh-en)(模型不入仓库,按需下载)

平台差异只在「外壳」:菜单栏/托盘、全局热键、麦克风采集、文本插入、悬浮窗、权限。

## 构建(各在本平台)

- **macOS**:`cd macos_build && ./package_release.sh`(构建 universal2 → 签名 → 公证 → dmg →
  生成 Sparkle 更新包 + appcast);快速自测 `./package_release.sh dev`。
  详见 [macos_build/README.md](macos_build/README.md)。
- **Windows**:`cd windows_build && ./build.ps1`(`dotnet publish -r win-x64` / `win-arm64`)。
  详见 [windows_build/README.md](windows_build/README.md)。

> **关于交叉编译**:macOS 侧在一台 Mac 上即可同时产出 arm64 + x86_64 的 universal2。
> Windows 侧可在 Windows 上交叉产出 win-x64 / win-arm64,但 **Windows GUI 无法在 macOS 上构建**
> (WinForms 需要 Windows Desktop SDK),需在 Windows 机器 / 虚拟机 / CI 上编译。

## 自动更新如何工作

App 内置 Sparkle,定期(及手动)拉取 GitHub Pages 上的 `appcast.xml`,与当前版本比对;
有新版则下载对应的更新包,用 EdDSA 公钥校验签名后原地替换并重启。更新包是「已公证 App 的 zip」
(公证票据随包内附,解压后照样通过 Gatekeeper),作为 Releases 资产分发。

## 致谢

[X-ASR](https://github.com/Gilgamesh-J/X-ASR) · [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) ·
[FireRedVAD](https://github.com/FireRedTeam/FireRedVAD) · [silero-vad](https://github.com/snakers4/silero-vad) ·
[onnxruntime](https://github.com/microsoft/onnxruntime) · [Sparkle](https://github.com/sparkle-project/Sparkle)
