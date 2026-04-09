using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BatteryMeter;

public sealed class TaskbarPanel : Form
{
    // Win32 constants
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int GWL_EXSTYLE = -20;
    private const int HWND_TOPMOST = -1;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int ABM_GETTASKBARPOS = 0x00000005;

    [DllImport("user32.dll")] private static extern IntPtr FindWindow(string cls, string? wnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("shell32.dll")] private static extern uint SHAppBarMessage(int msg, ref APPBARDATA data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    private string _text = "…";
    private Color _textColor = Color.Silver;
    private readonly System.Windows.Forms.Timer _posTimer;

    public TaskbarPanel()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 32, 32);
        TopMost = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        // Start small, will be positioned in Load
        Size = new Size(1, 1);

        _posTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _posTimer.Tick += (_, _) => RepositionToTaskbar();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    // Prevent the window from stealing focus
    protected override bool ShowWithoutActivation => true;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RepositionToTaskbar();
        _posTimer.Start();
    }

    public void UpdateDisplay(string text, Color color)
    {
        if (_text == text && _textColor == color) return;
        _text = text;
        _textColor = color;
        RecalcSize();
        Invalidate();
    }

    private void RecalcSize()
    {
        using var g = CreateGraphics();
        var taskbarRect = GetTaskbarRect();
        int taskbarH = taskbarRect.Bottom - taskbarRect.Top;
        if (taskbarH <= 0) taskbarH = 48;

        using var font = MakeFont(taskbarH);
        var textSize = g.MeasureString(_text, font);
        int newW = (int)textSize.Width + 12;
        int newH = taskbarH;

        if (Width != newW || Height != newH)
        {
            Size = new Size(newW, newH);
            RepositionToTaskbar();
        }
    }

    private void RepositionToTaskbar()
    {
        var taskbarRect = GetTaskbarRect();
        int taskbarH = taskbarRect.Bottom - taskbarRect.Top;
        if (taskbarH <= 0) return;

        // Recalc width for current text
        using var g = CreateGraphics();
        using var font = MakeFont(taskbarH);
        var textSize = g.MeasureString(_text, font);
        int panelW = (int)textSize.Width + 12;

        // Position: just left of the tray notification area
        var trayRect = GetTrayNotifyRect();
        int x = trayRect.Left - panelW - 2;
        int y = taskbarRect.Top;

        SetBounds(x, y, panelW, taskbarH);
        SetWindowPos(Handle, (IntPtr)HWND_TOPMOST, x, y, panelW, taskbarH,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        using var font = MakeFont(Height);
        using var brush = new SolidBrush(_textColor);

        var size = g.MeasureString(_text, font);
        float x = (Width - size.Width) / 2f;
        float y = (Height - size.Height) / 2f;
        g.DrawString(_text, font, brush, x, y);
    }

    private static Font MakeFont(int taskbarHeight)
    {
        float fontSize = taskbarHeight * 0.28f;
        if (fontSize < 9f) fontSize = 9f;
        return new Font("Segoe UI", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _posTimer.Stop();
        _posTimer.Dispose();
        base.OnFormClosing(e);
    }

    // Ignore mouse clicks — pass them through
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;
        if (m.Msg == WM_NCHITTEST)
        {
            m.Result = (IntPtr)HTTRANSPARENT;
            return;
        }
        base.WndProc(ref m);
    }

    private static RECT GetTaskbarRect()
    {
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        SHAppBarMessage(ABM_GETTASKBARPOS, ref abd);
        return abd.rc;
    }

    private static RECT GetTrayNotifyRect()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        // On Windows 11, find the system tray area
        // Try the direct child first
        var tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray != IntPtr.Zero)
        {
            GetWindowRect(tray, out var r);
            return r;
        }
        // Fallback: use right portion of taskbar
        GetWindowRect(taskbar, out var tr);
        tr.Left = tr.Right - 300;
        return tr;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? wnd);
}
