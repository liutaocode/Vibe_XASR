using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VibeXASR.Windows.Dictation;
using VibeXASR.Windows.Input;
using VibeXASR.Windows.Models;
using VibeXASR.Windows.Storage;
using VibeXASR.Windows.Ui;

namespace VibeXASR.Windows;

/// <summary>
/// Owns the whole runtime: tray icon + menu, the dictation engine, the mic, the global
/// hotkey, and the overlay. This is the Windows analogue of the macOS AppDelegate /
/// status-item controller.
/// </summary>
public sealed class TrayApp : IDisposable
{
    public ApplicationContext Context { get; } = new();

    private readonly Settings _settings;
    private readonly HistoryStore _history = new();

    private NotifyIcon? _tray;
    private GlobalHotkey? _hotkey;
    private MicCapture? _mic;
    private DictationEngine? _engine;
    private OverlayForm? _overlay;

    // Type-mode streaming diff state: what we've already typed into the target app.
    private string _typedSoFar = string.Empty;

    public TrayApp()
    {
        _settings = Settings.Load();
    }

    public void Start()
    {
        BuildTray();

        _overlay = new OverlayForm();
        _overlay.PositionBottomCenter();
        _overlay.CopyRequested += (_, _) => CopyOverlayText();
        _overlay.StopRequested += (_, _) => SetMode(DictationMode.Paste); // leave OnCall

        // Hotkey is always installed; it only matters for Paste/Type.
        _hotkey = new GlobalHotkey(_settings.HotkeyVk);
        _hotkey.KeyDown += OnHotkeyDown;
        _hotkey.KeyUp += OnHotkeyUp;
        _hotkey.Install();

        // Engine + mic are started lazily once models are present (see EnsureEngineAsync).
        _ = EnsureEngineAsync();
    }

    // ---- tray UI ----

    private void BuildTray()
    {
        _tray = new NotifyIcon
        {
            // TODO(win): ship a real .ico (Resources) and load it here; SystemIcons is a
            // placeholder so the skeleton runs.
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Vibe XASR",
        };

        var menu = new ContextMenuStrip();

        // Mode ▸ paste / type / oncall
        var modeMenu = new ToolStripMenuItem("Mode");
        foreach (DictationMode m in Enum.GetValues<DictationMode>())
        {
            var item = new ToolStripMenuItem(m.ToString())
            {
                Checked = _settings.Mode == m,
                CheckOnClick = false,
                Tag = m,
            };
            item.Click += (_, _) => SetMode((DictationMode)item.Tag!);
            modeMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(modeMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("History…", null, (_, _) => ShowHistory());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _tray.ContextMenuStrip = menu;
    }

    private void RefreshModeChecks()
    {
        if (_tray?.ContextMenuStrip?.Items[0] is ToolStripMenuItem modeMenu)
            foreach (ToolStripMenuItem item in modeMenu.DropDownItems.OfType<ToolStripMenuItem>())
                item.Checked = (DictationMode)item.Tag! == _settings.Mode;
    }

    // ---- model bootstrap ----

    private async Task EnsureEngineAsync()
    {
        var paths = ModelPaths.ForTier(_settings.Tier);
        if (!paths.AsrModelPresent() || !paths.VadPresent(_settings.Vad))
        {
            // TODO(win): show a proper progress dialog. For now use a balloon tip and download.
            _tray?.ShowBalloonTip(3000, "Vibe XASR",
                $"Downloading {(int)_settings.Tier}ms model…", ToolTipIcon.Info);
            try
            {
                var dl = new ModelDownloader();
                var progress = new Progress<DownloadProgress>(p =>
                {
                    // TODO(win): reflect p.Fraction in a real UI.
                });
                await dl.EnsureTierAsync(paths, progress);
                await dl.EnsureVadAsync(paths.VadFileFor(_settings.Vad), progress);
            }
            catch (Exception ex)
            {
                _tray?.ShowBalloonTip(5000, "Vibe XASR",
                    "Model download failed: " + ex.Message, ToolTipIcon.Error);
                return;
            }
        }

        StartEngine();
    }

    private void StartEngine()
    {
        StopEngine();

        try
        {
            _engine = new DictationEngine(_settings) { Mode = _settings.Mode };
            _engine.OnPartial += OnPartial;
            _engine.OnFinal += OnFinal;
            _engine.Start();

            _mic = new MicCapture();
            _mic.FrameAvailable += (_, frame) => _engine?.PushFrame(frame);
            _mic.Start();

            if (_settings.Mode == DictationMode.OnCall)
                EnterOnCall();
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloonTip(5000, "Vibe XASR",
                "Engine start failed: " + ex.Message, ToolTipIcon.Error);
        }
    }

    private void StopEngine()
    {
        _mic?.Dispose(); _mic = null;
        if (_engine is not null)
        {
            _engine.OnPartial -= OnPartial;
            _engine.OnFinal -= OnFinal;
            _engine.Dispose();
            _engine = null;
        }
    }

    // ---- hotkey -> engine ----

    private void OnHotkeyDown(object? sender, EventArgs e)
    {
        if (_settings.Mode == DictationMode.OnCall) return;
        _typedSoFar = string.Empty;
        _engine?.BeginHold();
        _overlay?.ShowTransient();
        _overlay?.SetText("…"); // listening ellipsis
    }

    private void OnHotkeyUp(object? sender, EventArgs e)
    {
        if (_settings.Mode == DictationMode.OnCall) return;
        _engine?.EndHold();
        // Final text arrives via OnFinal; overlay hides there.
    }

    // ---- engine events ----

    private void OnPartial(object? sender, PartialEventArgs e)
    {
        switch (_settings.Mode)
        {
            case DictationMode.Paste:
            case DictationMode.OnCall:
                _overlay?.SetText(e.Text);
                break;

            case DictationMode.Type:
                // Stream char-by-char: diff against what we've already typed and emit the delta
                // (backspace the divergent tail, then type the new tail).
                StreamTypeDiff(e.Text);
                _overlay?.SetText(e.Text);
                break;
        }
    }

    private void OnFinal(object? sender, FinalEventArgs e)
    {
        _history.Append(e.Text);

        switch (_settings.Mode)
        {
            case DictationMode.Paste:
                TextInserter.InsertText(e.Text);
                _overlay?.HideOverlay();
                break;

            case DictationMode.Type:
                // Ensure the final text is fully reflected (in case the last partial != final).
                StreamTypeDiff(e.Text);
                _typedSoFar = string.Empty;
                _overlay?.HideOverlay();
                break;

            case DictationMode.OnCall:
                // Keep showing; user copies manually. Overlay text already set by OnPartial.
                _overlay?.SetText(e.Text);
                break;
        }
    }

    /// <summary>
    /// Type-mode incremental insertion: compute the common prefix between what we've typed
    /// and the new hypothesis, backspace the rest, then type the new suffix. This mirrors the
    /// macOS streaming inserter and handles the recognizer revising earlier characters.
    /// </summary>
    private void StreamTypeDiff(string newText)
    {
        int common = 0;
        int max = Math.Min(_typedSoFar.Length, newText.Length);
        while (common < max && _typedSoFar[common] == newText[common]) common++;

        int toDelete = _typedSoFar.Length - common;
        if (toDelete > 0) TextInserter.Backspace(toDelete);

        var suffix = newText[common..];
        if (suffix.Length > 0) TextInserter.InsertText(suffix);

        _typedSoFar = newText;
    }

    // ---- menu actions ----

    private void SetMode(DictationMode mode)
    {
        if (_settings.Mode == mode) return;

        bool wasOnCall = _settings.Mode == DictationMode.OnCall;
        _settings.Mode = mode;
        _settings.Save();
        RefreshModeChecks();

        if (_engine is not null) _engine.Mode = mode;

        if (wasOnCall && mode != DictationMode.OnCall)
        {
            _overlay?.HideOverlay();
            _overlay?.Hide();
        }
        if (mode == DictationMode.OnCall) EnterOnCall();
    }

    private void EnterOnCall()
    {
        _overlay?.PositionBottomCenter();
        _overlay?.ShowOnCall();
        _overlay?.SetText(string.Empty);
        // OnCall capture is always-on; engine flips _capturing itself based on Mode.
    }

    private void CopyOverlayText()
    {
        // TODO(win): expose the current overlay text; for now copy the latest history entry.
        var latest = _history.List().FirstOrDefault();
        if (latest is not null)
        {
            try { Clipboard.SetText(latest.Text); } catch { /* clipboard busy */ }
        }
    }

    private void ShowHistory()
    {
        // TODO(win): build a real history window (ListView + Clear button). Skeleton shows a
        // message box with the most recent entries.
        var entries = _history.List().Take(20);
        var text = string.Join(Environment.NewLine,
            entries.Select(e => $"{e.Timestamp:g}  {e.Text}"));
        MessageBox.Show(string.IsNullOrEmpty(text) ? "(empty)" : text, "History");
    }

    private void ShowSettings()
    {
        // TODO(win): build a real Settings dialog (mode, tier, hotkey capture, VAD choice,
        // language). Changing tier/VAD should call EnsureEngineAsync() to (re)download +
        // restart the engine. Changing the hotkey should call _hotkey.SetKey(vk).
        MessageBox.Show(
            $"Mode: {_settings.Mode}\nTier: {(int)_settings.Tier}ms\nVAD: {_settings.Vad}\n" +
            $"Hotkey VK: 0x{_settings.HotkeyVk:X2}\nLanguage: {_settings.Language}\n\n" +
            $"Settings file: {Settings.FilePath}",
            "Settings (stub)");
    }

    private void Quit()
    {
        Dispose();
        Application.ExitThread();
    }

    public void Dispose()
    {
        _hotkey?.Dispose(); _hotkey = null;
        StopEngine();
        _overlay?.Dispose(); _overlay = null;
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }
}
