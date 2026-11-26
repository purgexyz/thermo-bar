using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Microsoft.Win32;

namespace TempBar;

/// <summary>
/// Per-pixel-alpha layered window owned by the taskbar, so it stays above it without a z-order fight.
/// </summary>
public sealed class OverlayForm : Form
{
    private readonly Settings _settings;
    private readonly ContextMenuStrip _menu;
    private readonly ToolTip _tip = new() { InitialDelay = 400, ShowAlways = true };

    private Reading _reading;
    private bool _lightTheme;
    private bool _hover;
    private uint _dpi = 96;
    private IntPtr _taskbar;
    private Native.RECT _taskbarRect;
    private Size _size;
    private Point _pos;
    private string _renderKey = "";

    private bool _dragging;
    private int _dragStartCursorX;
    private int _dragStartWindowX;

    public OverlayForm(Settings settings, ContextMenuStrip menu)
    {
        _settings = settings;
        _menu = menu;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        _lightTheme = ReadLightTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style = Native.WS_POPUP;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public void SetReading(Reading reading)
    {
        _reading = reading;
        _tip.SetToolTip(this, TrayContext.Describe(reading, _settings));
        UpdateOverlay(force: false);
    }

    /// <summary>Re-anchors to the taskbar, hides during fullscreen apps, and repaints if anything changed.</summary>
    public void UpdateOverlay(bool force)
    {
        if (!_settings.ShowOverlay)
        {
            if (Visible) Hide();
            return;
        }

        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !Native.GetWindowRect(taskbar, out var tb) || !Native.IsWindowVisible(taskbar)
            || Native.IsFullscreenAppRunning() || IsTaskbarAutoHidden(tb))
        {
            if (Visible) Hide();
            return;
        }

        if (!IsHandleCreated) CreateHandle();

        if (taskbar != _taskbar)
        {
            _taskbar = taskbar;
            Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, taskbar);
            force = true;
        }
        _taskbarRect = tb;

        var dpi = Native.GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;
        if (dpi != _dpi) { _dpi = dpi; force = true; }

        var key = $"{Format(_reading.Cpu)}|{Format(_reading.Gpu)}|{_lightTheme}|{_hover}|{_dpi}|{tb.Height}";
        var pos = ComputePosition(tb, _size);

        // Show before painting: a layered window has no content until UpdateLayeredWindow runs,
        // and WinForms may apply its own bounds during the first Show.
        if (!Visible)
        {
            Show();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            force = true;
        }

        if (force || key != _renderKey || pos != _pos)
        {
            _renderKey = key;
            Render(tb);
        }
    }

    private static bool IsTaskbarAutoHidden(Native.RECT tb)
    {
        var screen = Screen.FromRectangle(Rectangle.FromLTRB(tb.Left, tb.Top, tb.Right, tb.Bottom)).Bounds;
        var visible = Rectangle.Intersect(screen, Rectangle.FromLTRB(tb.Left, tb.Top, tb.Right, tb.Bottom));
        return visible.Width < 8 || visible.Height < 8;
    }

    private Point ComputePosition(Native.RECT tb, Size size)
    {
        int s(int v) => (int)Math.Round(v * _dpi / 96.0);
        int y = tb.Top + (tb.Height - size.Height) / 2;

        int right;
        if (_settings.OffsetFromRight is int off)
        {
            right = tb.Right - off;
        }
        else
        {
            var tray = Native.FindWindowEx(_taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tray != IntPtr.Zero && Native.GetWindowRect(tray, out var tr) && tr.Width > s(40) && tr.Left > tb.Left)
                right = tr.Left - s(4);
            else
                right = tb.Right - s(260);
        }

        int x = Math.Clamp(right - size.Width, tb.Left, Math.Max(tb.Left, tb.Right - size.Width));
        return new Point(x, y);
    }

    private string Format(float? celsius)
    {
        if (celsius is not float c) return "--";
        var v = _settings.Fahrenheit ? c * 9 / 5 + 32 : c;
        return $"{Math.Round(v):0}°";
    }

    private void Render(Native.RECT tb)
    {
        float scale = _dpi / 96f;
        int pad = (int)(8 * scale);
        int gap = (int)(12 * scale);
        int labelGap = (int)(5 * scale);

        using var valueFont = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var labelFont = new Font("Segoe UI", 10f * scale, FontStyle.Regular, GraphicsUnit.Pixel);

        var fg = _lightTheme ? Color.FromArgb(255, 20, 20, 20) : Color.FromArgb(255, 255, 255, 255);
        var dim = _lightTheme ? Color.FromArgb(170, 20, 20, 20) : Color.FromArgb(170, 255, 255, 255);
        var warn = _lightTheme ? Color.FromArgb(255, 196, 43, 28) : Color.FromArgb(255, 255, 153, 164);

        var fmt = StringFormat.GenericTypographic;
        fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

        Size Measure(string text, Font f)
        {
            using var b = new Bitmap(1, 1);
            using var g = Graphics.FromImage(b);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var sz = g.MeasureString(text, f, int.MaxValue, fmt);
            return new Size((int)Math.Ceiling(sz.Width), (int)Math.Ceiling(sz.Height));
        }

        // Reserve width for "188°" so the overlay doesn't jitter as values change.
        int valueW = Measure("188°", valueFont).Width;
        var cpuLabel = Measure("CPU", labelFont);
        var gpuLabel = Measure("GPU", labelFont);
        int valueH = Measure("0°", valueFont).Height;

        int width = pad + cpuLabel.Width + labelGap + valueW + gap + gpuLabel.Width + labelGap + valueW + pad;
        int height = Math.Max(valueH + (int)(8 * scale), tb.Height - (int)(8 * scale));
        _size = new Size(width, height);
        _pos = ComputePosition(tb, _size);

        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            // Alpha 1 keeps the whole rect hit-testable for drag and right-click.
            var bg = _hover
                ? (_lightTheme ? Color.FromArgb(28, 0, 0, 0) : Color.FromArgb(24, 255, 255, 255))
                : Color.FromArgb(1, 0, 0, 0);
            using (var path = RoundedRect(new Rectangle(0, 0, width - 1, height - 1), (int)(4 * scale)))
            using (var brush = new SolidBrush(bg))
                g.FillPath(brush, path);

            int x = pad;
            void Draw(Size labelSize, string label, float? value)
            {
                using var dimBrush = new SolidBrush(dim);
                g.DrawString(label, labelFont, dimBrush, x, (height - labelSize.Height) / 2f + scale, fmt);
                x += labelSize.Width + labelGap;

                var hot = value is float c && c >= _settings.WarnCelsius;
                using var valBrush = new SolidBrush(hot ? warn : fg);
                g.DrawString(Format(value), valueFont, valBrush, x, (height - valueH) / 2f, fmt);
                x += valueW;
            }

            Draw(cpuLabel, "CPU", _reading.Cpu);
            x += gap;
            Draw(gpuLabel, "GPU", _reading.Gpu);
        }

        Apply(bmp, _pos);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private void Apply(Bitmap bmp, Point pos)
    {
        var screenDc = Native.GetDC(IntPtr.Zero);
        var memDc = Native.CreateCompatibleDC(screenDc);
        var hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
        var old = Native.SelectObject(memDc, hBitmap);
        try
        {
            var size = new Native.SIZE(bmp.Width, bmp.Height);
            var src = new Native.POINT(0, 0);
            var dst = new Native.POINT(pos.X, pos.Y);
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteObject(hBitmap);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static bool ReadLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        var light = ReadLightTheme();
        if (light == _lightTheme) return;
        _lightTheme = light;
        if (IsHandleCreated) BeginInvoke(() => UpdateOverlay(force: true));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        UpdateOverlay(force: false);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        UpdateOverlay(force: false);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStartCursorX = Cursor.Position.X;
        _dragStartWindowX = _pos.X;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var tb = _taskbarRect;
        int x = Math.Clamp(_dragStartWindowX + Cursor.Position.X - _dragStartCursorX, tb.Left, tb.Right - _size.Width);
        _settings.OffsetFromRight = tb.Right - (x + _size.Width);
        _pos = new Point(x, _pos.Y);
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, _pos.X, _pos.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            _menu.Show(Cursor.Position);
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        Capture = false;
        _settings.Save();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }
}
