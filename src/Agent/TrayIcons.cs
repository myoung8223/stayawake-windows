using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace StayAwake.Agent
{
    /// <summary>
    /// Draws the tray icon: a gray closed eye when off, a green badge with the minutes left when on
    /// (like the Chrome toolbar badge). Sized for the current DPI.
    /// </summary>
    static class TrayIcons
    {
        public static readonly Color On = Color.FromArgb(0x15, 0x80, 0x3d);
        static readonly Color Off = Color.FromArgb(0x6b, 0x72, 0x80);

        /// <summary>Returns an HICON the caller must free with DestroyIcon.</summary>
        public static IntPtr Create(int? minutesLeft, bool dark)
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                if (minutesLeft.HasValue) DrawBadge(g, size, minutesLeft.Value);
                else DrawClosedEye(g, size, dark ? Color.FromArgb(0xd1, 0xd5, 0xdb) : Off);
                return bmp.GetHicon();
            }
        }

        public static Icon AppIcon()
        {
            // Large open eye for the window and About box.
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                DrawOpenEye(g, 32, On);
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }

        static void DrawBadge(Graphics g, int size, int minutes)
        {
            string text = minutes < 100 ? minutes.ToString() : (minutes / 60) + "h";
            using (var b = new SolidBrush(On))
            using (var path = RoundedRect(new RectangleF(0, 0, size, size), size * 0.25f))
                g.FillPath(b, path);
            float fontPx = text.Length <= 1 ? size * 0.78f : (text.Length == 2 ? size * 0.66f : size * 0.5f);
            using (var f = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, f, Brushes.White, new RectangleF(-1, 0, size + 2, size), sf);
        }

        static void DrawClosedEye(Graphics g, int size, Color c)
        {
            float w = Math.Max(1.5f, size / 10f);
            using (var p = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float y = size * 0.42f, l = size * 0.12f, r = size * 0.88f;
                g.DrawArc(p, l, y - size * 0.35f, r - l, size * 0.7f, 20, 140);
                for (int i = 0; i < 3; i++)
                {
                    float x = size * (0.3f + 0.2f * i);
                    g.DrawLine(p, x, size * 0.72f, x - (i - 1) * size * 0.05f, size * 0.88f);
                }
            }
        }

        static void DrawOpenEye(Graphics g, int size, Color c)
        {
            using (var p = new Pen(c, size / 10f))
            using (var b = new SolidBrush(c))
            {
                var path = new GraphicsPath();
                path.AddArc(size * 0.06f, size * 0.25f, size * 0.88f, size * 0.9f, 200, 140);
                path.AddArc(size * 0.06f, size * -0.15f, size * 0.88f, size * 0.9f, 20, 140);
                path.CloseFigure();
                g.DrawPath(p, path);
                float r = size * 0.16f;
                g.FillEllipse(b, size / 2f - r, size / 2f - r, r * 2, r * 2);
            }
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
