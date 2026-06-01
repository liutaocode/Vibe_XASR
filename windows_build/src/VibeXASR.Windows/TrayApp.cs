using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VibeXASR.Windows.Dictation;
using VibeXASR.Windows.Input;
using VibeXASR.Windows.Models;
using VibeXASR.Windows.Storage;
using VibeXASR.Windows.Ui;

namespace VibeXASR.Windows;

/// <summary>
/// Owns the whole runtime — tray icon + menu/popup, the dictation engine, the mic, the
/// global hotkey, the overlay, and the Settings/History windows. The Windows analogue of
/// the macOS AppDelegate / status-item controller. Implements <see cref="IAppController"/>
/// so every window writes its changes back here and they apply live.
/// </summary>
public sealed class TrayApp : IDisposable, IAppController
{
    public ApplicationContext Context { get; } = new();

    private readonly Settings _settings;
    private readonly HistoryStore _history = new();
    private readonly ModelManager _models;

    private NotifyIcon? _tray;
    private GlobalHotkey? _hotkey;
    private MicCapture? _mic;
    private DictationEngine? _engine;
    private OverlayForm? _overlay;
    private TrayPopupForm? _popup;
    private SettingsForm? _settingsForm;
    private HistoryForm? _historyForm;
    private OnCallSessionForm? _onCallSessionForm;
    private DownloadForm? _dl;

    // Ephemeral transcript of the CURRENT OnCall session (cleared when a session starts) —
    // distinct from the persistent _history store; this is what the overlay "View" button shows
    // (macOS OnCallLog parity). Mutated on the engine worker thread, read on the UI thread.
    private readonly List<HistoryEntry> _onCallSession = new();

    private SynchronizationContext _ui = null!;
    private string _typedSoFar = string.Empty;
    private volatile bool _engineReady;
    private volatile bool _engineSwapping;
    private bool _dictationEnabled = true;
    private volatile bool _listening;
    private volatile float _holdPeakRms;   // loudest mic level seen during the current hold (diagnostics)
    private bool _announcedReady;          // show the "ready" tray prompt once per launch

    public TrayApp()
    {
        _settings = Settings.Load();
        _models = new ModelManager(_settings);
        L10n.Current = L10n.FromCode(_settings.Language);
        Theme.IsDark = true; // dark-first like macOS; respect system below
        Theme.IsDark = DetectDark();
    }

    public void Start()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _overlay = new OverlayForm();
        _overlay.CopyRequested += (_, _) => CopyOverlayText();
        _overlay.StopRequested += (_, _) => SetMode(DictationMode.Paste); // leave OnCall
        _overlay.ViewRequested += (_, _) => OpenOnCallSession();
        _overlay.PauseRequested += (_, _) => TogglePause();
        // Realize the overlay handle so cross-thread BeginInvoke works immediately.
        _ = _overlay.Handle;

        _popup = new TrayPopupForm(this);

        BuildTray();

        _hotkey = new GlobalHotkey(_settings.HotkeyVk);
        _hotkey.KeyDown += (_, _) => OnHotkeyDown();
        _hotkey.KeyUp += (_, _) => OnHotkeyUp();
        _hotkey.Install();

        // Optional launch hook: VIBEXASR_OPEN=settings|history|popup opens that window at
        // startup (used for verification, and the seam for a future single-instance "show
        // settings"). In that mode we skip the engine bootstrap so no download dialog overlaps.
        var openRaw = Environment.GetEnvironmentVariable("VIBEXASR_OPEN")?.ToLowerInvariant();
        var open = openRaw?.Split(':')[0];
        var openArg = openRaw is not null && openRaw.Contains(':') ? openRaw.Split(':', 2)[1] : null;
        // Normal launch starts the engine; "popup"/"rebind" also do (they need real engine state).
        // settings/history/overlay hooks skip it for clean screenshots.
        if (string.IsNullOrEmpty(open) || open is "popup" or "rebind")
            _ = EnsureEngineAsync(swapping: false);
        // Auto-update (WinSparkle): start automatic daily checks on a normal launch only —
        // never during the screenshot/test hooks (settings/history/overlay/selftest…).
        if (string.IsNullOrEmpty(open))
            Updater.Initialize(_ui, Quit);
        switch (open)
        {
            case "settings": OpenSettings(openArg); break;
            case "history": OpenHistory(); break;
            case "popup": _popup?.ShowNear(); break;
            case "rebind": SetHotkey(int.TryParse(openArg, out var vk) ? vk : 0x78); break; // live-rebind self-test
            case "selftest": _ = SelfTestAsync(openArg); break; // feed a WAV through the engine
            case "mictest": _ = MicTestAsync(); break; // capture real mic → save WAV → run ASR
            case "checkupdate": Updater.Initialize(_ui, Quit); Updater.CheckForUpdatesUi(); break; // WinSparkle UI
            case "oncallsession": // populate a fake current-session log + open the session transcript view
                lock (_onCallSession)
                {
                    _onCallSession.Add(new HistoryEntry { Text = "把这个 function 改成 async。", Mode = "oncall", Timestamp = DateTimeOffset.Now.AddSeconds(-42) });
                    _onCallSession.Add(new HistoryEntry { Text = "顺便帮我加一个错误处理,别让它直接崩。", Mode = "oncall", Timestamp = DateTimeOffset.Now.AddSeconds(-18) });
                    _onCallSession.Add(new HistoryEntry { Text = "Then write a unit test for the parser.", Mode = "oncall", Timestamp = DateTimeOffset.Now });
                }
                OpenOnCallSession();
                break;
            case "overlay":
                _listening = true;
                _overlay?.ShowListening();
                _overlay?.SetLevel(0.7);
                _overlay?.SetText(openArg == "oncall" ? "" : "把这个 function 改成 async");
                if (openArg == "oncall") _overlay?.ShowOnCall();
                break;
        }
    }

    // ---- tray icon + menus ----

    private void BuildTray()
    {
        _tray = new NotifyIcon
        {
            Icon = Branding.AppIcon,
            Visible = _settings.ShowTrayIcon,
            Text = "Vibe XASR",
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_popup is { Visible: true }) _popup.Hide();
                else _popup?.ShowNear();
            }
        };

        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Ui(9.5f) };
        menu.BackColor = Theme.Surface2;
        menu.ForeColor = Theme.Text;
        menu.Opening += (_, _) => RebuildTrayMenu(menu);
        _tray.ContextMenuStrip = menu;
        RebuildTrayMenu(menu);
    }

    private void RebuildTrayMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        var enable = new ToolStripMenuItem(L10n.T("menu.enable")) { Checked = _dictationEnabled, CheckOnClick = true };
        enable.Click += (_, _) => DictationEnabled = enable.Checked;
        menu.Items.Add(enable);

        menu.Items.Add(new ToolStripSeparator());

        var mode = new ToolStripMenuItem(L10n.T("dict.mode"));
        AddModeItem(mode, DictationMode.Paste, "dict.mode.paste.title");
        AddModeItem(mode, DictationMode.Type, "dict.mode.type.title");
        AddModeItem(mode, DictationMode.OnCall, "dict.mode.oncall.title");
        menu.Items.Add(mode);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L10n.T("menu.history"), null, (_, _) => OpenHistory());
        menu.Items.Add(L10n.T("menu.settings"), null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L10n.T("menu.quit"), null, (_, _) => Quit());
    }

    private void AddModeItem(ToolStripMenuItem parent, DictationMode m, string key)
    {
        var item = new ToolStripMenuItem(L10n.T(key)) { Checked = _settings.Mode == m, ForeColor = Theme.Text };
        item.Click += (_, _) => SetMode(m);
        parent.DropDownItems.Add(item);
    }

    // ---- model bootstrap / engine swap ----

    private async Task EnsureEngineAsync(bool swapping)
    {
        if (swapping) _engineSwapping = true;
        StopEngine();
        try
        {
            var paths = ModelPaths.ForTier(_settings.Tier);
            var vad = _settings.EffectiveVad;
            Diag.Log($"EnsureEngine tier={(int)_settings.Tier} vad={vad} " +
                     $"asr={paths.AsrModelPresent()} vadPresent={paths.VadPresent(vad)}");
            if (!paths.AsrModelPresent() || !paths.VadPresent(vad))
            {
                ShowDownloadDialog();
                var dl = new ModelDownloader();
                var prog = new Progress<DownloadProgress>(p =>
                    _dl?.Report(p.Fraction ?? 0,
                        $"{p.FileName}  ({p.FileIndex + 1}/{p.FileCount})"));
                await dl.EnsureTierAsync(paths, prog);
                await dl.EnsureVadAsync(paths.VadFileFor(vad), prog);
                CloseDownloadDialog();
            }
            // Build the engine (565 MB model) OFF the UI thread so the app + hotkey stay
            // responsive, then start the mic ON the UI thread (WASAPI is more stable owned by a
            // pumping thread, and lets us hot-swap the device without a full reload).
            await Task.Run(BuildEngineCore);
            RunOnUi(() =>
            {
                StartMic();
                _engineReady = true;
                Diag.Log($"engine: READY (mic running={_mic?.IsRunning == true})");
                if (_settings.Mode == DictationMode.OnCall) EnterOnCall();
                _popup?.Invalidate();
                AnnounceReady();
            });
        }
        catch (Exception ex)
        {
            CloseDownloadDialog();
            Diag.Log("ENGINE FAILED: " + ex);
            _tray?.ShowBalloonTip(5000, "Vibe XASR",
                "Model/engine failed: " + ex.Message, ToolTipIcon.Error);
        }
        finally { _engineSwapping = false; }
    }

    /// <summary>Build the ASR/VAD engine + worker (heavy model load). Background thread. No mic.</summary>
    private void BuildEngineCore()
    {
        Diag.Log("engine: loading model…");
        _engine = new DictationEngine(_settings) { Mode = _settings.Mode };
        _engine.OnPartial += OnPartial;
        _engine.OnFinal += OnFinal;
        _engine.Start();
        Diag.Log("engine: model loaded");
    }

    /// <summary>(Re)start the microphone on the chosen device. Must run on the UI thread.</summary>
    private void StartMic()
    {
        if (_mic is not null) { _mic.FrameAvailable -= OnMicFrame; try { _mic.Dispose(); } catch { } _mic = null; }
        try
        {
            _mic = new MicCapture(_settings.MicDeviceId);
            _mic.FrameAvailable += OnMicFrame;
            _mic.Start();
        }
        catch (Exception ex) { Diag.Log("mic start failed: " + ex.Message); }
    }

    private void StopEngine()
    {
        _engineReady = false;
        if (_mic is not null) { _mic.FrameAvailable -= OnMicFrame; _mic.Dispose(); _mic = null; }
        if (_engine is not null)
        {
            _engine.OnPartial -= OnPartial;
            _engine.OnFinal -= OnFinal;
            _engine.Dispose();
            _engine = null;
        }
    }

    /// <summary>
    /// Diagnostic self-test: feed a speech WAV through the real engine as if it were a
    /// push-to-talk hold, and log the partials/final. Confirms the ASR pipeline produces
    /// text on Windows independent of the microphone. Triggered by VIBEXASR_OPEN=selftest:&lt;wav&gt;.
    /// </summary>
    private async Task SelfTestAsync(string? wavPath)
    {
        try
        {
            var paths = ModelPaths.ForTier(_settings.Tier);
            Diag.Log($"selftest: asrPresent={paths.AsrModelPresent()} wav={wavPath} exists={File.Exists(wavPath ?? "")}");
            if (!paths.AsrModelPresent() || string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath)) return;

            // Read + downmix + resample to 16 kHz mono float via NAudio.
            var samples = new List<float>();
            using (var rdr = new AudioFileReader(wavPath))
            {
                ISampleProvider sp = rdr;
                if (sp.WaveFormat.Channels > 1) sp = sp.ToMono();
                if (sp.WaveFormat.SampleRate != 16000) sp = new WdlResamplingSampleProvider(sp, 16000);
                var buf = new float[16000];
                int n;
                while ((n = sp.Read(buf, 0, buf.Length)) > 0)
                    for (int i = 0; i < n; i++) samples.Add(buf[i]);
            }
            float peak = 0; foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));
            Diag.Log($"selftest: {samples.Count} samples ({samples.Count / 16000.0:F1}s) peak={peak:F3}");

            // Drive a fresh DictationEngine (its own model, NO mic) through the real PTT path:
            // BeginHold → push frames → EndHold. This exercises the exact code the hotkey uses
            // (queue + PTT branch + InputFinished + OnFinal), with one clean audio source.
            await Task.Run(() =>
            {
                using var eng = new DictationEngine(_settings) { Mode = DictationMode.Paste };
                eng.OnPartial += (_, ev) => { };
                eng.OnFinal += (_, ev) => Diag.Log($"selftest ENGINE OnFinal: len={ev.Text?.Length ?? 0} \"{ev.Text}\"");
                eng.Start();
                eng.BeginHold();
                for (int i = 0; i < samples.Count; i += 512)
                {
                    int n = Math.Min(512, samples.Count - i);
                    var f = new float[n];
                    samples.CopyTo(i, f, 0, n);
                    eng.PushFrame(f);
                    Thread.Sleep(8);
                }
                Thread.Sleep(400);
                eng.EndHold();
                Thread.Sleep(1500); // let the worker finalize
                Diag.Log("selftest: engine path done");
            });
        }
        catch (Exception ex) { Diag.Log("selftest FAILED: " + ex); }
    }

    /// <summary>Once per launch, tell the user the app is live + how to use it (tray prompt),
    /// since otherwise it just sits silently in the notification area.</summary>
    private void AnnounceReady()
    {
        if (_announcedReady || _tray is null) return;
        _announcedReady = true;

        // First launch ever: a clear welcome/onboarding window (a toast is too easy to miss).
        if (!_settings.Welcomed)
        {
            try { new WelcomeForm(this).Show(); return; }
            catch (Exception ex) { Diag.Log("welcome failed: " + ex); }
        }

        // Subsequent launches: a lightweight tray prompt with the hotkey hint.
        var key = VkNames.Name(_settings.HotkeyVk);
        bool zh = L10n.Resolved == Lang.Zh;
        string title = zh ? "Vibe XASR 已就绪" : "Vibe XASR is ready";
        string msg = _settings.Mode == DictationMode.OnCall
            ? (zh ? "持续候机已开启 · 识别结果显示在右上角悬浮窗" : "OnCall is on · live text shows top-right")
            : (zh ? $"按住 {key} 说话,松开即把文字落到光标处。" : $"Hold {key} and speak; release to drop the text.");
        try { _tray.ShowBalloonTip(6000, title, msg, ToolTipIcon.Info); } catch { }
    }

    /// <summary>
    /// Mic diagnostic: capture ~6 s from the real microphone, log device/format/level, save it
    /// to %APPDATA%\VibeXASR\mictest.wav, and run the ASR on it — so we can see whether the
    /// user's mic audio actually reaches + recognizes. Triggered by VIBEXASR_OPEN=mictest.
    /// </summary>
    // Runs entirely on a background thread (synchronous) so it can't stall on the startup
    // sync-context. Captures the real mic for 6 s, logs level, saves a WAV, and runs the ASR.
    private Task MicTestAsync() => Task.Run(() =>
    {
        try
        {
            var samples = new List<float>();
            int frames = 0; float peak = 0;
            var mic = new MicCapture();
            mic.FrameAvailable += (_, f) =>
            {
                frames++;
                lock (samples) samples.AddRange(f);
                double s = 0; foreach (var v in f) s += v * v;
                float rms = f.Length > 0 ? (float)Math.Sqrt(s / f.Length) : 0;
                if (rms > peak) peak = rms;
            };
            mic.Start();
            RunOnUi(() => _tray?.ShowBalloonTip(6500, "Vibe XASR",
                L10n.Resolved == Lang.Zh ? "麦克风测试:请现在说话 6 秒…" : "Mic test: please speak for 6 seconds…",
                ToolTipIcon.Info));
            Diag.Log("mictest: recording 6s — SPEAK NOW");
            Thread.Sleep(6000);

            float[] arr; lock (samples) arr = samples.ToArray();
            Diag.Log($"mictest: frames={frames} samples={arr.Length} ({arr.Length / 16000.0:F1}s) peakRMS={peak:F4}");
            try { mic.Stop(); mic.Dispose(); } catch (Exception ex) { Diag.Log("mic stop/dispose: " + ex.Message); }

            var wav = System.IO.Path.Combine(AppPaths.DataDir, "mictest.wav");
            using (var w = new WaveFileWriter(wav, WaveFormat.CreateIeeeFloatWaveFormat(16000, 1)))
                w.WriteSamples(arr, 0, arr.Length);
            Diag.Log("mictest: saved " + wav);

            using var asr = new StreamingAsr(ModelPaths.ForTier(_settings.Tier), 16000);
            for (int i = 0; i < arr.Length; i += 1600)
            {
                int n = Math.Min(1600, arr.Length - i);
                var f = new float[n]; Array.Copy(arr, i, f, 0, n);
                asr.AcceptWaveform(f);
            }
            var text = asr.Finalize();
            Diag.Log($"mictest ASR result: \"{text}\"");
            RunOnUi(() => _tray?.ShowBalloonTip(8000, "Vibe XASR",
                string.IsNullOrEmpty(text)
                    ? (L10n.Resolved == Lang.Zh ? $"未识别到内容(峰值音量 {peak:F3})" : $"Nothing recognized (peak {peak:F3})")
                    : (L10n.Resolved == Lang.Zh ? "识别到:" : "Recognized: ") + text,
                ToolTipIcon.Info));
        }
        catch (Exception ex) { Diag.Log("mictest FAILED: " + ex); }
    });

    private void ShowDownloadDialog()
        => RunOnUi(() => { _dl ??= new DownloadForm(); if (!_dl.Visible) _dl.Show(); });

    private void CloseDownloadDialog()
        => RunOnUi(() => { _dl?.Hide(); });

    // ---- mic → engine + level meter ----

    private void OnMicFrame(object? sender, float[] frame)
    {
        _engine?.PushFrame(frame);
        // Cheap RMS envelope to drive the overlay waveform.
        double sum = 0;
        for (int i = 0; i < frame.Length; i++) sum += frame[i] * frame[i];
        double rms = frame.Length > 0 ? Math.Sqrt(sum / frame.Length) : 0;
        if (rms > _holdPeakRms) _holdPeakRms = (float)rms;
        _overlay?.SetLevel(Math.Min(1.0, rms * 6.0));
    }

    // ---- hotkey ----

    private void OnHotkeyDown()
    {
        Diag.Log($"OnHotkeyDown enabled={_dictationEnabled} mode={_settings.Mode} ready={_engineReady}");
        if (!_dictationEnabled)
        {
            _tray?.ShowBalloonTip(2500, "Vibe XASR",
                L10n.Resolved == Lang.Zh ? "听写已停用(在菜单里启用)" : "Dictation is disabled (enable it in the menu).",
                ToolTipIcon.Info);
            return;
        }
        if (_settings.Mode == DictationMode.OnCall) return; // OnCall is always-on; PTT n/a
        if (!_engineReady)
        {
            // Don't fail silently — tell the user the model is still loading.
            _tray?.ShowBalloonTip(2500, "Vibe XASR",
                L10n.Resolved == Lang.Zh ? "模型正在加载,请稍候…" : "Model is still loading, please wait…",
                ToolTipIcon.Info);
            return;
        }
        _typedSoFar = string.Empty;
        _holdPeakRms = 0;
        _listening = true;
        _engine?.BeginHold();
        _overlay?.ShowListening();
    }

    private void OnHotkeyUp()
    {
        if (_settings.Mode == DictationMode.OnCall) return;
        if (!_listening) return;
        _listening = false;
        Diag.Log($"OnHotkeyUp; peak mic RMS={_holdPeakRms:F4}");
        _engine?.EndHold();
    }

    // ---- engine events (raised on the engine worker thread) ----

    private void OnPartial(object? sender, PartialEventArgs e)
    {
        switch (_settings.Mode)
        {
            case DictationMode.Paste:
            case DictationMode.OnCall:
                _overlay?.SetText(e.Text);
                break;
            case DictationMode.Type:
                StreamTypeDiff(e.Text);
                _overlay?.SetText(e.Text);
                break;
        }
        if (_popup is { Visible: true }) RunOnUi(() => _popup?.Invalidate());
    }

    private void OnFinal(object? sender, FinalEventArgs e)
    {
        Diag.Log($"OnFinal mode={_settings.Mode} len={e.Text?.Length ?? 0} text=\"{Trunc(e.Text)}\"");

        // Empty final = end-of-hold with nothing recognized: just close the overlay (PTT),
        // don't insert/record. (Without this the overlay would stay up after release.)
        if (string.IsNullOrEmpty(e.Text))
        {
            if (_settings.Mode != DictationMode.OnCall) _overlay?.HideOverlay();
            return;
        }

        var modeTag = _settings.Mode.ToString().ToLowerInvariant();
        _history.Append(e.Text, modeTag, ephemeral: !_settings.HistoryEnabled);

        switch (_settings.Mode)
        {
            case DictationMode.Paste:
                TextInserter.InsertText(e.Text);
                MaybeOverwriteClipboard(e.Text);
                _overlay?.ShowInserted();
                break;
            case DictationMode.Type:
                StreamTypeDiff(e.Text);
                _typedSoFar = string.Empty;
                MaybeOverwriteClipboard(e.Text);
                _overlay?.ShowInserted();
                break;
            case DictationMode.OnCall:
                lock (_onCallSession)
                    _onCallSession.Add(new HistoryEntry { Text = e.Text.Trim(), Mode = "oncall", Timestamp = DateTimeOffset.Now });
                RefreshOnCallSession();
                _overlay?.SetText(e.Text);
                break;
        }
        RefreshOpenWindows();
    }

    private void MaybeOverwriteClipboard(string text)
    {
        if (!_settings.ClipboardOverwrite || string.IsNullOrEmpty(text)) return;
        RunOnUi(() => { try { Clipboard.SetText(text); } catch { } });
    }

    /// <summary>Type-mode incremental insertion: keep the common prefix, backspace the
    /// divergent tail, type the new suffix (mirrors the macOS streaming inserter).</summary>
    private void StreamTypeDiff(string newText)
    {
        int common = 0, max = Math.Min(_typedSoFar.Length, newText.Length);
        while (common < max && _typedSoFar[common] == newText[common]) common++;
        int toDelete = _typedSoFar.Length - common;
        if (toDelete > 0) TextInserter.Backspace(toDelete);
        var suffix = newText[common..];
        if (suffix.Length > 0) TextInserter.InsertText(suffix);
        _typedSoFar = newText;
    }

    // ---- IAppController ----

    public Settings Settings => _settings;
    public HistoryStore History => _history;
    public ModelManager Models => _models;
    public bool EngineSwapping => _engineSwapping;
    public bool EngineReady => _engineReady;
    public bool IsListening => _listening || _settings.Mode == DictationMode.OnCall;
    public string CurrentOverlayText => _overlay?.CurrentText ?? string.Empty;

    public bool DictationEnabled
    {
        get => _dictationEnabled;
        set
        {
            _dictationEnabled = value;
            if (!value && _settings.Mode == DictationMode.OnCall) SetMode(DictationMode.Paste);
            _popup?.Invalidate();
        }
    }

    public void SetMode(DictationMode mode)
    {
        if (_settings.Mode == mode) return;
        bool wasOnCall = _settings.Mode == DictationMode.OnCall;
        _settings.Mode = mode;
        _settings.Save();
        if (_engine is not null) _engine.Mode = mode;

        if (wasOnCall && mode != DictationMode.OnCall) _overlay?.LeaveOnCall();
        if (mode == DictationMode.OnCall) EnterOnCall();

        NotifyExternallyChanged();
    }

    private void EnterOnCall()
    {
        if (_engine is not null) _engine.Mode = DictationMode.OnCall;
        lock (_onCallSession) _onCallSession.Clear();   // fresh session log (macOS clears on start)
        RefreshOnCallSession();
        _overlay?.ShowOnCall();
        _overlay?.SetText(string.Empty);
    }

    private void TogglePause()
    {
        if (_engine is not null) _engine.Paused = !_engine.Paused;
    }

    public void SetVad(VadKind vad)
    {
        if (_settings.Vad == vad) return;
        _settings.Vad = vad; _settings.Save();
        _ = EnsureEngineAsync(swapping: true);
    }

    public void SelectTier(ModelTier tier)
    {
        if (_settings.Tier == tier && _engineReady) return;
        _settings.Tier = tier; _settings.Save();
        _ = EnsureEngineAsync(swapping: true);
    }

    public void SetHotkey(int vk)
    {
        _settings.HotkeyVk = vk; _settings.Save();
        _hotkey?.SetKey(vk);
    }

    public void SetLanguage(Lang lang)
    {
        L10n.Current = lang;
        _settings.Language = L10n.ToCode(lang);
        _settings.Save();
        _popup?.Invalidate();
    }

    public void SetClipboardOverwrite(bool on) { _settings.ClipboardOverwrite = on; _settings.Save(); }
    public void SetHistoryEnabled(bool on) { _settings.HistoryEnabled = on; _settings.Save(); }

    public void SetLaunchAtLogin(bool on)
    {
        _settings.LaunchAtLogin = on; _settings.Save();
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;
            if (on) key.SetValue("VibeXASR", $"\"{Application.ExecutablePath}\"");
            else key.DeleteValue("VibeXASR", throwOnMissingValue: false);
        }
        catch { /* registry locked — non-fatal */ }
    }

    public bool MicGranted()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone");
            // "Deny" => blocked globally. Missing/"Allow" => permitted.
            return key?.GetValue("Value") as string != "Deny";
        }
        catch { return true; }
    }

    public void OpenMicPrivacy()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true }); }
        catch { }
    }

    public System.Collections.Generic.List<(string Id, string Name)> MicDevices() => MicCapture.Devices();
    public string MicDeviceId => _settings.MicDeviceId;

    public void SetMicDevice(string id)
    {
        if (_settings.MicDeviceId == id) return;
        _settings.MicDeviceId = id;
        _settings.Save();
        Diag.Log($"SetMicDevice -> {id}");
        if (_engineReady) RunOnUi(StartMic); // hot-swap the mic only (no model reload)
    }

    public void OpenSettings() => OpenSettings(null);

    private void OpenSettings(string? tab)
    {
        RunOnUi(() =>
        {
            if (_settingsForm is { IsDisposed: false })
            {
                _settingsForm.Activate();
                if (tab is not null) _settingsForm.ShowTab(tab);
                return;
            }
            _settingsForm = new SettingsForm(this) { Icon = Branding.AppIcon };
            if (tab is not null) _settingsForm.ShowTab(tab);
            _settingsForm.Show();
            _settingsForm.Activate();
            _settingsForm.BringToFront();
        });
    }

    public void OpenHistory()
    {
        RunOnUi(() =>
        {
            if (_historyForm is { IsDisposed: false }) { _historyForm.Activate(); _historyForm.BringToFront(); return; }
            _historyForm = new HistoryForm(_history) { Icon = Branding.AppIcon };
            _historyForm.Show();
            _historyForm.Activate();
            _historyForm.BringToFront();
        });
    }

    /// <summary>Open the CURRENT OnCall session transcript (the overlay "View" button) — the
    /// ephemeral per-session records, NOT the global history (macOS OnCallSessionView parity).</summary>
    public void OpenOnCallSession()
    {
        RunOnUi(() =>
        {
            if (_onCallSessionForm is { IsDisposed: false })
            { _onCallSessionForm.Reload(); _onCallSessionForm.Activate(); _onCallSessionForm.BringToFront(); return; }
            _onCallSessionForm = new OnCallSessionForm(SnapshotOnCallSession) { Icon = Branding.AppIcon };
            _onCallSessionForm.Show();
            _onCallSessionForm.Activate();
            _onCallSessionForm.BringToFront();
        });
    }

    private IReadOnlyList<HistoryEntry> SnapshotOnCallSession()
    {
        lock (_onCallSession) return _onCallSession.ToList();
    }

    private void RefreshOnCallSession()
    {
        if (_onCallSessionForm is { IsDisposed: false }) RunOnUi(() => _onCallSessionForm?.Reload());
    }

    private string OnCallSessionText()
    {
        lock (_onCallSession)
            return string.Join(Environment.NewLine,
                _onCallSession.Select(e => $"[{e.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss}] {e.Text}"));
    }

    public void Quit()
    {
        Dispose();
        Application.ExitThread();
    }

    // ---- helpers ----

    private void CopyOverlayText()
    {
        // OnCall: copy the WHOLE current session (timestamped), like macOS. PTT: copy the current
        // overlay text, falling back to the most recent history entry.
        string? text;
        if (_settings.Mode == DictationMode.OnCall)
            text = OnCallSessionText();
        else
        {
            text = _overlay?.CurrentText;
            if (string.IsNullOrEmpty(text)) text = _history.List().FirstOrDefault()?.Text;
        }
        if (!string.IsNullOrEmpty(text))
            RunOnUi(() => { try { Clipboard.SetText(text!); } catch { } });
    }

    private void RefreshOpenWindows()
    {
        if (_popup is { Visible: true }) RunOnUi(() => _popup?.Invalidate());
    }

    private void NotifyExternallyChanged()
    {
        // Keep an open Settings window's controls in sync after a programmatic change.
        RunOnUi(() =>
        {
            _popup?.Invalidate();
            if (_tray?.ContextMenuStrip is { } m) RebuildTrayMenu(m);
        });
    }

    private void RunOnUi(Action action)
    {
        if (_ui is not null) _ui.Post(_ => action(), null);
        else action();
    }

    private static string Trunc(string? s, int n = 40)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "…");

    private static bool DetectDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v) return v == 0;
        }
        catch { }
        return true;
    }

    public void Dispose()
    {
        Updater.Cleanup();
        _hotkey?.Dispose(); _hotkey = null;
        StopEngine();
        _overlay?.Dispose(); _overlay = null;
        _popup?.Dispose(); _popup = null;
        _dl?.Dispose(); _dl = null;
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
    }
}
