using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZETXBoost
{
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(9, 12, 17);
        public static readonly Color Panel = Color.FromArgb(15, 20, 28);
        public static readonly Color PanelAlt = Color.FromArgb(19, 25, 35);
        public static readonly Color Panel2 = Color.FromArgb(24, 31, 43);
        public static readonly Color Border = Color.FromArgb(38, 52, 70);
        public static readonly Color Select = Color.FromArgb(0, 70, 90);
        public static readonly Color Text = Color.FromArgb(226, 233, 242);
        public static readonly Color Muted = Color.FromArgb(128, 144, 166);
        public static readonly Color Accent = Color.FromArgb(0, 229, 255);
        public static readonly Color Accent2 = Color.FromArgb(255, 140, 0);
        public static readonly Color Green = Color.FromArgb(60, 255, 130);
        public static readonly Color Red = Color.FromArgb(255, 80, 80);

        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 13f);
        public static readonly Font H1 = new Font("Segoe UI Black", 22f);
        public static readonly Font StatValue = new Font("Segoe UI Semibold", 15f);
        public static readonly Font Mono = new Font("Consolas", 9f);

        public static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(b.R + (a.R - b.R) * t),
                (int)(b.G + (a.G - b.G) * t),
                (int)(b.B + (a.B - b.B) * t));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void DarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch { }
        }
    }

    internal sealed class HudButton : Button
    {
        private readonly Color _accent;

        public HudButton(string text, Color accent)
        {
            _accent = accent;
            Text = text;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderColor = accent;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.MouseOverBackColor = Theme.Blend(accent, Theme.Panel2, 0.18);
            FlatAppearance.MouseDownBackColor = Theme.Blend(accent, Theme.Panel2, 0.35);
            BackColor = Theme.Panel2;
            ForeColor = accent;
            Font = Theme.Bold;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12, 5, 12, 5);
            Margin = new Padding(0, 4, 8, 4);
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            ForeColor = Enabled ? _accent : Theme.Muted;
            FlatAppearance.BorderColor = Enabled ? _accent : Theme.Border;
        }
    }

    internal sealed class NavButton : Button
    {
        private bool _active;

        public NavButton(string text)
        {
            Text = "   " + text;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Theme.Panel2;
            FlatAppearance.MouseDownBackColor = Theme.Panel2;
            BackColor = Theme.Panel;
            ForeColor = Theme.Muted;
            Font = new Font("Segoe UI Semibold", 10.5f);
            TextAlign = ContentAlignment.MiddleLeft;
            Width = 200;
            Height = 44;
            Margin = new Padding(0, 0, 0, 2);
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
        }

        public bool Active
        {
            get => _active;
            set
            {
                _active = value;
                BackColor = value ? Theme.Panel2 : Theme.Panel;
                ForeColor = value ? Theme.Accent : Theme.Muted;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_active)
                using (var b = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(b, 0, 6, 4, Height - 12);
        }
    }

    internal sealed class StatCard : Panel
    {
        public readonly Label Value;
        private readonly Color _accent;

        public StatCard(string title, Color accent)
        {
            _accent = accent;
            BackColor = Theme.Panel;
            Padding = new Padding(14, 10, 10, 10);
            Margin = new Padding(0, 0, 10, 10);
            Height = 82;
            Dock = DockStyle.Fill;
            ResizeRedraw = true;
            DoubleBuffered = true;

            Value = new Label
            {
                Text = "-",
                Dock = DockStyle.Fill,
                ForeColor = accent,
                Font = Theme.StatValue,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var t = new Label
            {
                Text = title.ToUpperInvariant(),
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Theme.Muted,
                Font = new Font("Segoe UI Semibold", 8.25f)
            };
            Controls.Add(Value);
            Controls.Add(t);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(_accent))
                e.Graphics.FillRectangle(b, 0, 0, Width, 2);
        }
    }

    internal sealed class DarkListView : ListView
    {
        public DarkListView()
        {
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            OwnerDraw = true;
            DoubleBuffered = true;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            BorderStyle = BorderStyle.None;
            Font = Theme.Body;
            Dock = DockStyle.Fill;
            Margin = new Padding(0, 4, 0, 8);
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Panel2)) e.Graphics.FillRectangle(b, e.Bounds);
            var r = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.Bold, r, Theme.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            using (var p = new Pen(Theme.Border))
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e) { e.DrawDefault = false; }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            Color back = e.Item.Selected ? Theme.Select : (e.ItemIndex % 2 == 0 ? Theme.Panel : Theme.PanelAlt);
            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            Color fore = e.Item.UseItemStyleForSubItems ? e.Item.ForeColor : e.SubItem.ForeColor;
            var r = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, r, fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitLastColumn();
        }

        public void FitLastColumn()
        {
            if (Columns.Count == 0) return;
            int used = 0;
            for (int i = 0; i < Columns.Count - 1; i++) used += Columns[i].Width;
            int w = ClientSize.Width - used;
            if (w > 60) Columns[Columns.Count - 1].Width = w;
        }
    }
}
