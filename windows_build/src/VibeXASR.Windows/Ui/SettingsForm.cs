using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using VibeXASR.Windows.Models;
using VibeXASR.Windows.Storage;

namespace VibeXASR.Windows.Ui;

/// <summary>
/// The Preferences window — a faithful WinForms port of the macOS <c>SettingsView.swift</c>:
/// a left sidebar (General / Dictation / Model / Records / Permissions / About) and a
/// scrolling content pane of grouped rows. Every control is live-wired through
/// <see cref="IAppController"/>, so tier/VAD/hotkey/language changes apply immediately.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly IAppController _app;
    private Settings S => _app.Settings;

    private const int SidebarW = 196;
    private const int InnerX = 24;
    private const int InnerTop = 22;
    private int _innerWidth;

    private Panel _content = null!;
    private readonly List<SidebarButton> _tabButtons = new();
    private string _tab = "general";

    // Model-tab live bits, refreshed on a timer while that tab is visible.
    private readonly System.Windows.Forms.Timer _modelTimer = new() { Interval = 300 };
    private readonly List<TierCard> _tierCards = new();
    private readonly List<TierManageRow> _manageRows = new();
    private Control? _switchBanner;
    private MicMeterControl? _micMeter;

    private static readonly ModelTier[] Tiers =
        { ModelTier.Ms160, ModelTier.Ms480, ModelTier.Ms960, ModelTier.Ms1920 };

    public SettingsForm(IAppController app)
    {
        _app = app;
        Text = L10n.T("settings.window.title");
        ClientSize = new Size(SidebarW + 1 + 564, 612);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Surface;
        Font = Theme.Ui(9.5f);
        _innerWidth = ClientSize.Width - SidebarW - 1 - InnerX * 2 - SystemInformation.VerticalScrollBarWidth;

        BuildSidebar();
        BuildContentHost();

        _modelTimer.Tick += (_, _) => RefreshModelTab();
        L10n.LanguageChanged += OnLanguageChanged;
        FormClosed += (_, _) => { _modelTimer.Dispose(); _micMeter?.Stop(); L10n.LanguageChanged -= OnLanguageChanged; };

        Select("general");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(Handle);
    }

    private void OnLanguageChanged()
    {
        if (IsDisposed) return;
        Text = L10n.T("settings.window.title");
        foreach (var b in _tabButtons) b.Invalidate();
        RebuildCurrentTab();
    }

    // ---- sidebar ----

    private void BuildSidebar()
    {
        var bar = new Panel { Left = 0, Top = 0, Width = SidebarW, Height = ClientSize.Height,
                              BackColor = Theme.Surface };
        Controls.Add(bar);

        var logo = new LogoTile { Bounds = new Rectangle(16, 18, 22, 22) };
        bar.Controls.Add(logo);
        var name = new Label
        {
            Text = L10n.T("app.name"), Font = Theme.Ui(11f, FontStyle.Bold),
            ForeColor = Theme.Text, AutoSize = true, Location = new Point(46, 21),
            BackColor = Color.Transparent,
        };
        bar.Controls.Add(name);

        var divider = new Panel { Left = SidebarW, Top = 0, Width = 1, Height = ClientSize.Height,
                                  BackColor = Theme.Hairline };
        Controls.Add(divider);

        (string id, string key, string icon)[] tabs =
        {
            ("general", "tab.general", "⚙"),
            ("dictation", "tab.dictation", "🎙"),
            ("model", "tab.model", "🧠"),
            ("records", "tab.records", "📋"),
            ("permissions", "tab.permissions", "🔐"),
            ("about", "tab.about", "ⓘ"),
        };
        int y = 58;
        foreach (var (id, key, icon) in tabs)
        {
            var btn = new SidebarButton(id, icon, key)
            {
                Bounds = new Rectangle(10, y, SidebarW - 20, 36),
            };
            btn.Clicked += () => Select(id);
            bar.Controls.Add(btn);
            _tabButtons.Add(btn);
            y += 38;
        }
    }

    private void BuildContentHost()
    {
        _content = new Panel
        {
            Left = SidebarW + 1, Top = 0,
            Width = ClientSize.Width - SidebarW - 1,
            Height = ClientSize.Height,
            BackColor = Theme.Surface,
            AutoScroll = true,
            Padding = new Padding(0, 0, 0, 18),
        };
        Controls.Add(_content);
    }

    private void Select(string tab)
    {
        _tab = tab;
        foreach (var b in _tabButtons) b.Selected = b.Id == tab;
        RebuildCurrentTab();
    }

    /// <summary>Programmatically switch tabs (used by the VIBEXASR_OPEN launch hook).</summary>
    public void ShowTab(string id) => Select(id);

    private void RebuildCurrentTab()
    {
        _modelTimer.Stop();
        _micMeter?.Stop();
        _micMeter = null;
        _tierCards.Clear();
        _manageRows.Clear();
        _switchBanner = null;
        _content.SuspendLayout();
        _content.Controls.Clear();
        _content.AutoScroll = true;
        _content.AutoScrollPosition = Point.Empty;

        try
        {
            var col = new Column(_content, InnerX, InnerTop, _innerWidth);
            switch (_tab)
            {
                case "general": BuildGeneral(col); break;
                case "dictation": BuildDictation(col); break;
                case "model": BuildModel(col); _modelTimer.Start(); break;
                case "records": BuildRecords(); break;
                case "permissions": BuildPermissions(col); break;
                default: BuildAbout(col); break;
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"SettingsForm tab '{_tab}' build FAILED: {ex}");
            _content.Controls.Add(new Label
            {
                Text = "⚠ " + ex.Message, Dock = DockStyle.Top, AutoSize = false, Height = 60,
                ForeColor = Theme.Error, BackColor = Theme.Surface, Padding = new Padding(16),
            });
        }
        _content.ResumeLayout();
    }

    // ---- General ----

    private void BuildGeneral(Column col)
    {
        var rows = new List<Control>
        {
            Row(L10n.T("gen.launchAtLogin"), L10n.T("gen.launchAtLogin.help"),
                Toggle(S.LaunchAtLogin, v => _app.SetLaunchAtLogin(v))),
            Row(L10n.T("gen.lang"), L10n.T("gen.lang.help"), LangSelect()),
        };
        col.AddGroup(L10n.T("grp.general"), rows);
    }

    private Control LangSelect()
    {
        var sel = new VibeSelect
        {
            Width = 172,
            Options = new[]
            {
                ("auto", L10n.Display(Lang.Auto)), ("zh", "中文"), ("en", "English"),
                ("ja", "日本語"), ("ko", "한국어"),
            },
            Value = L10n.ToCode(L10n.Current),
        };
        sel.SelectionChanged += (_, v) => _app.SetLanguage(L10n.FromCode(v));
        return sel;
    }

    // ---- Dictation ----

    private void BuildDictation(Column col)
    {
        var hotkey = new HotkeyRecorder { Vk = S.HotkeyVk, Width = 150 };
        hotkey.HotkeyChanged += vk => _app.SetHotkey(vk);

        var rows = new List<Control>
        {
            Row(L10n.T("dict.hotkey"), L10n.T("dict.hotkey.help"), hotkey),
            ModeList(),
            Row(L10n.T("dict.clipOverwrite"), L10n.T("dict.clipOverwrite.help"),
                Toggle(S.ClipboardOverwrite, v => _app.SetClipboardOverwrite(v))),
            Row(L10n.T("dict.history"), L10n.T("dict.history.help"),
                Toggle(S.HistoryEnabled, v => _app.SetHistoryEnabled(v))),
        };
        col.AddGroup(L10n.T("grp.dictation"), rows);
    }

    private Control ModeList()
    {
        var host = new Panel { BackColor = Theme.Surface, Width = _innerWidth };
        var caption = new Label
        {
            Text = L10n.T("dict.mode"), Font = Theme.Ui(10.5f, FontStyle.Bold),
            ForeColor = Theme.Text, AutoSize = true, Location = new Point(16, 13),
            BackColor = Color.Transparent,
        };
        host.Controls.Add(caption);

        (DictationMode mode, string title, string desc, string? warn)[] modes =
        {
            (DictationMode.Paste, "dict.mode.paste.title", "dict.mode.paste.desc", null),
            (DictationMode.Type, "dict.mode.type.title", "dict.mode.type.desc", "dict.mode.type.warn"),
            (DictationMode.OnCall, "dict.mode.oncall.title", "dict.mode.oncall.desc", null),
        };
        int y = 40;
        int w = _innerWidth - 32;
        var cards = new List<DictationModeRow>();
        foreach (var (mode, tk, dk, wk) in modes)
        {
            var card = new DictationModeRow(L10n.T(tk), L10n.T(dk), w, wk is null ? null : L10n.T(wk)) { Selected = S.Mode == mode };
            card.Location = new Point(16, y);
            var capturedMode = mode;
            card.Clicked += () =>
            {
                _app.SetMode(capturedMode);
                foreach (var c in cards) c.Selected = false;
                card.Selected = true;
            };
            cards.Add(card);
            host.Controls.Add(card);
            y += card.Height + 8;
        }
        host.Height = y - 8 + 13;
        return host;
    }

    // ---- Model ----

    private void BuildModel(Column col)
    {
        _switchBanner = SwitchingBanner();
        _switchBanner.Visible = _app.EngineSwapping;
        col.AddRaw(_switchBanner);
        if (_switchBanner.Visible) col.Gap(16); else col.Shrink(_switchBanner.Height);

        // X-ASR streaming model group: headline + source + tier 2x2 + recognition lang.
        var headline = ModelHeadline();
        var source = Row(L10n.T("model.source"), null, SourceLink());
        var tierGrid = TierGrid();
        var langRow = Row(L10n.T("model.aslang"), null, MutedValue(L10n.T("model.aslang.zhen")));
        col.AddGroup(L10n.T("grp.xasr"), new List<Control> { headline, source, tierGrid, langRow });

        // Model management group: per-tier rows.
        var manage = new List<Control>();
        foreach (var tier in Tiers)
        {
            var row = new TierManageRow(_app.Models, tier, _innerWidth) { IsActive = S.Tier == tier };
            row.UseRequested += () => _app.SelectTier(tier);
            manage.Add(row);
            _manageRows.Add(row);
        }
        col.AddGroup(L10n.T("grp.models"), manage);

        // VAD (de-emphasized).
        // Only silero is supported on Windows (FireRedVAD is a macOS-only onnxruntime shim).
        var vadSel = new VibeSelect
        {
            Width = 180,
            Options = new[] { ("silero", L10n.T("model.vad.silero")) },
            Value = "silero",
        };
        vadSel.SelectionChanged += (_, v) => _app.SetVad(VadKind.Silero);
        col.AddGroup(L10n.T("grp.vad"),
            new List<Control> { Row(L10n.T("model.vad"), L10n.T("model.vad.help"), vadSel) });
    }

    private void RefreshModelTab()
    {
        if (_tab != "model") return;
        if (_switchBanner is not null) _switchBanner.Visible = _app.EngineSwapping;
        foreach (var c in _tierCards) c.Selected = S.Tier == c.Tier;
        foreach (var r in _manageRows) { r.IsActive = S.Tier == r.Tier; r.Refresh(); }
    }

    private Control ModelHeadline()
    {
        // Owner-drawn (no overlapping child labels): accent title on the left, a rounded
        // accent-soft repo badge pinned to the right.
        var p = new Panel { BackColor = Theme.Surface, Width = _innerWidth, Height = 46 };
        p.Paint += (_, e) =>
        {
            var g = e.Graphics; Draw.Hq(g);
            var repoText = L10n.T("model.headline.repo");
            var repoFont = Theme.Mono(8f);
            int rw = TextRenderer.MeasureText(repoText, repoFont).Width + 16;
            int rx = _innerWidth - rw - 16;
            Draw.FillRounded(g, new RectangleF(rx, 13, rw, 20), 10, Theme.AccentSoft);
            TextRenderer.DrawText(g, repoText, repoFont, new Rectangle(rx, 13, rw, 20), Theme.AccentB,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, L10n.T("model.headline.title"), Theme.Ui(11.5f, FontStyle.Bold),
                new Rectangle(16, 0, rx - 24, 46), Theme.AccentA,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        };
        return p;
    }

    private Control SourceLink()
    {
        var link = new LinkLabel
        {
            Text = L10n.T("model.source.value"), AutoSize = true, Font = Theme.Mono(8f),
            LinkColor = Theme.AccentB, ActiveLinkColor = Theme.AccentA, BackColor = Color.Transparent,
        };
        link.LinkClicked += (_, _) => OpenUrl("https://huggingface.co/GilgameshWind/X-ASR-zh-en");
        return link;
    }

    private Control TierGrid()
    {
        var host = new Panel { BackColor = Theme.Surface, Width = _innerWidth };
        var caption = new Label
        {
            Text = L10n.T("model.tier"), Font = Theme.Ui(10.5f, FontStyle.Bold), ForeColor = Theme.Text,
            AutoSize = true, Location = new Point(16, 12), BackColor = Color.Transparent,
        };
        var help = new Label
        {
            Text = L10n.T("model.tier.help"), Font = Theme.Ui(8.5f), ForeColor = Theme.TextMuted,
            AutoSize = false, Location = new Point(16, 32), Size = new Size(_innerWidth - 32, 32),
            BackColor = Color.Transparent,
        };
        host.Controls.Add(caption);
        host.Controls.Add(help);

        int gap = 8;
        int cardW = (_innerWidth - 32 - gap) / 2;
        int y = 70;
        for (int i = 0; i < Tiers.Length; i++)
        {
            var tier = Tiers[i];
            var card = new TierCard(tier, cardW) { Selected = S.Tier == tier };
            int cx = 16 + (i % 2) * (cardW + gap);
            int cy = y + (i / 2) * (card.Height + gap);
            card.Location = new Point(cx, cy);
            card.Clicked += () =>
            {
                _app.SelectTier(tier);
                foreach (var c in _tierCards) c.Selected = c.Tier == tier;
            };
            _tierCards.Add(card);
            host.Controls.Add(card);
        }
        int rows = (Tiers.Length + 1) / 2;
        host.Height = y + rows * (_tierCards[0].Height + gap) + 6;
        return host;
    }

    private Control SwitchingBanner()
    {
        var p = new RoundedPanel
        {
            Fill = Theme.AccentSoft, Border = Color.FromArgb(115, Theme.AccentA),
            Radius = Theme.RadiusCard, ClipToRound = true,
            Width = _innerWidth, Height = 44,
        };
        var label = new Label
        {
            Text = L10n.T("model.switching.banner"), Font = Theme.Ui(10f, FontStyle.Bold),
            ForeColor = Theme.AccentA, AutoSize = false, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0),
            BackColor = Color.Transparent,
        };
        p.Controls.Add(label);
        return p;
    }

    // ---- Records (embedded history) ----

    private void BuildRecords()
    {
        var panel = new HistoryPanel(_app.History)
        {
            Left = 0, Top = 0,
            Width = _content.ClientSize.Width,
            Height = _content.ClientSize.Height,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };
        _content.AutoScroll = false;
        _content.Controls.Add(panel);
    }

    // ---- Permissions ----

    private void BuildPermissions(Column col)
    {
        bool zh = L10n.Resolved == Lang.Zh;

        // ---- Microphone device + LIVE level meter (so the user can pick a mic and SEE input) ----
        var opts = new List<(string, string)> { ("", zh ? "系统默认麦克风" : "System default mic") };
        foreach (var d in _app.MicDevices()) opts.Add((d.Id, d.Name));
        var sel = new VibeSelect { Width = 300, Options = opts.ToArray(), Value = _app.MicDeviceId };
        sel.SelectionChanged += (_, id) => { _app.SetMicDevice(id); _micMeter?.SetDevice(id); };
        var deviceRow = Row(zh ? "麦克风设备" : "Microphone device", null, sel);

        col.AddGroup(zh ? "麦克风" : "MICROPHONE", new List<Control> { deviceRow, MicMeterRow(zh) });

        // ---- Permission status ----
        bool mic = _app.MicGranted();
        var banner = PermBanner(mic);
        var micRow = PermRow(L10n.T("perm.mic"), L10n.T("perm.mic.help"), mic, () => _app.OpenMicPrivacy());
        var inputRow = Row(L10n.T("perm.input"), L10n.T("perm.input.help"), MutedValue("ⓘ"));

        var recheck = new VibeButton { Text = L10n.T("perm.recheck"), Style = VibeButton.Kind.Ghost,
                                       Size = new Size(110, 30) };
        var recheckHost = new Panel { BackColor = Theme.Surface, Width = _innerWidth, Height = 56 };
        recheck.Location = new Point(_innerWidth - recheck.Width - 16, 13);
        recheck.Click += (_, _) => RebuildCurrentTab();
        recheckHost.Controls.Add(recheck);

        col.AddGroup(L10n.T("grp.permissions"),
            new List<Control> { banner, micRow, inputRow, recheckHost });
    }

    /// <summary>A row hosting the live mic level meter + a hint; creates and starts the meter.</summary>
    private Control MicMeterRow(bool zh)
    {
        var host = new Panel { BackColor = Theme.Surface, Width = _innerWidth, Height = 66 };
        var hint = new Label
        {
            Text = zh ? "对着麦克风说话,下面的音量条应随声音跳动:" : "Speak — the level bar below should move:",
            Font = Theme.Ui(9.5f), ForeColor = Theme.TextMuted, AutoSize = false,
            Location = new Point(16, 10), Size = new Size(_innerWidth - 32, 18), BackColor = Color.Transparent,
        };
        host.Controls.Add(hint);
        _micMeter = new MicMeterControl { Location = new Point(16, 36), Size = new Size(_innerWidth - 32, 18) };
        host.Controls.Add(_micMeter);
        _micMeter.SetDevice(_app.MicDeviceId);
        _micMeter.Start();
        return host;
    }

    private Control PermBanner(bool ok)
    {
        var tint = ok ? Theme.Success : Theme.Warn;
        var text = ok ? L10n.T("perm.banner.ok") : L10n.T("perm.banner.warn");
        var font = Theme.Ui(10f);
        int h = MeasureWrapped(text, font, _innerWidth - 28);
        var p = new Panel { BackColor = Blend(tint, Theme.Surface, 0.13f), Width = _innerWidth, Height = Math.Max(46, h + 24) };
        var label = new Label
        {
            Text = text, Font = font, ForeColor = tint, AutoSize = false, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 14, 0),
            BackColor = Color.Transparent,
        };
        p.Controls.Add(label);
        return p;
    }

    private Control PermRow(string title, string help, bool granted, Action onOpen)
    {
        // Fixed-size cluster (not AutoSize) so SettingsRow knows its width at layout time —
        // an AutoSize FlowLayoutPanel reports width 0 until parented, which mis-positioned it.
        var cluster = new FlowLayoutPanel { AutoSize = false, BackColor = Color.Transparent,
                                            FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var pill = new StatusPill(granted);
        cluster.Controls.Add(pill);
        if (!granted)
        {
            var open = new VibeButton { Text = L10n.T("perm.openSettings"), Style = VibeButton.Kind.Solid,
                                        Size = new Size(150, 30), Margin = new Padding(8, 0, 0, 0) };
            open.Click += (_, _) => onOpen();
            cluster.Controls.Add(open);
        }
        cluster.Size = cluster.GetPreferredSize(Size.Empty);
        return Row(title, help, cluster);
    }

    // ---- About ----

    private void BuildAbout(Column col)
    {
        var card = new Panel { BackColor = Theme.Surface, Width = _innerWidth };
        int cx = _innerWidth / 2;
        int y = 36;

        var logo = new LogoTile { Bars = new float[] { 10, 22, 30, 18, 12 }, BarW = 4, Gap = 3,
                                  Radius = 16, Size = new Size(64, 64), Location = new Point(cx - 32, y) };
        card.Controls.Add(logo); y += 64 + 12;

        var name = CenterLabel(L10n.T("app.name"), Theme.Ui(17f, FontStyle.Bold), Theme.Text, _innerWidth, y); y += 30;
        card.Controls.Add(name);
        var vv = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        var ver = CenterLabel(L10n.T("about.version", $"{vv.Major}.{vv.Minor}.{Math.Max(0, vv.Build)}"),
                              Theme.Mono(8.5f), Theme.TextMuted, _innerWidth, y); y += 30;
        card.Controls.Add(ver);

        // Check-for-updates (WinSparkle) — drives the EdDSA-signed appcast like macOS Sparkle.
        var upd = new VibeButton { Text = L10n.T("about.checkUpdate"), Style = VibeButton.Kind.Ghost,
                                   Size = new Size(120, 30) };
        upd.Location = new Point(cx - upd.Width / 2, y);
        upd.Click += (_, _) => Updater.CheckForUpdatesUi();
        card.Controls.Add(upd); y += 30 + 12;

        // X-ASR credit card.
        var credit = new RoundedPanel { Fill = Theme.AccentSoft, Border = Color.FromArgb(102, Theme.AccentA),
                                        Radius = Theme.RadiusCard, ClipToRound = true,
                                        Width = _innerWidth - 24, Location = new Point(12, y) };
        int cy = 16;
        var ct = CenterLabel(L10n.T("about.xasr.title"), Theme.Ui(14f, FontStyle.Bold), Theme.AccentA, credit.Width, cy);
        ct.Cursor = Cursors.Hand; ct.Click += (_, _) => OpenUrl("https://github.com/Gilgamesh-J/X-ASR");
        credit.Controls.Add(ct); cy += 26;
        var cd = new Label { Text = L10n.T("about.xasr.desc"), Font = Theme.Ui(9.5f), ForeColor = Theme.Text,
                             AutoSize = false, TextAlign = ContentAlignment.TopCenter,
                             Location = new Point(16, cy), Size = new Size(credit.Width - 32, 40),
                             BackColor = Color.Transparent };
        cd.Height = MeasureWrapped(cd.Text, cd.Font, credit.Width - 32);
        credit.Controls.Add(cd); cy += cd.Height + 8;
        var repo = CenterLabel(L10n.T("about.xasr.repo"), Theme.Mono(8.5f), Theme.AccentB, credit.Width, cy);
        repo.Cursor = Cursors.Hand; repo.Click += (_, _) => OpenUrl("https://huggingface.co/GilgameshWind/X-ASR-zh-en");
        credit.Controls.Add(repo); cy += 26;
        credit.Height = cy;
        card.Controls.Add(credit); y += credit.Height + 22;

        var credits = CenterLabel(L10n.T("about.credits"), Theme.Mono(8f), Theme.TextMuted, _innerWidth, y); y += 22;
        card.Controls.Add(credits);
        var stack = CenterLabel("sherpa-onnx · FireRedVAD · onnxruntime · kaldi-native-fbank · silero-vad · NAudio",
                                Theme.Ui(9f), Theme.TextMuted, _innerWidth, y); y += 28;
        card.Controls.Add(stack);
        var local = CenterLabel(L10n.T("about.local"), Theme.Mono(8.5f), Theme.TextMuted, _innerWidth, y); y += 30;
        card.Controls.Add(local);

        card.Height = y;
        col.AddGroup(null, new List<Control> { card });
    }

    // ---- shared row / group builders ----

    private VibeToggle Toggle(bool initial, Action<bool> onChange)
    {
        var t = new VibeToggle { Checked = initial };
        t.CheckedChanged += (_, _) => onChange(t.Checked);
        return t;
    }

    private Label MutedValue(string text) => new()
    {
        Text = text, Font = Theme.Ui(10f), ForeColor = Theme.TextMuted, AutoSize = true,
        BackColor = Color.Transparent,
    };

    /// <summary>Build a `.row`: title + optional help on the left, a control on the right.</summary>
    private SettingsRow Row(string title, string? help, Control control)
        => new(title, help, control, _innerWidth);

    private Label CenterLabel(string text, Font font, Color color, int width, int y) => new()
    {
        Text = text, Font = font, ForeColor = color, AutoSize = false,
        TextAlign = ContentAlignment.TopCenter, Location = new Point(0, y),
        Size = new Size(width, MeasureWrapped(text, font, width - 24) + 2),
        BackColor = Color.Transparent,
    };

    // ---- helpers ----

    internal static int MeasureWrapped(string text, Font font, int width)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var sz = TextRenderer.MeasureText(text, font, new Size(width, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        return Math.Max(font.Height, sz.Height);
    }

    internal static Color Blend(Color a, Color b, float t)
        => Color.FromArgb(
            (int)(a.R * t + b.R * (1 - t)),
            (int)(a.G * t + b.G * (1 - t)),
            (int)(a.B * t + b.B * (1 - t)));

    internal static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}

// =====================================================================
//  Layout helper + custom row/card controls used by SettingsForm
// =====================================================================

/// <summary>Stacks group/raw controls top-down inside an AutoScroll content panel.</summary>
internal sealed class Column
{
    private readonly Panel _host;
    private readonly int _x;
    private int _y;
    private readonly int _width;

    public Column(Panel host, int x, int top, int width) { _host = host; _x = x; _y = top; _width = width; }

    public void Gap(int h) => _y += h;
    public void Shrink(int h) => _y -= h;

    public void AddRaw(Control c)
    {
        c.Location = new Point(_x, _y);
        c.Width = _width;
        _host.Controls.Add(c);
        _y += c.Height;
    }

    /// <summary>An uppercase mono label over a hairline-separated card of rows.</summary>
    public void AddGroup(string? label, List<Control> rows)
    {
        if (!string.IsNullOrEmpty(label))
        {
            var lbl = new Label
            {
                Text = label, Font = Theme.Mono(8f), ForeColor = Theme.TextMuted,
                AutoSize = true, Location = new Point(_x + 2, _y), BackColor = Color.Transparent,
            };
            _host.Controls.Add(lbl);
            _y += 22;
        }

        // Card: hairline fill shows as 1px separators between Surface rows.
        var card = new RoundedPanel
        {
            Fill = Theme.Hairline, Border = Theme.Hairline, Radius = Theme.RadiusCard,
            ClipToRound = true, Location = new Point(_x, _y), Width = _width,
        };
        int cy = 0;
        foreach (var row in rows)
        {
            row.Location = new Point(0, cy);
            row.Width = _width;
            card.Controls.Add(row);
            cy += row.Height + 1;
        }
        card.Height = Math.Max(1, cy - 1);
        _host.Controls.Add(card);
        _y += card.Height + 26;
    }
}

/// <summary>`.row` — title + optional help on the left, a control vertically centered on the right.</summary>
internal sealed class SettingsRow : Panel
{
    private readonly string _title;
    private readonly string? _help;
    private readonly Control _control;

    public SettingsRow(string title, string? help, Control control, int width)
    {
        _title = title; _help = help; _control = control;
        DoubleBuffered = true;
        BackColor = Theme.Surface;
        Width = width;
        Controls.Add(control);

        int textW = width - 32 - (control.Width + 16);
        int titleH = Theme.Ui(10.5f, FontStyle.Bold).Height;
        int helpH = help is null ? 0 : 3 + SettingsForm.MeasureWrapped(help, Theme.Ui(8.5f), textW);
        Height = Math.Max(48, 13 + titleH + helpH + 13);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        _control.Location = new Point(Width - _control.Width - 16, (Height - _control.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; Draw.Hq(g);
        int textW = Width - 32 - (_control.Width + 16);
        var titleFont = Theme.Ui(10.5f, FontStyle.Bold);
        TextRenderer.DrawText(g, _title, titleFont, new Rectangle(16, 13, textW, titleFont.Height),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        if (_help is not null)
        {
            var helpFont = Theme.Ui(8.5f);
            int hy = 13 + titleFont.Height + 3;
            TextRenderer.DrawText(g, _help, helpFont, new Rectangle(16, hy, textW, Height - hy - 8),
                Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>A sidebar tab button: emoji + label, accent-soft fill when selected.</summary>
internal sealed class SidebarButton : Control
{
    public string Id { get; }
    private readonly string _icon;
    private readonly string _key;
    private bool _selected, _hover;
    public event Action? Clicked;

    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    public SidebarButton(string id, string icon, string key)
    {
        Id = id; _icon = icon; _key = key;
        DoubleBuffered = true; Cursor = Cursors.Hand;
        Font = Theme.Ui(10.5f);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { Clicked?.Invoke(); base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Draw.Hq(g);
        var r = new RectangleF(0, 0, Width, Height);
        if (_selected) Draw.FillRounded(g, r, Theme.RadiusControl, Theme.AccentSoft);
        else if (_hover) Draw.FillRounded(g, r, Theme.RadiusControl, Theme.Hairline);
        TextRenderer.DrawText(g, _icon, Theme.Ui(11f), new Rectangle(8, 0, 22, Height), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, L10n.T(_key), Font, new Rectangle(36, 0, Width - 40, Height), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>One tappable dictation-mode option: radio dot + title + wrapped description.</summary>
internal sealed class DictationModeRow : Control
{
    private readonly string _title;
    private readonly string _desc;
    private readonly string? _warning;
    private bool _selected;
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }
    public event Action? Clicked;

    public DictationModeRow(string title, string desc, int width, string? warning = null)
    {
        _title = title; _desc = desc; _warning = warning;
        DoubleBuffered = true; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Width = width;
        int contentW = width - 24 - 29;
        int descH = SettingsForm.MeasureWrapped(desc, Theme.Ui(8.5f), contentW);
        int warnH = warning is null ? 0 : SettingsForm.MeasureWrapped(warning, Theme.Ui(8.5f, FontStyle.Bold), contentW) + 5;
        Height = 11 + Theme.Ui(10f, FontStyle.Bold).Height + 3 + descH + warnH + 11;
    }

    protected override void OnClick(EventArgs e) { Clicked?.Invoke(); base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Draw.Hq(g);
        var r = new RectangleF(0, 0, Width, Height);
        Draw.FillRounded(g, r, 9, _selected ? Theme.AccentSoft : Theme.Surface2);
        Draw.StrokeRounded(g, r, 9, _selected ? Theme.AccentA : Theme.Hairline, _selected ? 1.5f : 1f);

        // Radio dot.
        float dotX = 12 + 9, dotY = 13 + 9;
        using (var ring = new Pen(_selected ? Theme.AccentA : Theme.HairlineStrong, _selected ? 5f : 1.5f))
            g.DrawEllipse(ring, dotX - 9, dotY - 9, 18, 18);
        if (_selected)
            TextRenderer.DrawText(g, "✓", Theme.Ui(7f, FontStyle.Bold),
                new Rectangle((int)dotX - 9, (int)dotY - 9, 18, 18), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int tx = 41;
        var titleFont = Theme.Ui(10f, FontStyle.Bold);
        TextRenderer.DrawText(g, _title, titleFont, new Rectangle(tx, 11, Width - tx - 12, titleFont.Height),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        var descFont = Theme.Ui(8.5f);
        int dy = 11 + titleFont.Height + 3;
        int descW = Width - tx - 12;
        int descH = SettingsForm.MeasureWrapped(_desc, descFont, descW);
        TextRenderer.DrawText(g, _desc, descFont, new Rectangle(tx, dy, descW, descH),
            Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

        // WeChat caveat (Type/逐字 mode): painted in the warning colour so it stands out.
        if (_warning is not null)
        {
            var warnFont = Theme.Ui(8.5f, FontStyle.Bold);
            int wy = dy + descH + 5;
            TextRenderer.DrawText(g, _warning, warnFont, new Rectangle(tx, wy, descW, Height - wy - 8),
                Theme.Warn, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>A selectable latency-tier scenario card (2×2 grid).</summary>
internal sealed class TierCard : Control
{
    public ModelTier Tier { get; }
    private bool _selected;
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }
    public event Action? Clicked;

    public TierCard(ModelTier tier, int width)
    {
        Tier = tier;
        DoubleBuffered = true; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Width = width; Height = 62;
    }

    protected override void OnClick(EventArgs e) { Clicked?.Invoke(); base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Draw.Hq(g);
        var r = new RectangleF(0, 0, Width, Height);
        Draw.FillRounded(g, r, 9, _selected ? Theme.AccentSoft : Theme.Surface2);
        Draw.StrokeRounded(g, r, 9, _selected ? Theme.AccentA : Theme.Hairline, _selected ? 1.5f : 1f);

        TextRenderer.DrawText(g, L10n.T($"model.tier.{(int)Tier}.name"), Theme.Ui(10f, FontStyle.Bold),
            new Rectangle(12, 9, Width - 40, 20), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        if (_selected)
            TextRenderer.DrawText(g, "✓", Theme.Ui(10f, FontStyle.Bold),
                new Rectangle(Width - 26, 9, 18, 18), Theme.AccentA,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, L10n.T($"model.tier.{(int)Tier}.scene"), Theme.Ui(8.5f),
            new Rectangle(12, 30, Width - 20, Height - 36), Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }
}

/// <summary>`.pill.ok / .pill.bad` permission status pill.</summary>
internal sealed class StatusPill : Control
{
    private readonly bool _ok;
    public StatusPill(bool ok)
    {
        _ok = ok;
        DoubleBuffered = true; Font = Theme.Ui(9.5f, FontStyle.Bold);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        var text = ok ? L10n.T("perm.granted") : L10n.T("perm.denied");
        Size = new Size(TextRenderer.MeasureText(text, Font).Width + 24, 26);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Draw.Hq(g);
        var tint = _ok ? Theme.Success : Theme.Error;
        Draw.FillRounded(g, new RectangleF(0, 0, Width, Height), Height / 2f, Color.FromArgb(41, tint));
        TextRenderer.DrawText(g, _ok ? L10n.T("perm.granted") : L10n.T("perm.denied"), Font,
            new Rectangle(0, 0, Width, Height), tint,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
