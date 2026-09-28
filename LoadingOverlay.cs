using System.Drawing;

namespace DeadCellsUniversalQuickSave;

internal sealed class LoadingOverlay : Form
{
    private readonly SaveManager _saveManager;
    private readonly Label _label;
    private readonly Panel _progressTrack;
    private readonly Panel _progressBar;
    private readonly System.Windows.Forms.Timer _progressTimer;
    private int _progressX;

    public LoadingOverlay(SaveManager saveManager)
    {
        _saveManager = saveManager;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        ForeColor = Color.White;
        Opacity = 1.0;

        _label = new Label
        {
            Dock = DockStyle.Fill,
            Text = "读档中…",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Black
        };

        _progressTrack = new Panel
        {
            Height = 8,
            BackColor = Color.FromArgb(38, 38, 38)
        };

        _progressBar = new Panel
        {
            Height = 8,
            BackColor = Color.FromArgb(46, 204, 113)
        };

        _progressTrack.Controls.Add(_progressBar);

        _progressTimer = new System.Windows.Forms.Timer
        {
            Interval = 30
        };

        _progressTimer.Tick += (_, _) =>
        {
            if (_progressTrack.Width <= 0)
                return;

            _progressX += 12;

            if (_progressX > _progressTrack.Width)
                _progressX = -_progressBar.Width;

            _progressBar.Left = _progressX;
        };

        Resize += (_, _) => LayoutProgressBar();

        Controls.Add(_label);
        Controls.Add(_progressTrack);
        _progressTrack.BringToFront();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_NOACTIVATE = 0x08000000;

            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public bool ShowOverGame()
    {
        if (InvokeRequired)
        {
            var result = false;
            Invoke(() => result = ShowOverGame());
            return result;
        }

        var hwnd = _saveManager.GetGameWindowHandle();
        if (hwnd == IntPtr.Zero ||
            !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        Bounds = new Rectangle(
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));

        LayoutProgressBar();
        _progressX = -_progressBar.Width;
        _progressBar.Left = _progressX;
        _progressTimer.Start();

        Show();
        _progressTrack.BringToFront();
        return true;
    }

    public void HideOverlay()
    {
        if (InvokeRequired)
        {
            BeginInvoke(HideOverlay);
            return;
        }

        _progressTimer.Stop();
        Hide();
    }

    private void LayoutProgressBar()
    {
        var width = Math.Clamp(
            ClientSize.Width * 45 / 100,
            220,
            560);

        width = Math.Min(
            width,
            Math.Max(120, ClientSize.Width - 80));

        _progressTrack.Width = width;
        _progressTrack.Height = 8;
        _progressTrack.Left =
            Math.Max(0, (ClientSize.Width - width) / 2);
        _progressTrack.Top =
            Math.Max(0, ClientSize.Height / 2 + 44);

        _progressBar.Width =
            Math.Max(60, width / 4);
        _progressBar.Height =
            _progressTrack.Height;
        _progressBar.Top = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _progressTimer.Dispose();

        base.Dispose(disposing);
    }
}
