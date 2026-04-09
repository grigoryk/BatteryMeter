using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace BatteryMeter;

public static class TrayIconRenderer
{
    private const int RenderSize = 256; // large internal canvas for crisp text

    public static Icon Render(double watts, BatteryState state)
    {
        var iconSize = SystemInformation.SmallIconSize;
        int iconW = Math.Max(iconSize.Width, 20);
        int iconH = Math.Max(iconSize.Height, 20);

        var color = state switch
        {
            BatteryState.Charging => Color.LimeGreen,
            BatteryState.Discharging => Color.FromArgb(255, 180, 50),
            _ => Color.Silver
        };

        string text = FormatWatts(watts, state);

        using var large = new Bitmap(RenderSize, RenderSize);
        using (var g = Graphics.FromImage(large))
        {
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            // Binary search for the largest font that fits the canvas
            float fontSize = FindMaxFontSize(g, text, RenderSize, RenderSize);
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);

            var size = g.MeasureString(text, font);
            float x = (RenderSize - size.Width) / 2f;
            float y = (RenderSize - size.Height) / 2f;

            // Dark outline for readability
            using var outlineBrush = new SolidBrush(Color.FromArgb(20, 20, 20));
            float ow = fontSize * 0.06f;
            for (float dx = -ow; dx <= ow; dx += ow)
                for (float dy = -ow; dy <= ow; dy += ow)
                    if (dx != 0 || dy != 0)
                        g.DrawString(text, font, outlineBrush, x + dx, y + dy);

            g.DrawString(text, font, brush, x, y);
        }

        // Downscale to icon size
        using var final = new Bitmap(iconW, iconH);
        using (var g = Graphics.FromImage(final))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(large, 0, 0, iconW, iconH);
        }

        return Icon.FromHandle(final.GetHicon());
    }

    private static float FindMaxFontSize(Graphics g, string text, int maxW, int maxH)
    {
        float lo = 10f, hi = maxH * 1.2f;
        while (hi - lo > 1f)
        {
            float mid = (lo + hi) / 2f;
            using var f = new Font("Segoe UI", mid, FontStyle.Bold, GraphicsUnit.Pixel);
            var s = g.MeasureString(text, f);
            if (s.Width <= maxW && s.Height <= maxH)
                lo = mid;
            else
                hi = mid;
        }
        return lo;
    }

    public static string FormatWatts(double watts, BatteryState state = BatteryState.Idle)
    {
        return watts < 10 ? watts.ToString("0.0") : watts.ToString("0");
    }
}

public enum BatteryState
{
    Charging,
    Discharging,
    Idle
}
