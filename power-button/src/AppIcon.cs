using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace Atlas8PowerButton
{
    internal static class AppIcon
    {
        public static readonly Color Accent = Color.FromArgb(0x2E, 0xD3, 0x6B);

        /// <summary>Tray icon: a power glyph in the taskbar's text color with a status dot; no dot means paused.</summary>
        public static Icon Tray(int size, Color? dot)
        {
            var color = dot == null
                ? Color.FromArgb(140, 140, 140)
                : TaskbarIsLight() ? Color.FromArgb(32, 32, 32) : Color.White;
            using (var bitmap = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    Draw(g, size, color, dot);
                }

                var handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    Native.DestroyIcon(handle);
                }
            }
        }

        public static void Draw(Graphics g, int size, Color glyph, Color? dot)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var stroke = Math.Max(1.5f, size / 8f);
            var inset = stroke / 2 + size * 0.08f;
            var ring = new RectangleF(inset, inset + size * 0.04f, size - 2 * inset, size - 2 * inset);
            using (var pen = new Pen(glyph, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, ring, 300, 300);
                var center = size / 2f;
                g.DrawLine(pen, center, inset - size * 0.02f, center, size * 0.46f);
            }

            if (dot.HasValue)
            {
                var d = size * 0.34f;
                using (var brush = new SolidBrush(dot.Value))
                {
                    g.FillEllipse(brush, size - d, size - d, d, d);
                }
            }
        }

        private static bool TaskbarIsLight()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
