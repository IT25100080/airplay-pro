using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AirPlayPro;

/// <summary>
/// Draws the tray icons at runtime (AirPlay-style screen + triangle) so the
/// project has no binary assets. The colour of the dot shows receiver state.
/// </summary>
internal static class TrayIcons
{
    private static readonly Dictionary<ReceiverState, Icon> Cache = new();

    public static Icon For(ReceiverState state)
    {
        if (!Cache.TryGetValue(state, out var icon))
        {
            icon = Draw(state switch
            {
                ReceiverState.Connected => Color.FromArgb(52, 199, 89),
                ReceiverState.Waiting => Color.FromArgb(10, 132, 255),
                ReceiverState.Starting => Color.FromArgb(255, 159, 10),
                ReceiverState.Error => Color.FromArgb(255, 69, 58),
                _ => Color.FromArgb(142, 142, 147),
            });
            Cache[state] = icon;
        }
        return icon;
    }

    private static Icon Draw(Color accent)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var pen = new Pen(Color.White, 2.5f) { LineJoin = LineJoin.Round };
            using var screen = RoundedRect(new RectangleF(3, 4, 26, 18), 3);
            g.DrawPath(pen, screen);

            using var fill = new SolidBrush(Color.White);
            g.FillPolygon(fill, new PointF[] { new(16, 16), new(25, 29), new(7, 29) });

            using var dot = new SolidBrush(accent);
            g.FillEllipse(dot, 20, 0, 12, 12);
        }

        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
