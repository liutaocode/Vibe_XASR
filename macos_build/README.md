# Vibe XASR — 本地语音输入法 (macOS · Universal)

按住热键说话,文字直接落到光标处。**100% 本地、离线、不上云**。
原生 Swift app,内置 [X-ASR](https://github.com/Gilgamesh-J/X-ASR) 流式中英识别 + FireRedVAD,
**universal2 通用二进制(Apple Silicon + Intel)**,经 Developer ID 签名 + Apple 公证,可直接分发安装。

> 当前版本 **v1.1.0**(通用版,支持 Intel Mac)。架构图见 [`docs/architecture.html`](docs/architecture.html)。

## 功能

- **三种听写模式**(共用同一引擎,区别只在"何时分句 + 如何落地"):
  - **说完插入** — 松开后整段一次性写入(剪贴板 + ⌘V,最稳)。
  - **逐字插入** — 边说边把识别文本逐字打到光标处(合成 Unicode 键事件)。
  - **持续候机 OnCall** — 麦克风常驻后台,VAD 自动分句,右上角悬浮窗实时显示,
    带「复制 / 查看 / 暂停 / 停止」,停止需确认并切回上一个模式。
- **延迟档位** 160 / 480 / 960 / 1920 ms 可切换;内置 960ms,其余从 HuggingFace **按需下载**(带进度)。
- **VAD 可选** FireRedVAD(默认)/ silero。
- **本地历史**:统计累计字数 + 节省时间;逐条复制/编辑、导出(JSON/txt)、清空(二次确认 + 统计清零)。
  关闭"保存历史"时记录仍**临时保留 60 秒**(带倒计时)再销毁,避免凭空丢失。
- **内置便笺 Pad**、**首启向导**(先体验后授权)、**四语界面**(中/英/日/韩,默认跟随系统)。

## 安装(给用户)

下载 `VibeXASR-1.0.0.dmg` → 双击 → 拖 **Vibe XASR** 进「应用程序」→ 打开。
已公证,Gatekeeper 直接放行,无"未知开发者"拦截。

首次按向导授权 **麦克风**(采集)、**辅助功能**(把文本送进其它 app)、**输入监听**(全局热键)。

## 架构

```
🎙 麦克风 → AVAudioEngine(16kHz) → 分帧+预录 → VAD(fbank+CMVN 判语音段)
         → zipformer 流式 ASR → 文本 → CJK 去空格 → 插入光标 + 存历史 + HUD 显示
```

| 层 | 组成 |
|---|---|
| 交互 (VibeUI) | 菜单栏 `NSStatusItem` · HUD/OnCall 悬浮 `NSPanel` · 设置/历史/便笺/向导窗 · L10n 四语 |
| 输入 | `CGEventTap` 全局热键(按住 Right ⌘,可改)· `AVAudioEngine` 采集 → 16kHz |
| 引擎 (`DictationEngine`) | 分帧+preroll · FireRedVAD/silero(ONNX)· kaldi-native-fbank 80-mel+CMVN · sherpa-onnx zipformer2 transducer(贪心)· CJK 归一化 |
| 输出 | `StreamingInserter`(逐字 Unicode 键事件,清空修饰键)· `Paste`(剪贴板+⌘V)· OnCall 仅显示+手动复制 |
| 数据 | `~/Library/Application Support/VibeXASR/`:history.json · 便笺 · `UserDefaults` · 按需下载的档位模型 |
| 模型 | X-ASR zh-en zipformer(**fp32**,内置 960ms)· FireRedVAD/silero · ONNX Runtime(**CPU**,非 ANE、非量化) |

## 目录

```
xasr_macos_build/
├── native/
│   ├── app/                      # VibeIME 可执行目标(SwiftPM)
│   │   ├── Sources/VibeIME/      # AppDelegate, DictationEngine, SherpaASR, FireRedVAD,
│   │   │                         #   StreamingInserter(Paste), HistoryStore, ModelDownloader,
│   │   │                         #   SettingsStore, Permissions, Mic, Hotkey …
│   │   ├── Resources/            # Info.plist · VibeIME.entitlements
│   │   └── build_app.sh          # SwiftPM 构建 → 组装 .app
│   ├── ui_swift/Sources/VibeUI/  # SwiftUI 库:HUDView, OnCallOverlay, SettingsView,
│   │                             #   HistoryView, OnCallSessionView, L10n, DesignTokens …
│   ├── sherpa/                   # sherpa-onnx 预编译 + Swift 封装
│   ├── firered_shim/             # FireRedVAD C API(onnxruntime)
│   └── dist/                     # 产物:Vibe XASR.app · VibeXASR-1.0.0.dmg
├── models/firered/               # 导出的 FireRedVAD ONNX + CMVN
├── docs/architecture.html        # 架构图
└── package_release.sh            # 一键:构建→签名→公证→staple→dmg
```

## 开发 / 构建

```bash
cd native/app
swift build -c release            # 编译检查
./build_app.sh                    # 组装 → ../dist/Vibe XASR.app
```

一键开发装机(Developer ID 签名,跳过公证,直接装到 /Applications 并启动):

```bash
./package_release.sh dev
```

## 发布(签名 + 公证 + dmg)

```bash
./package_release.sh              # 构建→签名(硬化运行时+entitlements)→公证 app→staple
                                  # →出 dmg→公证 dmg→staple→校验 → dist/VibeXASR-<ver>.dmg
```

需要:Developer ID Application 证书(Team `TEAMID`)+ `notarytool` 钥匙串配置 `vibeime`。
关键 entitlement:`com.apple.security.device.audio-input`(硬化运行时下麦克风授权必需)、
`com.apple.security.cs.disable-library-validation`(加载内置 dylib)。

## 为什么纯 CPU 也能实时(且未量化)

流式 zipformer 每次只处理一个小 chunk(降采样 + 有限上下文),单块计算量小;
Apple Silicon CPU + ONNX Runtime 优化 GEMM,实时因子 ≪ 1,不需要 GPU/ANE。
量化(int8)只是进一步缩体积/提速,非"能否运行"的前提。

## Credits / License

引擎:[X-ASR](https://github.com/Gilgamesh-J/X-ASR)(Apache-2.0)· FireRedVAD(Apache-2.0)·
[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)(Apache-2.0)· silero-vad(MIT)·
kaldi-native-fbank · onnxruntime(MIT)。
