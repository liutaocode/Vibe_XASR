// ============================================================
//  Vibe XASR — Lightweight runtime localization (中英日韩)
//
//  A tiny, dependency-free i18n layer that lives in VibeUI so every SwiftUI
//  surface (HUD / Settings / MenuBar / Onboarding / Pad / History) can localize
//  live without a relaunch. Views observe the shared `L10n` ObservableObject and
//  re-render when `lang` changes.
//
//  Design:
//    * `enum Lang { auto, zh, en, ja, ko }` — `auto` resolves to the system's
//      preferred among {zh,en,ja,ko}, falling back to ENGLISH.
//    * `L10n.shared.t("key")` returns the string for the resolved language,
//      falling back to English, then to the key itself.
//    * The host persists the raw `Lang` choice in SettingsStore via the
//      `L10nPersistence` hook (so VibeUI stays free of UserDefaults knowledge).
//
//  This is UI language only — the ASR pipeline stays zh-en.
// ============================================================

import SwiftUI
import Foundation

/// UI language selection. `auto` follows the system; the rest are explicit.
public enum Lang: String, CaseIterable, Sendable {
    case auto, zh, en, ja, ko

    /// Native display name for the picker.
    public var display: String {
        switch self {
        case .auto: return "Auto"      // re-labelled per active language in the picker
        case .zh:   return "中文"
        case .en:   return "English"
        case .ja:   return "日本語"
        case .ko:   return "한국어"
        }
    }
}

/// Host hook so VibeUI can persist the language choice without importing the
/// app's SettingsStore. The host sets `L10n.shared.persistence` at launch.
/// MainActor-isolated to match `L10n` (and the host's `SettingsStore`).
@MainActor
public protocol L10nPersistence: AnyObject {
    var storedLang: String { get set }
}

@MainActor
public final class L10n: ObservableObject {

    /// Process-wide shared instance the views observe.
    public static let shared = L10n()

    /// The user's raw choice (auto/zh/en/ja/ko). Persisted via `persistence`.
    @Published public var lang: Lang = .auto {
        didSet {
            guard lang != oldValue else { return }
            persistence?.storedLang = lang.rawValue
            objectWillChange.send()
        }
    }

    /// Host-provided persistence (UserDefaults-backed). Optional for previews.
    public weak var persistence: L10nPersistence? {
        didSet {
            if let raw = persistence?.storedLang, let l = Lang(rawValue: raw) {
                // Seed without re-persisting.
                if l != lang { lang = l }
            }
        }
    }

    public init() {}

    /// The concrete language to render in (never `.auto`).
    public var resolved: Lang {
        guard lang == .auto else { return lang }
        return L10n.systemPreferred()
    }

    /// Resolve the system's preferred UI language among the four we support,
    /// falling back to English.
    public static func systemPreferred() -> Lang {
        for code in Locale.preferredLanguages {
            let lower = code.lowercased()
            if lower.hasPrefix("zh") { return .zh }
            if lower.hasPrefix("ja") { return .ja }
            if lower.hasPrefix("ko") { return .ko }
            if lower.hasPrefix("en") { return .en }
        }
        return .en
    }

    /// Translate a key for the resolved language. Falls back: resolved → en → key.
    public func t(_ key: String) -> String {
        let r = resolved
        if let table = L10n.tables[r], let v = table[key] { return v }
        if let v = L10n.tables[.en]?[key] { return v }
        return key
    }

    /// Convenience: translate with a printf-style argument list.
    public func t(_ key: String, _ args: CVarArg...) -> String {
        String(format: t(key), arguments: args)
    }

    /// The "Auto" picker label, localized (so it reads 自动/Auto/自動/자동).
    public func autoLabel() -> String { t("lang.auto") }

    // ============================================================
    // String tables. Keys are stable identifiers used across the UI.
    // Each language is a flat [key: value]. English is the fallback, so it
    // MUST contain every key; the others may omit a key to inherit English.
    // ============================================================

    static let tables: [Lang: [String: String]] = [
        .en: en, .zh: zh, .ja: ja, .ko: ko
    ]

    // ---- English (fallback — complete) -------------------------------------
    static let en: [String: String] = [
        // Generic
        "lang.auto": "Auto",
        "app.name": "Vibe XASR",
        "ok": "OK", "cancel": "Cancel", "done": "Done", "close": "Close",
        "copy": "Copy", "copy.all": "Copy all", "clear": "Clear", "clear.all": "Clear all",
        "delete": "Delete", "download": "Download", "switching": "Switching…",

        // Menu-bar menu
        "menu.loading": "Loading model…",
        "menu.ready": "Ready · hold Right ⌘ to talk",
        "menu.showDock": "Show icon in Dock",
        "menu.settings": "Settings…",
        "menu.pad": "Pad…",
        "menu.history": "History…",
        "menu.rerun": "Re-run setup wizard…",
        "menu.quit": "Quit",

        // Settings — chrome
        "settings.title": "Preferences",
        "settings.window.title": "Vibe XASR Settings",
        "tab.general": "General", "tab.dictation": "Dictation", "tab.model": "Model",
        "tab.records": "Records",
        "tab.permissions": "Permissions", "tab.about": "About",
        "records.empty": "No records yet",

        // Settings — General
        "grp.general": "GENERAL",
        "gen.dock": "Show icon in Dock",
        "gen.dock.help": "Off keeps only the menu-bar icon (pure background); on shows it in the Dock & Launchpad.",
        "gen.launchAtLogin": "Launch at login",
        "gen.launchAtLogin.help": "Start Vibe XASR silently in the background when you log in.",
        "gen.lang": "Interface language",
        "gen.lang.help": "Auto follows your system language (中 / EN / 日 / 한).",

        // Settings — Dictation
        "grp.dictation": "DICTATION",
        "dict.hotkey": "Trigger key (push-to-talk)",
        "dict.hotkey.help": "Click to record, then press the key you want to use.",
        "dict.insert": "Insert method",
        "dict.insert.help": "Paste is fastest; type-out is more compatible with old apps.",
        "dict.insert.paste": "Insert all at once",
        "dict.insert.type": "Insert character by character",
        "dict.mode": "Dictation mode",
        "dict.mode.paste.title": "Insert when done",
        "dict.mode.paste.desc": "Writes the whole sentence at the cursor once you finish speaking.",
        "dict.mode.type.title": "Insert as you speak",
        "dict.mode.type.desc": "Types into the field character by character while you talk.",
        "dict.mode.oncall.title": "Always On · OnCall",
        "dict.mode.oncall.desc": "Runs continuously in the background and starts writing the moment it hears speech; the recognized text stays visible in the top-right corner — always OnCall for you. Keeps the microphone listening while enabled.",
        "dict.clipOverwrite": "Overwrite clipboard after each dictation",
        "dict.clipOverwrite.help": "Leaves the dictated text on the clipboard so you can paste it anywhere.",
        "dict.padWrite": "Write dictation into Pad",
        "dict.padWrite.help": "Every final result is also appended to the built-in Pad.",
        "dict.history": "Save history locally",
        "dict.history.help": "Keep every dictation result on this device (see the History window). When off, records are kept 60s with a countdown then deleted — so a dictation isn't lost the instant it ends.",

        // Settings — Model (VAD / tiers)
        "grp.vadasr": "VAD / ASR",
        "grp.xasr": "X-ASR STREAMING MODEL",
        "grp.vad": "VAD (advanced)",
        "model.headline.title": "X-ASR · Chinese-English streaming ASR",
        "model.headline.repo": "GilgameshWind/X-ASR-zh-en",
        "model.source": "Model source",
        "model.source.help": "ModelScope is the default (faster in most regions). Switch to HuggingFace if a download stalls.",
        "model.switching.banner": "Switching model… the engine is rebuilding",
        "model.dl.starting": "Starting download…",
        "model.vad": "Voice activity detection (VAD)",
        "model.vad.help": "Detects speech vs. silence to decide when to cut sentences.",
        "model.vad.fire": "FireRedVAD (default)",
        "model.vad.silero": "silero",
        "model.tier": "Latency tier",
        "model.tier.help": "Smaller = faster, larger = more context. Only 960 ms ships built-in; others download on demand.",
        "model.tier.160.name": "160 ms",
        "model.tier.160.scene": "Real-time interaction / live captions",
        "model.tier.480.name": "480 ms",
        "model.tier.480.scene": "Low latency + more context",
        "model.tier.960.name": "960 ms",
        "model.tier.960.scene": "Steadier, tolerates a little latency",
        "model.tier.1920.name": "1920 ms",
        "model.tier.1920.scene": "Most context, close to offline",
        "model.aslang": "Recognition language",
        "model.aslang.zhen": "Chinese + English (zh-en)",
        "grp.models": "MODEL MANAGEMENT",
        "model.bundled": "Built-in",
        "model.downloaded": "Downloaded",
        "model.notDownloaded": "Not downloaded",
        "model.downloading": "Downloading %d%%",
        "model.active": "In use",
        "model.use": "Use",
        "model.tierRow": "Streaming model · %@",
        "model.vadRow": "VAD model",
        "model.dl.failed": "Download failed — tap to retry",

        // Settings — Permissions
        "grp.permissions": "PERMISSIONS",
        "perm.mic": "Microphone",
        "perm.mic.help": "Used to capture your voice locally; audio never leaves the device.",
        "perm.a11y": "Accessibility",
        "perm.a11y.help": "Used to insert recognized text into the current field.",
        "perm.input": "Input Monitoring",
        "perm.input.help": "Used to detect the trigger hotkey you press.",
        "perm.granted": "✓ Granted",
        "perm.denied": "✕ Not granted",
        "perm.openSettings": "Open System Settings",
        "perm.recheck": "Re-check",
        "perm.checking": "Checking…",
        "perm.banner.ok": "All permissions granted — Vibe XASR is ready ✓",
        "perm.banner.warn": "Some permissions are missing; parts won't work. Open System Settings for each, then come back.",

        // Settings — About
        "about.version": "Version %@ · macOS 15+ · Universal (Apple Silicon + Intel)",
        "about.checkUpdate": "Check for updates",
        "about.credits": "Also built on",
        "about.local": "100% local · offline · your data never leaves the device",
        "about.xasr.title": "Powered by X-ASR",
        "about.xasr.desc": "The Chinese-English streaming speech-recognition model at the heart of this app.",
        "about.xasr.repo": "🤗 GilgameshWind/X-ASR-zh-en",

        // HUD
        "hud.listening": "Listening…",
        "hud.inserted": "Inserted",
        "hud.cancelled": "Cancelled",
        "hud.goSettings": "Settings",
        "hud.insertedAt": "Inserted at cursor",
        "hud.releaseHint": "Release to drop · Esc to cancel",
        "hud.copyAll": "Copy all",
        "hud.micFail": "Can't start microphone",

        // Pad
        "pad.title": "Pad",
        "pad.placeholder": "Dictate or type here. Turn on \"Write dictation into Pad\" in Settings to append finals.",
        "pad.append": "Append dictation",
        "pad.cleared": "Cleared",

        // History
        "history.title": "History",
        "history.privacy": "Your data stays on this device — never uploaded.",
        "history.empty": "No dictation history yet.",
        "history.count": "%d entries",
        "history.copied": "Copied",
        "history.edit": "Edit",
        "history.save": "Save",
        "history.export": "Export",
        "history.export.panel": "Export history",
        "history.showOnCall": "Show OnCall content",
        "history.oncall.badge": "OnCall",
        "history.stats.chars": "%@ chars dictated",
        "history.stats.minutes": " · %@ min saved",
        "history.stats.hours": " · %@ h saved",
        "history.stats.big": ">10000 chars dictated · >100 hours saved",

        // Onboarding (kept concise; full flow already bilingual-friendly)
        "onbo.welcome.title": "Hold the button below and say something",
        "onbo.welcome.local": "100% local · what you say never leaves this device",
    ]

    // ---- 中文 --------------------------------------------------------------
    static let zh: [String: String] = [
        "lang.auto": "自动",
        "app.name": "Vibe XASR",
        "ok": "好", "cancel": "取消", "done": "完成", "close": "关闭",
        "copy": "复制", "copy.all": "复制全文", "clear": "清空", "clear.all": "全部清空",
        "delete": "删除", "download": "下载", "switching": "切换中…",

        "menu.loading": "正在加载模型…",
        "menu.ready": "就绪 · 按住右⌘说话",
        "menu.showDock": "在 Dock 显示图标",
        "menu.settings": "设置…",
        "menu.pad": "便笺…",
        "menu.history": "历史…",
        "menu.rerun": "重新运行向导…",
        "menu.quit": "退出",

        "settings.title": "偏好设置",
        "settings.window.title": "Vibe XASR 设置",
        "tab.general": "通用", "tab.dictation": "听写", "tab.model": "模型",
        "tab.records": "记录",
        "tab.permissions": "权限", "tab.about": "关于",
        "records.empty": "暂无记录",

        "grp.general": "通用",
        "gen.dock": "在 Dock 显示图标",
        "gen.dock.help": "关闭后仅保留菜单栏图标(纯后台);开启可在程序坞与启动台找到。",
        "gen.launchAtLogin": "开机自启动",
        "gen.launchAtLogin.help": "登录时在后台静默启动 Vibe XASR。",
        "gen.lang": "界面语言",
        "gen.lang.help": "自动会跟随系统语言(中 / EN / 日 / 한)。",

        "grp.dictation": "听写",
        "dict.hotkey": "触发键(push-to-talk)",
        "dict.hotkey.help": "点一下开始录制,然后按下你想用的键。",
        "dict.insert": "插入方式",
        "dict.insert.help": "粘贴更快;逐字更兼容老应用。",
        "dict.insert.paste": "说完一次性插入",
        "dict.insert.type": "逐字插入",
        "dict.mode": "听写模式",
        "dict.mode.paste.title": "说完插入",
        "dict.mode.paste.desc": "说完一句,一次性写入光标处。",
        "dict.mode.type.title": "逐字插入",
        "dict.mode.type.desc": "边说边逐字写入输入框。",
        "dict.mode.oncall.title": "持续候机 · OnCall",
        "dict.mode.oncall.desc": "持续后台运行,识别到说话即开始写入;右上角持续显示识别内容,永远为你 OnCall。开启后会一直监听麦克风。",
        "dict.clipOverwrite": "每次说话覆盖剪贴板",
        "dict.clipOverwrite.help": "把听写文本留在剪贴板,方便粘贴到任意位置。",
        "dict.padWrite": "听写写入便笺",
        "dict.padWrite.help": "每条最终结果同时追加到内置便笺。",
        "dict.history": "本地保存历史",
        "dict.history.help": "把每条听写结果保存在本设备(见「历史」窗口)。关闭后,记录仅临时保留 60 秒(点记录看倒计时)再销毁——避免说完就凭空消失。",

        "grp.vadasr": "VAD / ASR",
        "grp.xasr": "X-ASR 流式模型",
        "grp.vad": "VAD(进阶)",
        "model.headline.title": "X-ASR · 中英文流式语音识别",
        "model.headline.repo": "GilgameshWind/X-ASR-zh-en",
        "model.source": "模型来源",
        "model.source.help": "默认 ModelScope（多数地区更快）。若下载卡住可切换到 HuggingFace。",
        "model.switching.banner": "正在切换模型…引擎重建中",
        "model.dl.starting": "正在开始下载…",
        "model.vad": "语音活动检测 (VAD)",
        "model.vad.help": "检测「在说话 / 静音」,决定何时断句。",
        "model.vad.fire": "FireRedVAD(默认)",
        "model.vad.silero": "silero",
        "model.tier": "延迟档",
        "model.tier.help": "越小越快、越大上下文越多。仅 960ms 已内置,其余按需下载。",
        "model.tier.160.name": "160ms",
        "model.tier.160.scene": "实时交互 / 直播字幕",
        "model.tier.480.name": "480ms",
        "model.tier.480.scene": "低延迟 + 更多上下文",
        "model.tier.960.name": "960ms",
        "model.tier.960.scene": "更稳,容忍少量延迟",
        "model.tier.1920.name": "1920ms",
        "model.tier.1920.scene": "上下文最多,接近离线",
        "model.aslang": "识别语言",
        "model.aslang.zhen": "中英 (zh-en)",
        "grp.models": "模型管理",
        "model.bundled": "已内置",
        "model.downloaded": "已下载",
        "model.notDownloaded": "未下载",
        "model.downloading": "下载中 %d%%",
        "model.active": "使用中",
        "model.use": "启用",
        "model.tierRow": "流式模型 · %@",
        "model.vadRow": "VAD 模型",
        "model.dl.failed": "下载失败 — 点击重试",

        "grp.permissions": "权限",
        "perm.mic": "麦克风",
        "perm.mic.help": "用于本地采集你的语音,音频不离开设备。",
        "perm.a11y": "辅助功能",
        "perm.a11y.help": "用于把识别出的文字插入到当前输入框。",
        "perm.input": "输入监控",
        "perm.input.help": "用于检测你按下的触发热键。",
        "perm.granted": "✓ 已授权",
        "perm.denied": "✕ 未授权",
        "perm.openSettings": "打开系统设置",
        "perm.recheck": "重新检测",
        "perm.checking": "检测中…",
        "perm.banner.ok": "全部权限已授权,Vibe XASR 可以正常工作 ✓",
        "perm.banner.warn": "还有权限未开启,部分功能不可用。逐项「打开系统设置」后回到这里。",

        "about.version": "版本 %@ · macOS 15+ · 通用版(Apple 芯片 + Intel)",
        "about.checkUpdate": "检查更新",
        "about.credits": "同时基于",
        "about.local": "100% 本地 · 不联网 · 数据不出设备",
        "about.xasr.title": "由 X-ASR 驱动",
        "about.xasr.desc": "本应用核心的中英文流式语音识别模型。",
        "about.xasr.repo": "🤗 GilgameshWind/X-ASR-zh-en",

        "hud.listening": "在听…",
        "hud.inserted": "已插入",
        "hud.cancelled": "已取消",
        "hud.goSettings": "去设置",
        "hud.insertedAt": "已插入到光标处",
        "hud.releaseHint": "松开落字 · Esc 取消",
        "hud.copyAll": "复制全文",
        "hud.micFail": "无法启动麦克风",

        "pad.title": "便笺",
        "pad.placeholder": "在这里听写或输入。在「设置」开启「听写写入便笺」即可自动追加结果。",
        "pad.append": "追加听写",
        "pad.cleared": "已清空",

        "history.title": "历史",
        "history.privacy": "您的数据永远保存在本地,绝不上云。",
        "history.empty": "还没有听写历史。",
        "history.count": "%d 条",
        "history.copied": "已复制",
        "history.edit": "编辑",
        "history.save": "保存",
        "history.export": "导出",
        "history.export.panel": "导出历史",
        "history.showOnCall": "显示 OnCall 内容",
        "history.oncall.badge": "OnCall",
        "history.stats.chars": "累计 %@ 字",
        "history.stats.minutes": " · 节省 %@ 分钟",
        "history.stats.hours": " · 节省 %@ 小时",
        "history.stats.big": "累计 >10000 字 · 节省 >100 小时",

        "onbo.welcome.title": "按住下面的按钮,说一句话试试",
        "onbo.welcome.local": "100% 本地 · 你说的不出这台设备",
    ]

    // ---- 日本語 -------------------------------------------------------------
    static let ja: [String: String] = [
        "lang.auto": "自動",
        "app.name": "Vibe XASR",
        "ok": "OK", "cancel": "キャンセル", "done": "完了", "close": "閉じる",
        "copy": "コピー", "copy.all": "全文をコピー", "clear": "消去", "clear.all": "すべて消去",
        "delete": "削除", "download": "ダウンロード", "switching": "切り替え中…",

        "menu.loading": "モデルを読み込み中…",
        "menu.ready": "準備完了 · 右⌘長押しで話す",
        "menu.showDock": "Dock にアイコンを表示",
        "menu.settings": "設定…",
        "menu.pad": "メモ…",
        "menu.history": "履歴…",
        "menu.rerun": "セットアップを再実行…",
        "menu.quit": "終了",

        "settings.title": "環境設定",
        "settings.window.title": "Vibe XASR 設定",
        "tab.general": "一般", "tab.dictation": "音声入力", "tab.model": "モデル",
        "tab.records": "記録",
        "tab.permissions": "権限", "tab.about": "情報",
        "records.empty": "記録はまだありません",

        "grp.general": "一般",
        "gen.dock": "Dock にアイコンを表示",
        "gen.dock.help": "オフではメニューバーのみ(完全バックグラウンド)。オンで Dock と Launchpad に表示。",
        "gen.launchAtLogin": "ログイン時に起動",
        "gen.launchAtLogin.help": "ログイン時に Vibe XASR をバックグラウンドで静かに起動します。",
        "gen.lang": "表示言語",
        "gen.lang.help": "自動はシステム言語に従います(中 / EN / 日 / 한)。",

        "grp.dictation": "音声入力",
        "dict.hotkey": "トリガーキー(プッシュ・トゥ・トーク)",
        "dict.hotkey.help": "クリックして録音し、使いたいキーを押してください。",
        "dict.insert": "挿入方法",
        "dict.insert.help": "ペーストが最速。一文字ずつは古いアプリと互換性が高い。",
        "dict.insert.paste": "話し終えたら一括で挿入",
        "dict.insert.type": "一文字ずつ挿入",
        "dict.mode": "音声入力モード",
        "dict.mode.paste.title": "話し終えたら挿入",
        "dict.mode.paste.desc": "一文を話し終えると、カーソル位置にまとめて書き込みます。",
        "dict.mode.type.title": "一文字ずつ挿入",
        "dict.mode.type.desc": "話しながら一文字ずつ入力欄へ書き込みます。",
        "dict.mode.oncall.title": "常時待機 · OnCall",
        "dict.mode.oncall.desc": "バックグラウンドで常時動作し、発話を検知するとすぐに書き込みを開始します。認識中のテキストは右上に表示され続け、いつでもあなたを OnCall でサポート。オンの間はマイクを常に監視します。",
        "dict.clipOverwrite": "音声入力のたびにクリップボードを上書き",
        "dict.clipOverwrite.help": "音声入力したテキストをクリップボードに残し、どこにでも貼り付けられます。",
        "dict.padWrite": "音声入力をメモに書き込む",
        "dict.padWrite.help": "確定結果を内蔵メモにも追記します。",
        "dict.history": "履歴をローカルに保存",
        "dict.history.help": "すべての音声入力結果をこの端末に保存します(「履歴」ウィンドウ参照)。オフのときは記録を60秒だけ保持(カウントダウン表示)して削除します——直後に消えないように。",

        "grp.vadasr": "VAD / ASR",
        "grp.xasr": "X-ASR ストリーミングモデル",
        "grp.vad": "VAD(詳細)",
        "model.headline.title": "X-ASR · 中国語・英語ストリーミング音声認識",
        "model.headline.repo": "GilgameshWind/X-ASR-zh-en",
        "model.source": "モデルの入手元",
        "model.source.help": "既定は ModelScope（多くの地域で高速）。ダウンロードが進まない場合は HuggingFace に切り替えてください。",
        "model.switching.banner": "モデルを切り替え中…エンジンを再構築しています",
        "model.dl.starting": "ダウンロードを開始中…",
        "model.vad": "音声区間検出 (VAD)",
        "model.vad.help": "発話と無音を検出し、文の区切りを決めます。",
        "model.vad.fire": "FireRedVAD(既定)",
        "model.vad.silero": "silero",
        "model.tier": "レイテンシ段階",
        "model.tier.help": "小さいほど速く、大きいほど文脈が豊富。960ms のみ内蔵、他は必要時にダウンロード。",
        "model.tier.160.name": "160ms",
        "model.tier.160.scene": "リアルタイム / ライブ字幕",
        "model.tier.480.name": "480ms",
        "model.tier.480.scene": "低遅延 + より多くの文脈",
        "model.tier.960.name": "960ms",
        "model.tier.960.scene": "安定、わずかな遅延を許容",
        "model.tier.1920.name": "1920ms",
        "model.tier.1920.scene": "文脈最大、オフラインに近い",
        "model.aslang": "認識言語",
        "model.aslang.zhen": "中国語 + 英語 (zh-en)",
        "grp.models": "モデル管理",
        "model.bundled": "内蔵",
        "model.downloaded": "ダウンロード済み",
        "model.notDownloaded": "未ダウンロード",
        "model.downloading": "ダウンロード中 %d%%",
        "model.active": "使用中",
        "model.use": "使用",
        "model.tierRow": "ストリーミングモデル · %@",
        "model.vadRow": "VAD モデル",
        "model.dl.failed": "ダウンロード失敗 — タップで再試行",

        "grp.permissions": "権限",
        "perm.mic": "マイク",
        "perm.mic.help": "音声をローカルで取得します。音声は端末から出ません。",
        "perm.a11y": "アクセシビリティ",
        "perm.a11y.help": "認識結果を現在の入力欄に挿入するために使用します。",
        "perm.input": "入力監視",
        "perm.input.help": "押されたトリガーキーを検出するために使用します。",
        "perm.granted": "✓ 許可済み",
        "perm.denied": "✕ 未許可",
        "perm.openSettings": "システム設定を開く",
        "perm.recheck": "再確認",
        "perm.checking": "確認中…",
        "perm.banner.ok": "すべての権限が許可されています。Vibe XASR は正常に動作します ✓",
        "perm.banner.warn": "未許可の権限があります。一部機能が使えません。各項目で「システム設定を開く」後に戻ってください。",

        "about.version": "バージョン %@ · macOS 15+ · ユニバーサル(Apple Silicon + Intel)",
        "about.checkUpdate": "アップデートを確認",
        "about.credits": "さらに、次の技術を利用",
        "about.local": "100% ローカル · オフライン · データは端末外に出ません",
        "about.xasr.title": "X-ASR を搭載",
        "about.xasr.desc": "本アプリの中核となる中国語・英語ストリーミング音声認識モデル。",
        "about.xasr.repo": "🤗 GilgameshWind/X-ASR-zh-en",

        "hud.listening": "聞いています…",
        "hud.inserted": "挿入済み",
        "hud.cancelled": "キャンセル",
        "hud.goSettings": "設定へ",
        "hud.insertedAt": "カーソル位置に挿入しました",
        "hud.releaseHint": "離すと入力 · Esc で取消",
        "hud.copyAll": "全文をコピー",
        "hud.micFail": "マイクを起動できません",

        "pad.title": "メモ",
        "pad.placeholder": "ここで音声入力または入力します。設定で「音声入力をメモに書き込む」をオンにすると自動追記します。",
        "pad.append": "音声入力を追記",
        "pad.cleared": "消去しました",

        "history.title": "履歴",
        "history.privacy": "あなたのデータは常にこの端末に保存され、クラウドには送信されません。",
        "history.empty": "音声入力の履歴はまだありません。",
        "history.count": "%d 件",
        "history.copied": "コピーしました",
        "history.edit": "編集",
        "history.save": "保存",
        "history.export": "書き出し",
        "history.export.panel": "履歴を書き出し",
        "history.showOnCall": "OnCall の内容を表示",
        "history.oncall.badge": "OnCall",
        "history.stats.chars": "累計 %@ 文字",
        "history.stats.minutes": " · %@ 分節約",
        "history.stats.hours": " · %@ 時間節約",
        "history.stats.big": "累計 >10000 文字 · >100 時間節約",

        "onbo.welcome.title": "下のボタンを長押しして、一言話してみてください",
        "onbo.welcome.local": "100% ローカル · あなたの声はこの端末から出ません",
    ]

    // ---- 한국어 -------------------------------------------------------------
    static let ko: [String: String] = [
        "lang.auto": "자동",
        "app.name": "Vibe XASR",
        "ok": "확인", "cancel": "취소", "done": "완료", "close": "닫기",
        "copy": "복사", "copy.all": "전체 복사", "clear": "지우기", "clear.all": "모두 지우기",
        "delete": "삭제", "download": "다운로드", "switching": "전환 중…",

        "menu.loading": "모델 로드 중…",
        "menu.ready": "준비됨 · 오른쪽 ⌘ 누른 채 말하기",
        "menu.showDock": "Dock에 아이콘 표시",
        "menu.settings": "설정…",
        "menu.pad": "메모…",
        "menu.history": "기록…",
        "menu.rerun": "설정 마법사 다시 실행…",
        "menu.quit": "종료",

        "settings.title": "환경설정",
        "settings.window.title": "Vibe XASR 설정",
        "tab.general": "일반", "tab.dictation": "받아쓰기", "tab.model": "모델",
        "tab.records": "기록",
        "tab.permissions": "권한", "tab.about": "정보",
        "records.empty": "아직 기록이 없습니다",

        "grp.general": "일반",
        "gen.dock": "Dock에 아이콘 표시",
        "gen.dock.help": "끄면 메뉴 막대 아이콘만(완전 백그라운드). 켜면 Dock과 Launchpad에 표시.",
        "gen.launchAtLogin": "로그인 시 실행",
        "gen.launchAtLogin.help": "로그인할 때 Vibe XASR를 백그라운드에서 조용히 시작합니다.",
        "gen.lang": "인터페이스 언어",
        "gen.lang.help": "자동은 시스템 언어를 따릅니다(中 / EN / 日 / 한).",

        "grp.dictation": "받아쓰기",
        "dict.hotkey": "트리거 키(푸시 투 토크)",
        "dict.hotkey.help": "클릭하여 녹음한 후 사용할 키를 누르세요.",
        "dict.insert": "삽입 방식",
        "dict.insert.help": "붙여넣기가 가장 빠르고, 한 글자씩은 구형 앱과 호환성이 높습니다.",
        "dict.insert.paste": "말을 마치면 한 번에 삽입",
        "dict.insert.type": "한 글자씩 삽입",
        "dict.mode": "받아쓰기 모드",
        "dict.mode.paste.title": "말을 마치면 삽입",
        "dict.mode.paste.desc": "한 문장을 말하고 나면 커서 위치에 한 번에 입력합니다.",
        "dict.mode.type.title": "한 글자씩 삽입",
        "dict.mode.type.desc": "말하는 동안 입력란에 한 글자씩 입력합니다.",
        "dict.mode.oncall.title": "상시 대기 · OnCall",
        "dict.mode.oncall.desc": "백그라운드에서 계속 실행되며 말소리를 감지하는 즉시 입력을 시작합니다. 인식된 텍스트는 오른쪽 위에 계속 표시되어 언제나 당신을 위해 OnCall 대기합니다. 켜져 있는 동안 마이크를 계속 모니터링합니다.",
        "dict.clipOverwrite": "받아쓰기마다 클립보드 덮어쓰기",
        "dict.clipOverwrite.help": "받아쓴 텍스트를 클립보드에 남겨 어디에나 붙여넣을 수 있습니다.",
        "dict.padWrite": "받아쓰기를 메모에 기록",
        "dict.padWrite.help": "모든 최종 결과를 내장 메모에도 추가합니다.",
        "dict.history": "기록을 로컬에 저장",
        "dict.history.help": "모든 받아쓰기 결과를 이 기기에 보관합니다(‘기록’ 창 참고). 끄면 기록을 60초 동안만 보관(카운트다운 표시) 후 삭제합니다 — 끝나자마자 사라지지 않도록.",

        "grp.vadasr": "VAD / ASR",
        "grp.xasr": "X-ASR 스트리밍 모델",
        "grp.vad": "VAD(고급)",
        "model.headline.title": "X-ASR · 중국어·영어 스트리밍 음성 인식",
        "model.headline.repo": "GilgameshWind/X-ASR-zh-en",
        "model.source": "모델 출처",
        "model.source.help": "기본값은 ModelScope(대부분 지역에서 더 빠름). 다운로드가 멈추면 HuggingFace로 전환하세요.",
        "model.switching.banner": "모델 전환 중… 엔진을 다시 빌드하는 중입니다",
        "model.dl.starting": "다운로드 시작 중…",
        "model.vad": "음성 활동 감지 (VAD)",
        "model.vad.help": "말하기와 침묵을 감지하여 문장 구분 시점을 결정합니다.",
        "model.vad.fire": "FireRedVAD(기본)",
        "model.vad.silero": "silero",
        "model.tier": "지연 단계",
        "model.tier.help": "작을수록 빠르고, 클수록 문맥이 풍부합니다. 960ms만 내장, 나머지는 필요 시 다운로드.",
        "model.tier.160.name": "160ms",
        "model.tier.160.scene": "실시간 상호작용 / 라이브 자막",
        "model.tier.480.name": "480ms",
        "model.tier.480.scene": "낮은 지연 + 더 많은 문맥",
        "model.tier.960.name": "960ms",
        "model.tier.960.scene": "더 안정적, 약간의 지연 허용",
        "model.tier.1920.name": "1920ms",
        "model.tier.1920.scene": "문맥 최대, 오프라인에 가까움",
        "model.aslang": "인식 언어",
        "model.aslang.zhen": "중국어 + 영어 (zh-en)",
        "grp.models": "모델 관리",
        "model.bundled": "내장",
        "model.downloaded": "다운로드됨",
        "model.notDownloaded": "다운로드 안 됨",
        "model.downloading": "다운로드 중 %d%%",
        "model.active": "사용 중",
        "model.use": "사용",
        "model.tierRow": "스트리밍 모델 · %@",
        "model.vadRow": "VAD 모델",
        "model.dl.failed": "다운로드 실패 — 눌러서 재시도",

        "grp.permissions": "권한",
        "perm.mic": "마이크",
        "perm.mic.help": "음성을 로컬에서 수집합니다. 오디오는 기기를 떠나지 않습니다.",
        "perm.a11y": "손쉬운 사용",
        "perm.a11y.help": "인식된 텍스트를 현재 입력란에 삽입하는 데 사용합니다.",
        "perm.input": "입력 모니터링",
        "perm.input.help": "누른 트리거 단축키를 감지하는 데 사용합니다.",
        "perm.granted": "✓ 허용됨",
        "perm.denied": "✕ 허용 안 됨",
        "perm.openSettings": "시스템 설정 열기",
        "perm.recheck": "다시 확인",
        "perm.checking": "확인 중…",
        "perm.banner.ok": "모든 권한이 허용되었습니다 — Vibe XASR를 사용할 수 있습니다 ✓",
        "perm.banner.warn": "허용되지 않은 권한이 있어 일부 기능을 사용할 수 없습니다. 항목별로 ‘시스템 설정 열기’ 후 돌아오세요.",

        "about.version": "버전 %@ · macOS 15+ · 유니버설(Apple Silicon + Intel)",
        "about.checkUpdate": "업데이트 확인",
        "about.credits": "또한 다음 기술 기반",
        "about.local": "100% 로컬 · 오프라인 · 데이터가 기기를 떠나지 않습니다",
        "about.xasr.title": "X-ASR 기반",
        "about.xasr.desc": "이 앱의 핵심인 중국어·영어 스트리밍 음성 인식 모델입니다.",
        "about.xasr.repo": "🤗 GilgameshWind/X-ASR-zh-en",

        "hud.listening": "듣는 중…",
        "hud.inserted": "삽입됨",
        "hud.cancelled": "취소됨",
        "hud.goSettings": "설정으로",
        "hud.insertedAt": "커서 위치에 삽입했습니다",
        "hud.releaseHint": "놓으면 입력 · Esc로 취소",
        "hud.copyAll": "전체 복사",
        "hud.micFail": "마이크를 시작할 수 없습니다",

        "pad.title": "메모",
        "pad.placeholder": "여기에서 받아쓰거나 입력하세요. 설정에서 ‘받아쓰기를 메모에 기록’을 켜면 자동으로 추가됩니다.",
        "pad.append": "받아쓰기 추가",
        "pad.cleared": "지웠습니다",

        "history.title": "기록",
        "history.privacy": "귀하의 데이터는 항상 이 기기에 저장되며 클라우드에 업로드되지 않습니다.",
        "history.empty": "아직 받아쓰기 기록이 없습니다.",
        "history.count": "%d개",
        "history.copied": "복사됨",
        "history.edit": "편집",
        "history.save": "저장",
        "history.export": "내보내기",
        "history.export.panel": "기록 내보내기",
        "history.showOnCall": "OnCall 내용 표시",
        "history.oncall.badge": "OnCall",
        "history.stats.chars": "누적 %@자",
        "history.stats.minutes": " · %@분 절약",
        "history.stats.hours": " · %@시간 절약",
        "history.stats.big": "누적 >10000자 · >100시간 절약",

        "onbo.welcome.title": "아래 버튼을 누른 채 한마디 해 보세요",
        "onbo.welcome.local": "100% 로컬 · 당신의 말은 이 기기를 떠나지 않습니다",
    ]
}
