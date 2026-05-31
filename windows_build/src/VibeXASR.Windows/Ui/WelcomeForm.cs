using System;
using System.Drawing;
using System.Windows.Forms;

namespace VibeXASR.Windows.Ui;

/// <summary>
/// First-run onboarding shown once the engine is ready: a clear, on-brand prompt that the app
/// is live and how to use it (hold the key, speak, release). Addresses "after launch there's no
/// prompt, it just sits in the tray". Dark rounded card with a drop shadow; dismiss marks
/// <see cref="Storage.Settings.Welcomed"/> so it never nags again.
/// </summary>
public sealed class WelcomeForm : Form
{
    private readonly IAppController _app;
    private readonly bool _zh;
    private readonly string _keyName;

    public WelcomeForm(IAppController app)
    {
        _app = app;
        _zh = L10n.Resolved == Lang.Zh;
        _keyName = VkNames.Name(app.Settings.HotkeyVk);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        // TopMost so it's actually seen on launch — a background app can't steal foreground
        // focus from the active window (Windows foreground lock), but TopMost floats it above.
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Theme.Surface;
        ClientSize = new Size(460, 392);
        Icon = Branding.AppIcon;
        Text = "Vibe XASR";

        // Launch-at-login toggle.
        var launch = new VibeToggle { Checked = app.Settings.LaunchAtLogin, Location = new Point(ClientSize.Width - 70, 300) };
        launch.CheckedChanged += (_, _) => _app.SetLaunchAtLogin(launch.Checked);
        Controls.Add(launch);

        // Primary "Got it" button.
        var go = new VibeButton { Text = _zh ? "开始使用" : "Get started", Style = VibeButton.Kind.Solid,
                                  Size = new Size(180, 40), Location = new Point((ClientSize.Width - 180) / 2, 336) };
        go.Click += (_, _) => Dismiss();
        Controls.Add(go);

        // Close (X) in the corner.
        var x = new VibeButton { Text = "✕", Style = VibeButton.Kind.Ghost, Size = new Size(30, 28),
                                 Location = new Point(ClientSize.Width - 40, 12) };
        x.Click += (_, _) => Dismiss();
        Controls.Add(x);
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; } // CS_DROPSHADOW
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Region = new Region(Theme.RoundedRect(new RectangleF(0, 0, Width, Height), 18));
        Activate();
        BringToFront();
    }

    private void Dismiss()
    {
        _app.Settings.Welcomed = true;
        _app.Settings.Save();
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Draw.Hq(g);
        int W = ClientSize.Width;
        Draw.StrokeRounded(g, new RectangleF(0, 0, W, Height), 18, Theme.Hairline);

        // Logo tile.
        var logoRect = new RectangleF(W / 2f - 30, 30, 60, 60);
        Draw.FillAccent(g, logoRect, 16);
        Draw.LogoBars(g, logoRect, new float[] { 12, 26, 36, 22, 14 }, 4.5f, 3.5f);

        // Title + ready status.
        TextRenderer.DrawText(g, "Vibe XASR", Theme.Ui(17f, FontStyle.Bold),
            new Rectangle(0, 100, W, 26), Theme.Text, Center);
        using (var dot = new SolidBrush(Theme.Success)) g.FillEllipse(dot, W / 2f - 46, 134, 8, 8);
        TextRenderer.DrawText(g, _zh ? "已就绪" : "Ready", Theme.Ui(10f, FontStyle.Bold),
            new Rectangle(W / 2 - 34, 128, 80, 20), Theme.Success,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // Instruction card.
        var card = new RectangleF(28, 162, W - 56, 96);
        Draw.FillRounded(g, card, 12, Theme.Surface2);
        Draw.StrokeRounded(g, card, 12, Theme.Hairline);

        // Line 1: 按住 [Key] 说话
        string pre = _zh ? "按住 " : "Hold ";
        string post = _zh ? " 说话" : " and speak";
        var f1 = Theme.Ui(13f, FontStyle.Bold);
        var preSz = TextRenderer.MeasureText(pre, f1);
        var keyF = Theme.Ui(11f, FontStyle.Bold);
        int keyW = TextRenderer.MeasureText(_keyName, keyF).Width + 20;
        var postSz = TextRenderer.MeasureText(post, f1);
        int totalW = preSz.Width + keyW + postSz.Width;
        int sx = (W - totalW) / 2, ly = 182;
        TextRenderer.DrawText(g, pre, f1, new Rectangle(sx, ly, preSz.Width, 24), Theme.Text, LeftMid);
        var keyRect = new RectangleF(sx + preSz.Width, ly + 1, keyW, 22);
        Draw.FillRounded(g, keyRect, 6, Theme.AccentSoft);
        Draw.StrokeRounded(g, keyRect, 6, Color.FromArgb(120, Theme.AccentA));
        TextRenderer.DrawText(g, _keyName, keyF, Rectangle.Round(keyRect), Theme.AccentA,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, post, f1, new Rectangle(sx + preSz.Width + keyW, ly, postSz.Width, 24), Theme.Text, LeftMid);

        // Line 2: release → text drops at cursor
        TextRenderer.DrawText(g, _zh ? "松开,识别的文字自动落到光标处" : "Release — the text drops at your cursor",
            Theme.Ui(11f), new Rectangle(28, 218, W - 56, 22), Theme.TextMuted, Center);

        // Privacy line.
        TextRenderer.DrawText(g, _zh ? "🔒 100% 本地 · 离线 · 数据不出设备" : "🔒 100% local · offline · nothing leaves this device",
            Theme.Ui(9.5f), new Rectangle(0, 270, W, 20), Theme.TextMuted, Center);

        // Launch-at-login label (toggle is a child control to its right).
        TextRenderer.DrawText(g, _zh ? "开机自启动" : "Launch at login", Theme.Ui(11f),
            new Rectangle(28, 300, 200, 25), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private const TextFormatFlags Center =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding;
    private const TextFormatFlags LeftMid =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
}
