using System;
using System.Drawing;
using System.Windows.Forms;

namespace Atlas8PowerButton
{
    /// <summary>Shared look for the settings and setup windows: large type and touch-sized controls.</summary>
    internal static class Ui
    {
        public static readonly Color Green = Color.FromArgb(0x1E, 0x9E, 0x4F);
        public static readonly Color Orange = Color.FromArgb(0xD9, 0x7A, 0x00);
        public static readonly Color Red = Color.FromArgb(0xC4, 0x2B, 0x1C);
        public static readonly Color Grey = Color.FromArgb(0x70, 0x70, 0x70);
        public static readonly Color Muted = Color.FromArgb(0x5A, 0x5A, 0x5A);

        public const int ContentWidth = 560;

        /// <summary>Scales a 96-DPI pixel value to the window's DPI.</summary>
        public static int S(Control control, int pixels)
        {
            return (int)Math.Round(pixels * control.DeviceDpi / 96.0);
        }

        public static void SetUpForm(Form form, string title)
        {
            form.Text = title;
            form.AutoScaleMode = AutoScaleMode.None;
            form.Font = new Font("Segoe UI", 11f);
            form.BackColor = SystemColors.Window;
            form.FormBorderStyle = FormBorderStyle.FixedSingle;
            form.MaximizeBox = false;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.AutoSize = true;
            form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            form.Padding = new Padding(S(form, 28), S(form, 22), S(form, 28), S(form, 22));
            form.KeyPreview = true;
            try
            {
                form.Icon = Icon.ExtractAssociatedIcon(Installer.SelfPath);
            }
            catch (ArgumentException)
            {
            }
        }

        public static TableLayoutPanel Column(Control owner)
        {
            var column = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty
            };
            column.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return column;
        }

        public static Control Header(Control owner, string title)
        {
            var size = S(owner, 44);
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, S(owner, 12))
            };
            var glyph = new Bitmap(size, size);
            using (var g = Graphics.FromImage(glyph))
            {
                AppIcon.Draw(g, size, Color.FromArgb(32, 32, 32), AppIcon.Accent);
            }

            row.Controls.Add(new PictureBox
            {
                Image = glyph,
                Size = new Size(size, size),
                Margin = new Padding(0, 0, S(owner, 12), 0)
            });
            row.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 17f),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, S(owner, 4), 0, 0)
            });
            return row;
        }

        public static Label Text(Control owner, string text, float size = 11f, Color? color = null, int below = 12)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(S(owner, ContentWidth), 0),
                Font = new Font("Segoe UI", size),
                ForeColor = color ?? SystemColors.ControlText,
                Margin = new Padding(0, 0, 0, S(owner, below))
            };
        }

        public static Button Button(Control owner, string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(S(owner, 140), S(owner, 46)),
                Padding = new Padding(S(owner, 12), 0, S(owner, 12), 0),
                Margin = new Padding(S(owner, 10), 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Font = new Font("Segoe UI Semibold", 11f),
                Cursor = Cursors.Hand
            };
            if (primary)
            {
                button.BackColor = Green;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderSize = 0;
            }
            else
            {
                button.BackColor = Color.FromArgb(0xEE, 0xEE, 0xEE);
                button.FlatAppearance.BorderColor = Color.FromArgb(0xC8, 0xC8, 0xC8);
            }

            return button;
        }

        public static CheckBox Check(Control owner, string text)
        {
            return new CheckBox
            {
                Text = text,
                AutoSize = true,
                Padding = new Padding(0, S(owner, 4), 0, S(owner, 4)),
                Margin = new Padding(0, 0, 0, S(owner, 6)),
                Cursor = Cursors.Hand
            };
        }

        /// <summary>A colored dot followed by a sentence, for "what is happening right now".</summary>
        public sealed class StatusLine : FlowLayoutPanel
        {
            private readonly Panel _dot;
            private readonly Label _text;

            public StatusLine(Control owner)
            {
                AutoSize = true;
                WrapContents = false;
                BackColor = Color.FromArgb(0xF3, 0xF3, 0xF3);
                Padding = new Padding(S(owner, 12), S(owner, 10), S(owner, 12), S(owner, 10));
                Margin = new Padding(0, 0, 0, S(owner, 16));
                MinimumSize = new Size(S(owner, ContentWidth), 0);
                var dot = S(owner, 14);
                _dot = new Panel { Size = new Size(dot, dot), Margin = new Padding(0, S(owner, 5), S(owner, 10), 0) };
                _dot.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var brush = new SolidBrush(_dot.ForeColor))
                    {
                        e.Graphics.FillEllipse(brush, 0, 0, _dot.Width - 1, _dot.Height - 1);
                    }
                };
                _text = new Label
                {
                    AutoSize = true,
                    MaximumSize = new Size(S(owner, ContentWidth - 60), 0),
                    Font = new Font("Segoe UI Semibold", 11f),
                    Margin = Padding.Empty
                };
                Controls.Add(_dot);
                Controls.Add(_text);
            }

            public void Show(Color color, string text)
            {
                if (_dot.ForeColor != color)
                {
                    _dot.ForeColor = color;
                    _dot.Invalidate();
                }

                if (_text.Text != text)
                {
                    _text.Text = text;
                }
            }
        }

        /// <summary>A drop-down entry with its own text.</summary>
        public sealed class Choice<T>
        {
            public Choice(T value, string text)
            {
                Value = value;
                Text = text;
            }

            public T Value { get; }
            public string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
