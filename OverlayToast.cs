using System.Drawing;

namespace DeadCellsUniversalQuickSave;

internal sealed class OverlayToast : Form
{
    private readonly SaveManager _saveManager;
    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _hideTimer;

    public OverlayToast(SaveManager saveManager)
    {
        _saveManager = saveManager;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        ForeColor = Color.White;
        Opacity = 0.84;
        Size = new Size(310, 54);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent
        };
        Controls.Add(_label);

        _hideTimer = new System.Windows.Forms.Timer();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_TRANSPARENT = 0x00000020;
            const int WS_EX_NOACTIVATE = 0x08000000;

            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void ShowMessage(string text, int durationMs)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowMessage(text, durationMs));
            return;
        }

        var hwnd = _saveManager.GetGameWindowHandle();
        if (hwnd == IntPtr.Zero ||
            !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        _label.Text = text;

        var x = Math.Max(rect.Left + 8, rect.Right - Width - 26);
        var y = rect.Top + 28;
        Location = new Point(x, y);

        _hideTimer.Stop();
        _hideTimer.Interval = Math.Clamp(durationMs, 500, 5000);

        if (!Visible)
            Show();

        _hideTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _hideTimer.Dispose();

        base.Dispose(disposing);
    }
}
