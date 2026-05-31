using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VibeXASR.Windows.Ui;

/// <summary>
/// Borderless, top-most overlay that shows live partial text near the bottom of the
/// primary screen — mirrors the macOS floating caption. Two visual states:
///
///  - Transient (Paste/Type): a small pill that fades in while speaking, shows the
///    current partial, and is hidden on finalize. Click-through (the user keeps
///    interacting with the app underneath).
///  - OnCall: persistent panel with the live transcript plus Copy / Stop buttons.
///
/// Click-through is achieved with WS_EX_LAYERED | WS_EX_TRANSPARENT. In OnCall we
/// disable transparency on the button strip area so the buttons are clickable.
/// </summary>
public sealed class OverlayForm : Form
{
    private readonly Label _text;
    private readonly FlowLayoutPanel _buttons;
    private readonly Button _copyBtn;
    private readonly Button _stopBtn;

    private bool _onCall;

    /// <summary>Raised when the user clicks Copy in OnCall mode.</summary>
    public event EventHandler? CopyRequested;

    /// <summary>Raised when the user clicks Stop in OnCall mode.</summary>
    public event EventHandler? StopRequested;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(20, 20, 20);
        // TODO(win): tweak opacity / use a rounded region for a nicer pill.
        Opacity = 0.92;
        Padding = new Padding(14, 10, 14, 10);

        _text = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14f, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = string.Empty,
        };

        _copyBtn = new Button { Text = "Copy", AutoSize = true };
        _stopBtn = new Button { Text = "Stop", AutoSize = true };
        _copyBtn.Click += (_, _) => CopyRequested?.Invoke(this, EventArgs.Empty);
        _stopBtn.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        _buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Visible = false,
        };
        _buttons.Controls.Add(_stopBtn);
        _buttons.Controls.Add(_copyBtn);

        Controls.Add(_text);
        Controls.Add(_buttons);

        Size = new Size(560, 80);
    }

    /// <summary>Place the overlay centered near the bottom of the primary working area.</summary>
    public void PositionBottomCenter()
    {
        var wa = Screen.PrimaryScreen!.WorkingArea;
        int x = wa.Left + (wa.Width - Width) / 2;
        int y = wa.Bottom - Height - 80;
        Location = new Point(x, y);
    }

    /// <summary>Show as a transient caption (Paste/Type): click-through, no buttons.</summary>
    public void ShowTransient()
    {
        _onCall = false;
        _buttons.Visible = false;
        ApplyClickThrough(true);
        ShowNoActivate();
    }

    /// <summary>Show the persistent OnCall panel with Copy/Stop, clickable.</summary>
    public void ShowOnCall()
    {
        _onCall = true;
        _buttons.Visible = true;
        // Whole window clickable so buttons work. TODO(win): make only the text area
        // click-through while keeping the button strip interactive (per-region hit-testing).
        ApplyClickThrough(false);
        Size = new Size(560, 140);
        ShowNoActivate();
    }

    /// <summary>Update the live text without stealing focus or activating.</summary>
    public void SetText(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetText(text));
            return;
        }
        _text.Text = text;
        if (!Visible) ShowNoActivate();
    }

    public void HideOverlay()
    {
        if (InvokeRequired) { BeginInvoke(HideOverlay); return; }
        if (_onCall) return; // OnCall stays up until Stop
        Hide();
    }

    // ---- never-activate window styles (keeps focus on the user's target app) ----

    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOPMOST = 0x00000008;

    private bool _clickThrough;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // NOACTIVATE + TOOLWINDOW: showing the overlay must not pull focus away from the
            // app receiving dictated text.
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED;
            if (_clickThrough) cp.ExStyle |= WS_EX_TRANSPARENT;
            return cp;
        }
    }

    private void ApplyClickThrough(bool on)
    {
        _clickThrough = on;
        if (IsHandleCreated)
        {
            // Re-apply ex-style live.
            int ex = GetWindowLong(Handle, GWL_EXSTYLE);
            if (on) ex |= WS_EX_TRANSPARENT; else ex &= ~WS_EX_TRANSPARENT;
            SetWindowLong(Handle, GWL_EXSTYLE, ex);
        }
    }

    /// <summary>ShowWindow with SW_SHOWNOACTIVATE so we never take focus.</summary>
    private void ShowNoActivate()
    {
        if (!Visible) Visible = true;
        const int SW_SHOWNOACTIVATE = 4;
        ShowWindow(Handle, SW_SHOWNOACTIVATE);
        // Keep on top without activating.
        SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height,
            SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
    }

    // ---- P/Invoke ----
    // TODO(win): verify these signatures; GetWindowLong/SetWindowLong have ...Ptr variants —
    // on 64-bit you should generally use GetWindowLongPtr/SetWindowLongPtr.
    private const int GWL_EXSTYLE = -20;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
}
