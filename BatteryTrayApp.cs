using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BatteryMeter;

public sealed class BatteryTrayApp : ApplicationContext
{
    private const string AppName = "BatteryMeter";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private readonly NotifyIcon _notifyIcon;
    private readonly BatteryPoller _poller;
    // private readonly TaskbarPanel _panel;
    private readonly SynchronizationContext _syncContext;
    private Icon? _currentIcon;

    public BatteryTrayApp()
    {
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();

        _poller = new BatteryPoller(TimeSpan.FromMilliseconds(500));
        _poller.ReadingUpdated += reading => _syncContext.Post(_ => OnReadingUpdated(reading), null);

        // First poll synchronously so the icon is correct before we show it
        var firstReading = _poller.QueryBattery();
        if (firstReading != null)
            OnReadingUpdated(firstReading);

        // _panel = new TaskbarPanel();
        // _panel.Show();

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "BatteryMeter",
            ContextMenuStrip = BuildContextMenu(),
            Icon = _currentIcon ?? TrayIconRenderer.Render(0, BatteryState.Idle)
        };

        // Start continuous polling now that the UI is set up
        _poller.Start();
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var refreshItem = new ToolStripMenuItem("Refresh", null, (_, _) => _poller.Refresh());
        var copyItem = new ToolStripMenuItem("Copy stats", null, (_, _) => CopyStats());
        var startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup());
        startupItem.Checked = IsStartupEnabled();
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());

        menu.Items.Add(refreshItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private BatteryReading? _lastReading;
    private string? _lastIconText;
    private BatteryState? _lastIconState;

    // History for sparkline: sample every ~10s, keep 30 points = ~5 min
    private readonly List<uint> _mWhHistory = new();
    private DateTime _lastHistorySample = DateTime.MinValue;
    private const int HistoryMax = 30;
    private static readonly TimeSpan HistoryInterval = TimeSpan.FromSeconds(10);

    private void OnReadingUpdated(BatteryReading reading)
    {
        _lastReading = reading;

        var state = GetState(reading);
        double watts = state switch
        {
            BatteryState.Charging => reading.ChargeRateWatts,
            BatteryState.Discharging => reading.DischargeRateWatts,
            _ => 0
        };

        string iconText = TrayIconRenderer.FormatWatts(watts, state);
        bool iconChanged = iconText != _lastIconText || state != _lastIconState;

        if (iconChanged)
        {
            var newIcon = TrayIconRenderer.Render(watts, state);
            var oldIcon = _currentIcon;
            _currentIcon = newIcon;
            _lastIconText = iconText;
            _lastIconState = state;

            try { if (_notifyIcon != null) _notifyIcon.Icon = newIcon; } catch { }

            if (oldIcon != null)
            {
                DestroyIcon(oldIcon.Handle);
                oldIcon.Dispose();
            }
        }

        // TaskbarPanel update disabled for now
        // string sign = state == BatteryState.Charging ? "+" : state == BatteryState.Discharging ? "-" : "";
        // string panelText = $"{sign}{watts:0.0}W";
        // if (reading.SystemPowerWatts > 0)
        //     panelText += $" \u2502 {reading.SystemPowerWatts:0.0}W sys";
        // var panelColor = state switch
        // {
        //     BatteryState.Charging => Color.LimeGreen,
        //     BatteryState.Discharging => Color.FromArgb(255, 180, 50),
        //     _ => Color.Silver
        // };
        // if (_panel.InvokeRequired)
        //     _panel.Invoke(() => _panel.UpdateDisplay(panelText, panelColor));
        // else
        //     _panel.UpdateDisplay(panelText, panelColor);

        // Sample history for sparkline
        var now = DateTime.Now;
        if (now - _lastHistorySample >= HistoryInterval)
        {
            _mWhHistory.Add(reading.RemainingCapacityMWh);
            if (_mWhHistory.Count > HistoryMax)
                _mWhHistory.RemoveAt(0);
            _lastHistorySample = now;
        }

        string percentStr = "";
        if (_poller.FullChargedCapacityMWh > 0)
        {
            double pct = (double)reading.RemainingCapacityMWh / _poller.FullChargedCapacityMWh * 100;
            percentStr = $" ({pct:0}%)";
        }

        // Tooltip: info line + sparkline
        string acLabel = reading.PowerOnline ? "AC" : "Battery";
        string sparkline = BuildSparkline();

        string info = state switch
        {
            BatteryState.Charging =>
                $"\u26a1 +{reading.ChargeRateWatts:0.0}W \u2502 {reading.VoltageV:0.0}V \u2502 {acLabel}",
            BatteryState.Discharging =>
                $"\ud83d\udd0b -{reading.DischargeRateWatts:0.0}W \u2502 {reading.VoltageV:0.0}V \u2502 {acLabel}",
            _ =>
                $"Idle \u2502 {reading.VoltageV:0.0}V \u2502 {acLabel}"
        };

        string tooltip = $"{info}\n{reading.RemainingCapacityMWh:N0} mWh{percentStr}\n{sparkline}";

        // NotifyIcon.Text max is 127 chars — trim sparkline to fit on one line
        if (tooltip.Length > 127)
        {
            int overhead = $"{info}\n{reading.RemainingCapacityMWh:N0} mWh{percentStr}\n".Length;
            int maxSparkLen = 127 - overhead;
            sparkline = maxSparkLen > 0 && sparkline.Length > maxSparkLen
                ? sparkline[^maxSparkLen..]
                : maxSparkLen <= 0 ? "" : sparkline;
            tooltip = $"{info}\n{reading.RemainingCapacityMWh:N0} mWh{percentStr}\n{sparkline}";
        }

        try
        {
            if (_notifyIcon != null)
                _notifyIcon.Text = tooltip;
        }
        catch
        {
            // Can fail if the app is shutting down
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static readonly char[] SparkChars = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

    private string BuildSparkline()
    {
        if (_mWhHistory.Count < 2) return "";
        uint min = _mWhHistory.Min();
        uint max = _mWhHistory.Max();
        if (max == min) return new string(SparkChars[3], _mWhHistory.Count);

        var sb = new System.Text.StringBuilder(_mWhHistory.Count);
        foreach (var v in _mWhHistory)
        {
            int idx = (int)((v - min) * (SparkChars.Length - 1) / (double)(max - min));
            sb.Append(SparkChars[Math.Clamp(idx, 0, SparkChars.Length - 1)]);
        }
        return sb.ToString();
    }

    private static BatteryState GetState(BatteryReading reading)
    {
        if (reading.IsCharging && reading.ChargeRateWatts > 0)
            return BatteryState.Charging;
        if (!reading.IsCharging && reading.DischargeRateWatts > 0)
            return BatteryState.Discharging;
        return BatteryState.Idle;
    }

    private void CopyStats()
    {
        if (_lastReading is not { } r) return;

        string percentStr = "";
        if (_poller.FullChargedCapacityMWh > 0)
        {
            double pct = (double)r.RemainingCapacityMWh / _poller.FullChargedCapacityMWh * 100;
            percentStr = $" ({pct:0}%)";
        }

        string text =
            $"Charge:    {r.ChargeRateWatts:0.0}W (into battery)\n" +
            $"Discharge: {r.DischargeRateWatts:0.0}W (from battery)\n" +
            $"Voltage:   {r.VoltageV:0.0}V\n" +
            $"AC online: {(r.PowerOnline ? "Yes" : "No")}\n" +
            $"State:     {(r.IsCharging ? "Charging" : "Discharging")}\n" +
            $"Remaining: {r.RemainingCapacityMWh:N0} mWh{percentStr}";

        Clipboard.SetText(text);
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(AppName) != null;
    }

    private static void ToggleStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;

        if (key.GetValue(AppName) != null)
        {
            key.DeleteValue(AppName);
        }
        else
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(AppName, $"\"{exePath}\"");
        }
    }

    private void ExitApp()
    {
        _poller.Dispose();
        // _panel.Close();
        // _panel.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
        Application.Exit();
    }
}
