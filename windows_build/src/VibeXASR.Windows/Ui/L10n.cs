using System;
using System.Collections.Generic;
using System.Globalization;

namespace VibeXASR.Windows.Ui;

/// <summary>UI language selection. <c>Auto</c> follows the system; the rest are explicit.</summary>
public enum Lang { Auto, Zh, En, Ja, Ko }

/// <summary>
/// Dependency-free runtime localization, ported from the macOS app's <c>L10n.swift</c>.
/// Keys are identical to the macOS table so the surfaces stay 1:1; printf-style
/// placeholders were converted to .NET composite-format (<c>{0}</c>), and a handful of
/// macOS-only phrasings (⌘, Dock, System Settings, "macOS 13+") were adapted to Windows.
/// English is the complete fallback; the others inherit any missing key from English.
/// </summary>
public static class L10n
{
    /// <summary>Raised when <see cref="Current"/> changes, so open windows can re-render.</summary>
    public static event Action? LanguageChanged;

    private static Lang _current = Lang.Auto;
    public static Lang Current
    {
        get => _current;
        set { if (_current != value) { _current = value; LanguageChanged?.Invoke(); } }
    }

    /// <summary>Map a persisted code ("auto"/"zh"/...) to a <see cref="Lang"/>.</summary>
    public static Lang FromCode(string? code) => code?.ToLowerInvariant() switch
    {
        "zh" => Lang.Zh, "en" => Lang.En, "ja" => Lang.Ja, "ko" => Lang.Ko, _ => Lang.Auto,
    };

    public static string ToCode(Lang l) => l switch
    {
        Lang.Zh => "zh", Lang.En => "en", Lang.Ja => "ja", Lang.Ko => "ko", _ => "auto",
    };

    /// <summary>Native display name for the picker.</summary>
    public static string Display(Lang l) => l switch
    {
        Lang.Auto => T("lang.auto"),
        Lang.Zh => "中文", Lang.En => "English", Lang.Ja => "日本語", Lang.Ko => "한국어",
        _ => "English",
    };

    /// <summary>The concrete language to render (never Auto).</summary>
    public static Lang Resolved => Current == Lang.Auto ? SystemPreferred() : Current;

    private static Lang SystemPreferred()
    {
        var name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return name switch { "zh" => Lang.Zh, "ja" => Lang.Ja, "ko" => Lang.Ko, _ => Lang.En };
    }

    /// <summary>Translate a key: resolved language → English → the key itself.</summary>
    public static string T(string key)
    {
        var r = Resolved;
        if (Tables.TryGetValue(r, out var table) && table.TryGetValue(key, out var v)) return v;
        if (Tables[Lang.En].TryGetValue(key, out var en)) return en;
        return key;
    }

    /// <summary>Translate + composite-format ({0}, {1}, …).</summary>
    public static string T(string key, params object[] args) => string.Format(T(key), args);

    // Lazy so it builds AFTER the per-language dictionaries below are initialized
    // (static field initializers run in textual order; En/Zh/Ja/Ko are declared later).
    private static Dictionary<Lang, Dictionary<string, string>>? _tables;
    private static Dictionary<Lang, Dictionary<string, string>> Tables =>
        _tables ??= new()
        {
            [Lang.En] = En, [Lang.Zh] = Zh, [Lang.Ja] = Ja, [Lang.Ko] = Ko,
        };

    // ---- English (fallback — complete) ----
    private static readonly Dictionary<string, string> En = new()
    {
        ["lang.auto"] = "Auto",
        ["app.name"] = "Vibe XASR",
        ["ok"] = "OK", ["cancel"] = "Cancel", ["done"] = "Done", ["close"] = "Close",
        ["copy"] = "Copy", ["copy.all"] = "Copy all", ["clear"] = "Clear", ["clear.all"] = "Clear all",
        ["delete"] = "Delete", ["download"] = "Download", ["switching"] = "Switching…",

        ["menu.loading"] = "Loading model…",
        ["menu.ready"] = "Ready · hold Right Ctrl to talk",
        ["menu.listening"] = "Listening…",
        ["menu.recent"] = "Most recent",
        ["menu.enable"] = "Enable dictation",
        ["menu.settings"] = "Settings…",
        ["menu.pad"] = "Pad…",
        ["menu.history"] = "History…",
        ["menu.quit"] = "Quit Vibe XASR",

        ["settings.title"] = "Preferences",
        ["settings.window.title"] = "Vibe XASR Settings",
        ["tab.general"] = "General", ["tab.dictation"] = "Dictation", ["tab.model"] = "Model",
        ["tab.records"] = "Records",
        ["tab.permissions"] = "Permissions", ["tab.about"] = "About",
        ["records.empty"] = "No records yet",

        ["grp.general"] = "GENERAL",
        ["gen.launchAtLogin"] = "Launch at login",
        ["gen.launchAtLogin.help"] = "Start Vibe XASR silently in the background when you sign in.",
        ["gen.tray"] = "Show tray icon",
        ["gen.tray.help"] = "Keep the notification-area icon visible for quick access to the menu.",
        ["gen.lang"] = "Interface language",
        ["gen.lang.help"] = "Auto follows your system language (中 / EN / 日 / 한).",

        ["grp.dictation"] = "DICTATION",
        ["dict.hotkey"] = "Trigger key (push-to-talk)",
        ["dict.hotkey.help"] = "Click to record, then press the key you want to use.",
        ["dict.hotkey.recording"] = "Press a key…",
        ["dict.mode"] = "Dictation mode",
        ["dict.mode.paste.title"] = "Insert when done",
        ["dict.mode.paste.desc"] = "Writes the whole sentence at the cursor once you finish speaking.",
        ["dict.mode.type.title"] = "Insert as you speak",
        ["dict.mode.type.desc"] = "Types into the field character by character while you talk.",
        ["dict.mode.oncall.title"] = "Always On · OnCall",
        ["dict.mode.oncall.desc"] = "Runs continuously in the background and starts writing the moment it hears speech; the recognized text stays visible in an overlay — always OnCall for you. Keeps the microphone listening while enabled.",
        ["dict.clipOverwrite"] = "Overwrite clipboard after each dictation",
        ["dict.clipOverwrite.help"] = "Leaves the dictated text on the clipboard so you can paste it anywhere.",
        ["dict.history"] = "Save history locally",
        ["dict.history.help"] = "Keep every dictation result on this device (see the History window). When off, records are kept 60s with a countdown then deleted — so a dictation isn't lost the instant it ends.",

        ["grp.xasr"] = "X-ASR STREAMING MODEL",
        ["grp.vad"] = "VAD (advanced)",
        ["model.headline.title"] = "X-ASR · Chinese-English streaming ASR",
        ["model.headline.repo"] = "GilgameshWind/X-ASR-zh-en",
        ["model.source"] = "Model source",
        ["model.source.value"] = "huggingface.co/GilgameshWind/X-ASR-zh-en",
        ["model.switching.banner"] = "Switching model… the engine is rebuilding",
        ["model.dl.starting"] = "Starting download…",
        ["model.vad"] = "Voice activity detection (VAD)",
        ["model.vad.help"] = "Detects speech vs. silence to decide when to cut sentences.",
        ["model.vad.fire"] = "FireRedVAD (default)",
        ["model.vad.silero"] = "silero",
        ["model.tier"] = "Latency tier",
        ["model.tier.help"] = "Smaller = faster, larger = more context. Only 960 ms ships built-in; others download on demand.",
        ["model.tier.160.name"] = "160 ms",
        ["model.tier.160.scene"] = "Real-time interaction / live captions",
        ["model.tier.480.name"] = "480 ms",
        ["model.tier.480.scene"] = "Low latency + more context",
        ["model.tier.960.name"] = "960 ms",
        ["model.tier.960.scene"] = "Steadier, tolerates a little latency",
        ["model.tier.1920.name"] = "1920 ms",
        ["model.tier.1920.scene"] = "Most context, close to offline",
        ["model.aslang"] = "Recognition language",
        ["model.aslang.zhen"] = "Chinese + English (zh-en)",
        ["grp.models"] = "MODEL MANAGEMENT",
        ["model.bundled"] = "Built-in",
        ["model.downloaded"] = "Downloaded",
        ["model.notDownloaded"] = "Not downloaded",
        ["model.downloading"] = "Downloading {0}%",
        ["model.active"] = "In use",
        ["model.use"] = "Use",
        ["model.tierRow"] = "Streaming model · {0}",
        ["model.vadRow"] = "VAD model",
        ["model.dl.failed"] = "Download failed — click to retry",

        ["grp.permissions"] = "PERMISSIONS",
        ["perm.mic"] = "Microphone",
        ["perm.mic.help"] = "Used to capture your voice locally; audio never leaves the device.",
        ["perm.input"] = "Background input",
        ["perm.input.help"] = "The global hotkey and text insertion work without extra permission for normal apps. To dictate into apps running as administrator, run Vibe XASR as administrator too.",
        ["perm.granted"] = "✓ Granted",
        ["perm.denied"] = "✕ Not granted",
        ["perm.openSettings"] = "Open Privacy Settings",
        ["perm.recheck"] = "Re-check",
        ["perm.checking"] = "Checking…",
        ["perm.banner.ok"] = "Microphone access granted — Vibe XASR is ready ✓",
        ["perm.banner.warn"] = "Microphone access is missing; dictation won't work. Open Privacy Settings, allow the microphone, then come back.",

        ["about.version"] = "Version {0} · Windows 10/11 · x64 / ARM64",
        ["about.checkUpdate"] = "Check for updates",
        ["about.credits"] = "Also built on",
        ["about.local"] = "100% local · offline · your data never leaves the device",
        ["about.xasr.title"] = "Powered by X-ASR",
        ["about.xasr.desc"] = "The Chinese-English streaming speech-recognition model at the heart of this app.",
        ["about.xasr.repo"] = "🤗 GilgameshWind/X-ASR-zh-en",

        ["hud.listening"] = "Listening…",
        ["hud.inserted"] = "Inserted",
        ["hud.releaseHint"] = "Release to drop · Esc to cancel",
        ["hud.copyAll"] = "Copy all",
        ["hud.stop"] = "Stop",
        ["hud.micFail"] = "Can't start microphone",

        ["history.title"] = "History",
        ["history.privacy"] = "Your data stays on this device — never uploaded.",
        ["history.empty"] = "No dictation history yet.",
        ["history.count"] = "{0} entries",
        ["history.copied"] = "Copied",
        ["history.edit"] = "Edit",
        ["history.save"] = "Save",
        ["history.export"] = "Export",
        ["history.export.panel"] = "Export history",
        ["history.stats.chars"] = "{0} chars dictated",
        ["history.stats.minutes"] = " · {0} min saved",
        ["history.stats.hours"] = " · {0} h saved",
        ["history.stats.big"] = ">10000 chars dictated · >100 hours saved",
        ["history.clear.confirm.title"] = "Clear all records?",
        ["history.clear.confirm.body"] = "Permanently deletes all history and resets the cumulative stats. This can't be undone.",

        ["dl.title"] = "Downloading model",
        ["dl.tier"] = "Streaming model · {0} ms",
        ["dl.vad"] = "VAD model",
    };

    // ---- 中文 ----
    private static readonly Dictionary<string, string> Zh = new()
    {
        ["lang.auto"] = "自动",
        ["app.name"] = "Vibe XASR",
        ["ok"] = "好", ["cancel"] = "取消", ["done"] = "完成", ["close"] = "关闭",
        ["copy"] = "复制", ["copy.all"] = "复制全文", ["clear"] = "清空", ["clear.all"] = "全部清空",
        ["delete"] = "删除", ["download"] = "下载", ["switching"] = "切换中…",

        ["menu.loading"] = "正在加载模型…",
        ["menu.ready"] = "就绪 · 按住右 Ctrl 说话",
        ["menu.listening"] = "聆听中…",
        ["menu.recent"] = "最近一条",
        ["menu.enable"] = "启用听写",
        ["menu.settings"] = "设置…",
        ["menu.pad"] = "便笺…",
        ["menu.history"] = "历史…",
        ["menu.quit"] = "退出 Vibe XASR",

        ["settings.title"] = "偏好设置",
        ["settings.window.title"] = "Vibe XASR 设置",
        ["tab.general"] = "通用", ["tab.dictation"] = "听写", ["tab.model"] = "模型",
        ["tab.records"] = "记录",
        ["tab.permissions"] = "权限", ["tab.about"] = "关于",
        ["records.empty"] = "暂无记录",

        ["grp.general"] = "通用",
        ["gen.launchAtLogin"] = "开机自启动",
        ["gen.launchAtLogin.help"] = "登录时在后台静默启动 Vibe XASR。",
        ["gen.tray"] = "显示托盘图标",
        ["gen.tray.help"] = "在通知区域保留图标,便于随时打开菜单。",
        ["gen.lang"] = "界面语言",
        ["gen.lang.help"] = "自动会跟随系统语言(中 / EN / 日 / 한)。",

        ["grp.dictation"] = "听写",
        ["dict.hotkey"] = "触发键(push-to-talk)",
        ["dict.hotkey.help"] = "点一下开始录制,然后按下你想用的键。",
        ["dict.hotkey.recording"] = "请按一个键…",
        ["dict.mode"] = "听写模式",
        ["dict.mode.paste.title"] = "说完插入",
        ["dict.mode.paste.desc"] = "说完一句,一次性写入光标处。",
        ["dict.mode.type.title"] = "逐字插入",
        ["dict.mode.type.desc"] = "边说边逐字写入输入框。",
        ["dict.mode.oncall.title"] = "持续候机 · OnCall",
        ["dict.mode.oncall.desc"] = "持续后台运行,识别到说话即开始写入;识别内容持续显示在悬浮窗,永远为你 OnCall。开启后会一直监听麦克风。",
        ["dict.clipOverwrite"] = "每次说话覆盖剪贴板",
        ["dict.clipOverwrite.help"] = "把听写文本留在剪贴板,方便粘贴到任意位置。",
        ["dict.history"] = "本地保存历史",
        ["dict.history.help"] = "把每条听写结果保存在本设备(见「历史」窗口)。关闭后,记录仅临时保留 60 秒(点记录看倒计时)再销毁——避免说完就凭空消失。",

        ["grp.xasr"] = "X-ASR 流式模型",
        ["grp.vad"] = "VAD(进阶)",
        ["model.headline.title"] = "X-ASR · 中英文流式语音识别",
        ["model.headline.repo"] = "GilgameshWind/X-ASR-zh-en",
        ["model.source"] = "模型来源",
        ["model.source.value"] = "huggingface.co/GilgameshWind/X-ASR-zh-en",
        ["model.switching.banner"] = "正在切换模型…引擎重建中",
        ["model.dl.starting"] = "正在开始下载…",
        ["model.vad"] = "语音活动检测 (VAD)",
        ["model.vad.help"] = "检测「在说话 / 静音」,决定何时断句。",
        ["model.vad.fire"] = "FireRedVAD(默认)",
        ["model.vad.silero"] = "silero",
        ["model.tier"] = "延迟档",
        ["model.tier.help"] = "越小越快、越大上下文越多。仅 960ms 已内置,其余按需下载。",
        ["model.tier.160.name"] = "160ms",
        ["model.tier.160.scene"] = "实时交互 / 直播字幕",
        ["model.tier.480.name"] = "480ms",
        ["model.tier.480.scene"] = "低延迟 + 更多上下文",
        ["model.tier.960.name"] = "960ms",
        ["model.tier.960.scene"] = "更稳,容忍少量延迟",
        ["model.tier.1920.name"] = "1920ms",
        ["model.tier.1920.scene"] = "上下文最多,接近离线",
        ["model.aslang"] = "识别语言",
        ["model.aslang.zhen"] = "中英 (zh-en)",
        ["grp.models"] = "模型管理",
        ["model.bundled"] = "已内置",
        ["model.downloaded"] = "已下载",
        ["model.notDownloaded"] = "未下载",
        ["model.downloading"] = "下载中 {0}%",
        ["model.active"] = "使用中",
        ["model.use"] = "启用",
        ["model.tierRow"] = "流式模型 · {0}",
        ["model.vadRow"] = "VAD 模型",
        ["model.dl.failed"] = "下载失败 — 点击重试",

        ["grp.permissions"] = "权限",
        ["perm.mic"] = "麦克风",
        ["perm.mic.help"] = "用于本地采集你的语音,音频不离开设备。",
        ["perm.input"] = "后台输入",
        ["perm.input.help"] = "全局热键与文本插入对普通应用无需额外授权即可工作。若要向以管理员身份运行的应用听写,请同样以管理员身份运行 Vibe XASR。",
        ["perm.granted"] = "✓ 已授权",
        ["perm.denied"] = "✕ 未授权",
        ["perm.openSettings"] = "打开隐私设置",
        ["perm.recheck"] = "重新检测",
        ["perm.checking"] = "检测中…",
        ["perm.banner.ok"] = "麦克风已授权,Vibe XASR 可以正常工作 ✓",
        ["perm.banner.warn"] = "麦克风未授权,听写无法工作。打开隐私设置允许麦克风后回到这里。",

        ["about.version"] = "版本 {0} · Windows 10/11 · x64 / ARM64",
        ["about.checkUpdate"] = "检查更新",
        ["about.credits"] = "同时基于",
        ["about.local"] = "100% 本地 · 不联网 · 数据不出设备",
        ["about.xasr.title"] = "由 X-ASR 驱动",
        ["about.xasr.desc"] = "本应用核心的中英文流式语音识别模型。",
        ["about.xasr.repo"] = "🤗 GilgameshWind/X-ASR-zh-en",

        ["hud.listening"] = "在听…",
        ["hud.inserted"] = "已插入",
        ["hud.releaseHint"] = "松开落字 · Esc 取消",
        ["hud.copyAll"] = "复制全文",
        ["hud.stop"] = "停止",
        ["hud.micFail"] = "无法启动麦克风",

        ["history.title"] = "历史",
        ["history.privacy"] = "您的数据永远保存在本地,绝不上云。",
        ["history.empty"] = "还没有听写历史。",
        ["history.count"] = "{0} 条",
        ["history.copied"] = "已复制",
        ["history.edit"] = "编辑",
        ["history.save"] = "保存",
        ["history.export"] = "导出",
        ["history.export.panel"] = "导出历史",
        ["history.stats.chars"] = "累计 {0} 字",
        ["history.stats.minutes"] = " · 节省 {0} 分钟",
        ["history.stats.hours"] = " · 节省 {0} 小时",
        ["history.stats.big"] = "累计 >10000 字 · 节省 >100 小时",
        ["history.clear.confirm.title"] = "确定清空全部记录?",
        ["history.clear.confirm.body"] = "将永久删除全部历史记录,并把累计字数/节省时间清零,无法恢复。",

        ["dl.title"] = "正在下载模型",
        ["dl.tier"] = "流式模型 · {0} ms",
        ["dl.vad"] = "VAD 模型",
    };

    // ---- 日本語 ----
    private static readonly Dictionary<string, string> Ja = new()
    {
        ["lang.auto"] = "自動",
        ["app.name"] = "Vibe XASR",
        ["ok"] = "OK", ["cancel"] = "キャンセル", ["done"] = "完了", ["close"] = "閉じる",
        ["copy"] = "コピー", ["copy.all"] = "全文をコピー", ["clear"] = "消去", ["clear.all"] = "すべて消去",
        ["delete"] = "削除", ["download"] = "ダウンロード", ["switching"] = "切り替え中…",

        ["menu.loading"] = "モデルを読み込み中…",
        ["menu.ready"] = "準備完了 · 右 Ctrl 長押しで話す",
        ["menu.listening"] = "聞いています…",
        ["menu.recent"] = "最近の項目",
        ["menu.enable"] = "音声入力を有効化",
        ["menu.settings"] = "設定…",
        ["menu.pad"] = "メモ…",
        ["menu.history"] = "履歴…",
        ["menu.quit"] = "Vibe XASR を終了",

        ["settings.title"] = "環境設定",
        ["settings.window.title"] = "Vibe XASR 設定",
        ["tab.general"] = "一般", ["tab.dictation"] = "音声入力", ["tab.model"] = "モデル",
        ["tab.records"] = "記録",
        ["tab.permissions"] = "権限", ["tab.about"] = "情報",
        ["records.empty"] = "記録はまだありません",

        ["grp.general"] = "一般",
        ["gen.launchAtLogin"] = "ログイン時に起動",
        ["gen.launchAtLogin.help"] = "サインイン時に Vibe XASR をバックグラウンドで静かに起動します。",
        ["gen.tray"] = "トレイアイコンを表示",
        ["gen.tray.help"] = "通知領域にアイコンを残し、メニューへすぐアクセスできます。",
        ["gen.lang"] = "表示言語",
        ["gen.lang.help"] = "自動はシステム言語に従います(中 / EN / 日 / 한)。",

        ["grp.dictation"] = "音声入力",
        ["dict.hotkey"] = "トリガーキー(プッシュ・トゥ・トーク)",
        ["dict.hotkey.help"] = "クリックして録音し、使いたいキーを押してください。",
        ["dict.hotkey.recording"] = "キーを押してください…",
        ["dict.mode"] = "音声入力モード",
        ["dict.mode.paste.title"] = "話し終えたら挿入",
        ["dict.mode.paste.desc"] = "一文を話し終えると、カーソル位置にまとめて書き込みます。",
        ["dict.mode.type.title"] = "一文字ずつ挿入",
        ["dict.mode.type.desc"] = "話しながら一文字ずつ入力欄へ書き込みます。",
        ["dict.mode.oncall.title"] = "常時待機 · OnCall",
        ["dict.mode.oncall.desc"] = "バックグラウンドで常時動作し、発話を検知するとすぐに書き込みを開始します。認識中のテキストはオーバーレイに表示され続け、いつでもあなたを OnCall でサポート。オンの間はマイクを常に監視します。",
        ["dict.clipOverwrite"] = "音声入力のたびにクリップボードを上書き",
        ["dict.clipOverwrite.help"] = "音声入力したテキストをクリップボードに残し、どこにでも貼り付けられます。",
        ["dict.history"] = "履歴をローカルに保存",
        ["dict.history.help"] = "すべての音声入力結果をこの端末に保存します(「履歴」ウィンドウ参照)。オフのときは記録を60秒だけ保持(カウントダウン表示)して削除します——直後に消えないように。",

        ["grp.xasr"] = "X-ASR ストリーミングモデル",
        ["grp.vad"] = "VAD(詳細)",
        ["model.headline.title"] = "X-ASR · 中国語・英語ストリーミング音声認識",
        ["model.headline.repo"] = "GilgameshWind/X-ASR-zh-en",
        ["model.source"] = "モデルの入手元",
        ["model.source.value"] = "huggingface.co/GilgameshWind/X-ASR-zh-en",
        ["model.switching.banner"] = "モデルを切り替え中…エンジンを再構築しています",
        ["model.dl.starting"] = "ダウンロードを開始中…",
        ["model.vad"] = "音声区間検出 (VAD)",
        ["model.vad.help"] = "発話と無音を検出し、文の区切りを決めます。",
        ["model.vad.fire"] = "FireRedVAD(既定)",
        ["model.vad.silero"] = "silero",
        ["model.tier"] = "レイテンシ段階",
        ["model.tier.help"] = "小さいほど速く、大きいほど文脈が豊富。960ms のみ内蔵、他は必要時にダウンロード。",
        ["model.tier.160.name"] = "160ms",
        ["model.tier.160.scene"] = "リアルタイム / ライブ字幕",
        ["model.tier.480.name"] = "480ms",
        ["model.tier.480.scene"] = "低遅延 + より多くの文脈",
        ["model.tier.960.name"] = "960ms",
        ["model.tier.960.scene"] = "安定、わずかな遅延を許容",
        ["model.tier.1920.name"] = "1920ms",
        ["model.tier.1920.scene"] = "文脈最大、オフラインに近い",
        ["model.aslang"] = "認識言語",
        ["model.aslang.zhen"] = "中国語 + 英語 (zh-en)",
        ["grp.models"] = "モデル管理",
        ["model.bundled"] = "内蔵",
        ["model.downloaded"] = "ダウンロード済み",
        ["model.notDownloaded"] = "未ダウンロード",
        ["model.downloading"] = "ダウンロード中 {0}%",
        ["model.active"] = "使用中",
        ["model.use"] = "使用",
        ["model.tierRow"] = "ストリーミングモデル · {0}",
        ["model.vadRow"] = "VAD モデル",
        ["model.dl.failed"] = "ダウンロード失敗 — クリックで再試行",

        ["grp.permissions"] = "権限",
        ["perm.mic"] = "マイク",
        ["perm.mic.help"] = "音声をローカルで取得します。音声は端末から出ません。",
        ["perm.input"] = "バックグラウンド入力",
        ["perm.input.help"] = "グローバルホットキーとテキスト挿入は、通常のアプリでは追加の権限なしで動作します。管理者として実行中のアプリへ入力するには、Vibe XASR も管理者として実行してください。",
        ["perm.granted"] = "✓ 許可済み",
        ["perm.denied"] = "✕ 未許可",
        ["perm.openSettings"] = "プライバシー設定を開く",
        ["perm.recheck"] = "再確認",
        ["perm.checking"] = "確認中…",
        ["perm.banner.ok"] = "マイクが許可されています。Vibe XASR は正常に動作します ✓",
        ["perm.banner.warn"] = "マイクが未許可です。音声入力は動作しません。プライバシー設定でマイクを許可してから戻ってください。",

        ["about.version"] = "バージョン {0} · Windows 10/11 · x64 / ARM64",
        ["about.checkUpdate"] = "アップデートを確認",
        ["about.credits"] = "さらに、次の技術を利用",
        ["about.local"] = "100% ローカル · オフライン · データは端末外に出ません",
        ["about.xasr.title"] = "X-ASR を搭載",
        ["about.xasr.desc"] = "本アプリの中核となる中国語・英語ストリーミング音声認識モデル。",
        ["about.xasr.repo"] = "🤗 GilgameshWind/X-ASR-zh-en",

        ["hud.listening"] = "聞いています…",
        ["hud.inserted"] = "挿入済み",
        ["hud.releaseHint"] = "離すと入力 · Esc で取消",
        ["hud.copyAll"] = "全文をコピー",
        ["hud.stop"] = "停止",
        ["hud.micFail"] = "マイクを起動できません",

        ["history.title"] = "履歴",
        ["history.privacy"] = "あなたのデータは常にこの端末に保存され、クラウドには送信されません。",
        ["history.empty"] = "音声入力の履歴はまだありません。",
        ["history.count"] = "{0} 件",
        ["history.copied"] = "コピーしました",
        ["history.edit"] = "編集",
        ["history.save"] = "保存",
        ["history.export"] = "書き出し",
        ["history.export.panel"] = "履歴を書き出し",
        ["history.stats.chars"] = "累計 {0} 文字",
        ["history.stats.minutes"] = " · {0} 分節約",
        ["history.stats.hours"] = " · {0} 時間節約",
        ["history.stats.big"] = "累計 >10000 文字 · >100 時間節約",
        ["history.clear.confirm.title"] = "すべての記録を消去しますか?",
        ["history.clear.confirm.body"] = "すべての履歴を完全に削除し、累計の文字数/節約時間をリセットします。取り消せません。",

        ["dl.title"] = "モデルをダウンロード中",
        ["dl.tier"] = "ストリーミングモデル · {0} ms",
        ["dl.vad"] = "VAD モデル",
    };

    // ---- 한국어 ----
    private static readonly Dictionary<string, string> Ko = new()
    {
        ["lang.auto"] = "자동",
        ["app.name"] = "Vibe XASR",
        ["ok"] = "확인", ["cancel"] = "취소", ["done"] = "완료", ["close"] = "닫기",
        ["copy"] = "복사", ["copy.all"] = "전체 복사", ["clear"] = "지우기", ["clear.all"] = "모두 지우기",
        ["delete"] = "삭제", ["download"] = "다운로드", ["switching"] = "전환 중…",

        ["menu.loading"] = "모델 로드 중…",
        ["menu.ready"] = "준비됨 · 오른쪽 Ctrl 누른 채 말하기",
        ["menu.listening"] = "듣는 중…",
        ["menu.recent"] = "최근 항목",
        ["menu.enable"] = "받아쓰기 사용",
        ["menu.settings"] = "설정…",
        ["menu.pad"] = "메모…",
        ["menu.history"] = "기록…",
        ["menu.quit"] = "Vibe XASR 종료",

        ["settings.title"] = "환경설정",
        ["settings.window.title"] = "Vibe XASR 설정",
        ["tab.general"] = "일반", ["tab.dictation"] = "받아쓰기", ["tab.model"] = "모델",
        ["tab.records"] = "기록",
        ["tab.permissions"] = "권한", ["tab.about"] = "정보",
        ["records.empty"] = "아직 기록이 없습니다",

        ["grp.general"] = "일반",
        ["gen.launchAtLogin"] = "로그인 시 실행",
        ["gen.launchAtLogin.help"] = "로그인할 때 Vibe XASR를 백그라운드에서 조용히 시작합니다.",
        ["gen.tray"] = "트레이 아이콘 표시",
        ["gen.tray.help"] = "알림 영역에 아이콘을 유지해 메뉴에 빠르게 접근합니다.",
        ["gen.lang"] = "인터페이스 언어",
        ["gen.lang.help"] = "자동은 시스템 언어를 따릅니다(中 / EN / 日 / 한).",

        ["grp.dictation"] = "받아쓰기",
        ["dict.hotkey"] = "트리거 키(푸시 투 토크)",
        ["dict.hotkey.help"] = "클릭하여 녹음한 후 사용할 키를 누르세요.",
        ["dict.hotkey.recording"] = "키를 누르세요…",
        ["dict.mode"] = "받아쓰기 모드",
        ["dict.mode.paste.title"] = "말을 마치면 삽입",
        ["dict.mode.paste.desc"] = "한 문장을 말하고 나면 커서 위치에 한 번에 입력합니다.",
        ["dict.mode.type.title"] = "한 글자씩 삽입",
        ["dict.mode.type.desc"] = "말하는 동안 입력란에 한 글자씩 입력합니다.",
        ["dict.mode.oncall.title"] = "상시 대기 · OnCall",
        ["dict.mode.oncall.desc"] = "백그라운드에서 계속 실행되며 말소리를 감지하는 즉시 입력을 시작합니다. 인식된 텍스트는 오버레이에 계속 표시되어 언제나 당신을 위해 OnCall 대기합니다. 켜져 있는 동안 마이크를 계속 모니터링합니다.",
        ["dict.clipOverwrite"] = "받아쓰기마다 클립보드 덮어쓰기",
        ["dict.clipOverwrite.help"] = "받아쓴 텍스트를 클립보드에 남겨 어디에나 붙여넣을 수 있습니다.",
        ["dict.history"] = "기록을 로컬에 저장",
        ["dict.history.help"] = "모든 받아쓰기 결과를 이 기기에 보관합니다(‘기록’ 창 참고). 끄면 기록을 60초 동안만 보관(카운트다운 표시) 후 삭제합니다 — 끝나자마자 사라지지 않도록.",

        ["grp.xasr"] = "X-ASR 스트리밍 모델",
        ["grp.vad"] = "VAD(고급)",
        ["model.headline.title"] = "X-ASR · 중국어·영어 스트리밍 음성 인식",
        ["model.headline.repo"] = "GilgameshWind/X-ASR-zh-en",
        ["model.source"] = "모델 출처",
        ["model.source.value"] = "huggingface.co/GilgameshWind/X-ASR-zh-en",
        ["model.switching.banner"] = "모델 전환 중… 엔진을 다시 빌드하는 중입니다",
        ["model.dl.starting"] = "다운로드 시작 중…",
        ["model.vad"] = "음성 활동 감지 (VAD)",
        ["model.vad.help"] = "말하기와 침묵을 감지하여 문장 구분 시점을 결정합니다.",
        ["model.vad.fire"] = "FireRedVAD(기본)",
        ["model.vad.silero"] = "silero",
        ["model.tier"] = "지연 단계",
        ["model.tier.help"] = "작을수록 빠르고, 클수록 문맥이 풍부합니다. 960ms만 내장, 나머지는 필요 시 다운로드.",
        ["model.tier.160.name"] = "160ms",
        ["model.tier.160.scene"] = "실시간 상호작용 / 라이브 자막",
        ["model.tier.480.name"] = "480ms",
        ["model.tier.480.scene"] = "낮은 지연 + 더 많은 문맥",
        ["model.tier.960.name"] = "960ms",
        ["model.tier.960.scene"] = "더 안정적, 약간의 지연 허용",
        ["model.tier.1920.name"] = "1920ms",
        ["model.tier.1920.scene"] = "문맥 최대, 오프라인에 가까움",
        ["model.aslang"] = "인식 언어",
        ["model.aslang.zhen"] = "중국어 + 영어 (zh-en)",
        ["grp.models"] = "모델 관리",
        ["model.bundled"] = "내장",
        ["model.downloaded"] = "다운로드됨",
        ["model.notDownloaded"] = "다운로드 안 됨",
        ["model.downloading"] = "다운로드 중 {0}%",
        ["model.active"] = "사용 중",
        ["model.use"] = "사용",
        ["model.tierRow"] = "스트리밍 모델 · {0}",
        ["model.vadRow"] = "VAD 모델",
        ["model.dl.failed"] = "다운로드 실패 — 눌러서 재시도",

        ["grp.permissions"] = "권한",
        ["perm.mic"] = "마이크",
        ["perm.mic.help"] = "음성을 로컬에서 수집합니다. 오디오는 기기를 떠나지 않습니다.",
        ["perm.input"] = "백그라운드 입력",
        ["perm.input.help"] = "전역 단축키와 텍스트 삽입은 일반 앱에서는 추가 권한 없이 작동합니다. 관리자 권한으로 실행 중인 앱에 입력하려면 Vibe XASR도 관리자 권한으로 실행하세요.",
        ["perm.granted"] = "✓ 허용됨",
        ["perm.denied"] = "✕ 허용 안 됨",
        ["perm.openSettings"] = "개인정보 설정 열기",
        ["perm.recheck"] = "다시 확인",
        ["perm.checking"] = "확인 중…",
        ["perm.banner.ok"] = "마이크가 허용되었습니다 — Vibe XASR를 사용할 수 있습니다 ✓",
        ["perm.banner.warn"] = "마이크 권한이 없어 받아쓰기가 작동하지 않습니다. 개인정보 설정에서 마이크를 허용한 후 돌아오세요.",

        ["about.version"] = "버전 {0} · Windows 10/11 · x64 / ARM64",
        ["about.checkUpdate"] = "업데이트 확인",
        ["about.credits"] = "또한 다음 기술 기반",
        ["about.local"] = "100% 로컬 · 오프라인 · 데이터가 기기를 떠나지 않습니다",
        ["about.xasr.title"] = "X-ASR 기반",
        ["about.xasr.desc"] = "이 앱의 핵심인 중국어·영어 스트리밍 음성 인식 모델입니다.",
        ["about.xasr.repo"] = "🤗 GilgameshWind/X-ASR-zh-en",

        ["hud.listening"] = "듣는 중…",
        ["hud.inserted"] = "삽입됨",
        ["hud.releaseHint"] = "놓으면 입력 · Esc로 취소",
        ["hud.copyAll"] = "전체 복사",
        ["hud.stop"] = "중지",
        ["hud.micFail"] = "마이크를 시작할 수 없습니다",

        ["history.title"] = "기록",
        ["history.privacy"] = "귀하의 데이터는 항상 이 기기에 저장되며 클라우드에 업로드되지 않습니다.",
        ["history.empty"] = "아직 받아쓰기 기록이 없습니다.",
        ["history.count"] = "{0}개",
        ["history.copied"] = "복사됨",
        ["history.edit"] = "편집",
        ["history.save"] = "저장",
        ["history.export"] = "내보내기",
        ["history.export.panel"] = "기록 내보내기",
        ["history.stats.chars"] = "누적 {0}자",
        ["history.stats.minutes"] = " · {0}분 절약",
        ["history.stats.hours"] = " · {0}시간 절약",
        ["history.stats.big"] = "누적 >10000자 · >100시간 절약",
        ["history.clear.confirm.title"] = "모든 기록을 지울까요?",
        ["history.clear.confirm.body"] = "모든 기록을 영구 삭제하고 누적 글자수/절약 시간을 초기화합니다. 되돌릴 수 없습니다.",

        ["dl.title"] = "모델 다운로드 중",
        ["dl.tier"] = "스트리밍 모델 · {0} ms",
        ["dl.vad"] = "VAD 모델",
    };
}
