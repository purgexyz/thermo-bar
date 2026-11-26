using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;

namespace TempBar;

public sealed class TrayContext : ApplicationContext
{
    private readonly Settings _settings = Settings.Load();
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly OverlayForm _overlay;
    private readonly System.Windows.Forms.Timer _layoutTimer = new() { Interval = 1000 };
    private readonly SynchronizationContext _ui;
    private readonly Thread _sensorThread;
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _stopping;

    private Reading _last;
    private string _trayIconKey = "";
    private Icon? _trayIcon;
    private bool _warnedNoCpu;

    private readonly ToolStripMenuItem _overlayItem;
    private readonly ToolStripMenuItem _unitItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _intervalItem;
    private readonly ToolStripMenuItem _pawnIoItem;

    public TrayContext()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _overlayItem = new ToolStripMenuItem("Show on taskbar", null, (_, _) => ToggleOverlay()) { Checked = _settings.ShowOverlay };
        _unitItem = new ToolStripMenuItem("Fahrenheit", null, (_, _) => ToggleUnit()) { Checked = _settings.Fahrenheit };
        _intervalItem = new ToolStripMenuItem("Update every");
        foreach (var ms in new[] { 1000, 2000, 5000, 10000 })
        {
            var item = new ToolStripMenuItem($"{ms / 1000} s", null, (_, _) => SetInterval(ms)) { Tag = ms, Checked = _settings.IntervalMs == ms };
            _intervalItem.DropDownItems.Add(item);
        }
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup());
        _pawnIoItem = new ToolStripMenuItem("Install PawnIO driver for CPU temp…", null,
            (_, _) => OpenUrl("https://pawnio.eu/")) { Visible = false };

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _overlayItem,
            _unitItem,
            _intervalItem,
            new ToolStripMenuItem("Reset position", null, (_, _) => ResetPosition()),
            new ToolStripSeparator(),
            _startupItem,
            _pawnIoItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()),
        });
        _menu.Opening += (_, _) => _startupItem.Checked = Startup.IsEnabled();

        _overlay = new OverlayForm(_settings, _menu);

        _tray.ContextMenuStrip = _menu;
        _tray.Text = "TempBar: starting…";
        UpdateTrayIcon();
        _tray.Visible = true;

        _layoutTimer.Tick += (_, _) => _overlay.UpdateOverlay(force: false);
        _layoutTimer.Start();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        _sensorThread = new Thread(SensorLoop) { IsBackground = true, Name = "Sensors", Priority = ThreadPriority.BelowNormal };
        _sensorThread.Start();
    }

    // LibreHardwareMonitor is not thread-safe, so every call into it stays on this one thread.
    private void SensorLoop()
    {
        using var reader = new SensorReader();
        try
        {
            reader.Open();
        }
        catch (Exception ex)
        {
            _ui.Post(_ => _tray.Text = Truncate($"TempBar: sensor init failed ({ex.Message})"), null);
        }

        while (!_stopping)
        {
            Reading r;
            try { r = reader.Read(); }
            catch { r = default; }

            _ui.Post(_ => OnReading(r), null);
            _wake.WaitOne(_settings.IntervalMs);
        }
    }

    private void OnReading(Reading r)
    {
        if (_stopping) return;
        _last = r;
        _overlay.SetReading(r);
        _tray.Text = Truncate(Describe(r, _settings));
        UpdateTrayIcon();

        if (r.Cpu == null && !_warnedNoCpu && !PawnIoInstalled())
        {
            _warnedNoCpu = true;
            _pawnIoItem.Visible = true;
            _tray.ShowBalloonTip(8000, "CPU temperature unavailable",
                "Install the PawnIO driver (winget install namazso.PawnIO), then restart TempBar.", ToolTipIcon.Info);
        }
    }

    private static bool PawnIoInstalled()
    {
        try { return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled; }
        catch { return true; }
    }

    public static string Describe(Reading r, Settings s)
    {
        string f(float? c) => c is float v
            ? (s.Fahrenheit ? $"{Math.Round(v * 9 / 5 + 32):0}°F" : $"{Math.Round(v):0}°C")
            : "n/a";
        return $"CPU {f(r.Cpu)}  ·  GPU {f(r.Gpu)}" +
               (r.CpuName != null ? $"\n{r.CpuName}" : "") +
               (r.GpuName != null ? $"\n{r.GpuName}" : "");
    }

    private static string Truncate(string s) => s.Length <= 127 ? s : s[..127];

    // With the overlay on, the tray icon is a static chip; with it off, the tray icon carries the CPU temp.
    private void UpdateTrayIcon()
    {
        string? text = null;
        if (!_settings.ShowOverlay)
        {
            var c = _last.Cpu ?? _last.Gpu;
            text = c is float v ? $"{Math.Round(_settings.Fahrenheit ? v * 9 / 5 + 32 : v):0}" : "--";
        }

        var key = text ?? "<chip>";
        if (key == _trayIconKey) return;
        _trayIconKey = key;

        var size = SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            float s = size / 16f;

            if (text == null)
            {
                using var pen = new Pen(Color.White, 1.2f * s);
                using var fill = new SolidBrush(Color.FromArgb(255, 0, 120, 212));
                var body = new RectangleF(3.5f * s, 3.5f * s, 9f * s, 9f * s);
                g.FillRectangle(fill, body);
                g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
                for (int i = 0; i < 3; i++)
                {
                    float p = (5.5f + i * 2.5f) * s;
                    g.DrawLine(pen, p, 1f * s, p, 3.5f * s);
                    g.DrawLine(pen, p, 12.5f * s, p, 15f * s);
                    g.DrawLine(pen, 1f * s, p, 3.5f * s, p);
                    g.DrawLine(pen, 12.5f * s, p, 15f * s, p);
                }
            }
            else
            {
                var hot = (_last.Cpu ?? _last.Gpu) is float c && c >= _settings.WarnCelsius;
                using var font = new Font("Segoe UI", (text.Length > 2 ? 8.5f : 11f) * s, FontStyle.Bold, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(hot ? Color.FromArgb(255, 255, 120, 100) : Color.White);
                using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, font, brush, new RectangleF(-2, 0, size + 4, size), fmt);
            }
        }

        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        Native.DestroyIcon(hIcon);

        _tray.Icon = icon;
        _trayIcon?.Dispose();
        _trayIcon = icon;
    }

    private void ToggleOverlay()
    {
        _settings.ShowOverlay = !_settings.ShowOverlay;
        _overlayItem.Checked = _settings.ShowOverlay;
        _settings.Save();
        _overlay.UpdateOverlay(force: true);
        UpdateTrayIcon();
    }

    private void ToggleUnit()
    {
        _settings.Fahrenheit = !_settings.Fahrenheit;
        _unitItem.Checked = _settings.Fahrenheit;
        _settings.Save();
        _trayIconKey = "";
        OnReading(_last);
    }

    private void SetInterval(int ms)
    {
        _settings.IntervalMs = ms;
        foreach (ToolStripMenuItem item in _intervalItem.DropDownItems)
            item.Checked = (int)item.Tag! == ms;
        _settings.Save();
        _wake.Set();
    }

    private void ResetPosition()
    {
        _settings.OffsetFromRight = null;
        _settings.Save();
        _overlay.UpdateOverlay(force: true);
    }

    private void ToggleStartup()
    {
        var want = !Startup.IsEnabled();
        if (!Startup.SetEnabled(want))
            MessageBox.Show("Could not update the startup task.", "TempBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private void OnDisplayChanged(object? sender, EventArgs e) =>
        _ui.Post(_ => _overlay.UpdateOverlay(force: true), null);

    protected override void ExitThreadCore()
    {
        _stopping = true;
        _wake.Set();
        _sensorThread.Join(3000);
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _layoutTimer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _trayIcon?.Dispose();
        _overlay.Dispose();
        _menu.Dispose();
        base.ExitThreadCore();
    }
}
