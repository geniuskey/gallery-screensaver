using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GallerySaver;

/// <summary>Draws the Gallery preview in the window owned by Windows.</summary>
internal sealed class Preview : Control
{
    private readonly IntPtr parent;

    private Preview(IntPtr parent)
    {
        this.parent = parent;
        BackColor = Color.Black;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    public static void Run(IntPtr parent)
    {
        using var preview = new Preview(parent);
        using var context = new ApplicationContext();
        using var watch = new Timer { Interval = 250 };

        bool Resize()
        {
            if (!GetClientRect(parent, out var bounds)) return false;
            preview.Size = new Size(bounds.Right, bounds.Bottom);
            return true;
        }

        if (!Resize()) return;
        preview.CreateControl();
        watch.Tick += (_, _) =>
        {
            if (!Resize()) context.ExitThread();
        };
        watch.Start();
        Application.Run(context);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Parent = parent;
            parameters.Style = (parameters.Style & ~unchecked((int)0x80000000)) | 0x40000000; // WS_POPUP → WS_CHILD
            return parameters;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width == 0 || Height == 0) return;
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var stroke = new Pen(Color.Teal, Math.Max(1f, Width / 130f));
        var points = new PointF[65];
        for (var row = 0; row < 9; row++)
        {
            stroke.Color = Color.FromArgb(108 + row * 16, 87 + row * 9, 137 + row * 6, 151 + row * 5);
            for (var i = 0; i < points.Length; i++)
            {
                var t = (double)i / (points.Length - 1);
                var wave = Math.Sin(t * 8 + row * .5) * Math.Sin(t * Math.PI);
                points[i] = new PointF(
                    (float)((.1 + .8 * t) * Width),
                    (float)((.26 + row * .047 + wave * .13) * Height));
            }
            graphics.DrawLines(stroke, points);
        }

        TextRenderer.DrawText(graphics, "GALLERY", Font, new Rectangle(0, Height * 3 / 4, Width, Height / 4),
            Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);
}
