namespace Syntra;

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(0, 0, 0);
    public static readonly Color Side = Color.FromArgb(6, 6, 7);
    public static readonly Color Card = Color.FromArgb(13, 13, 14);
    public static readonly Color Hover = Color.FromArgb(26, 26, 28);
    public static readonly Color Border = Color.FromArgb(36, 36, 38);
    public static readonly Color Text = Color.FromArgb(240, 240, 240);
    public static readonly Color Muted = Color.FromArgb(150, 150, 150);
    public static readonly Color Good = Color.FromArgb(90, 200, 120);
    public static readonly Color Warn = Color.FromArgb(240, 120, 100);
    public static readonly Font H1 = new("Segoe UI Semibold", 20f);
    public static readonly Font H2 = new("Segoe UI Semibold", 11f);
    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Big = new("Segoe UI Semibold", 26f);
}

class DarkSlider : Control
{
    public double Min, Max, Value, Default, Step = 1;
    public double? Mark;
    public string Title;
    public Func<double, string> Format = v => v.ToString("0");
    public event Action<double>? Changed;
    public event Action? Released;
    bool drag;

    public DarkSlider(string title, double min, double max, double value, double def, int width = 400)
    {
        Title = title; Min = min; Max = max; Value = value; Default = def;
        Size = new Size(width, 54);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Theme.Card; ForeColor = Theme.Text;
        Margin = new Padding(4, 2, 24, 2);
        Cursor = Cursors.Hand;
    }

    Rectangle Track => new(10, 36, Width - 20, 4);

    public void SetValue(double v) { Value = Math.Clamp(v, Min, Max); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        TextRenderer.DrawText(g, Title, Theme.Body, new Point(6, 6), Theme.Text);
        var vs = Format(Value);
        var sz = TextRenderer.MeasureText(vs, Theme.H2);
        TextRenderer.DrawText(g, vs, Theme.H2, new Point(Width - sz.Width - 6, 4), Theme.Text);
        var t = Track;
        using var bb = new SolidBrush(Theme.Border);
        g.FillRectangle(bb, t);
        float x = (float)(t.Left + (Value - Min) / (Max - Min) * t.Width);
        using var wb = new SolidBrush(Color.White);
        g.FillRectangle(wb, t.Left, t.Top, Math.Max(0, x - t.Left), t.Height);
        if (Mark is double m)
        {
            float mx = (float)(t.Left + (m - Min) / (Max - Min) * t.Width);
            using var p = new Pen(Theme.Muted);
            g.DrawLine(p, mx, t.Top - 6, mx, t.Bottom + 6);
        }
        g.FillEllipse(wb, x - 7, t.Top - 5, 14, 14);
    }

    void FromX(int px)
    {
        var t = Track;
        double v = Min + (double)(px - t.Left) / t.Width * (Max - Min);
        v = Math.Round(Math.Clamp(v, Min, Max) / Step) * Step;
        v = Math.Clamp(v, Min, Max);
        if (Math.Abs(v - Value) > 1e-9) { Value = v; Invalidate(); Changed?.Invoke(v); }
    }

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { drag = true; Capture = true; FromX(e.X); } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (drag) FromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (drag) { drag = false; Capture = false; Released?.Invoke(); } base.OnMouseUp(e); }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { Value = Default; Invalidate(); Changed?.Invoke(Value); Released?.Invoke(); base.OnMouseDoubleClick(e); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var v = Math.Clamp(Value + Math.Sign(e.Delta) * Step, Min, Max);
        if (v != Value) { Value = v; Invalidate(); Changed?.Invoke(v); Released?.Invoke(); }
        base.OnMouseWheel(e);
    }
}

static class Ui
{
    public const int PageW = 880;

    public static Label Lbl(string text, Font? f = null, Color? c = null, int width = 0)
    {
        var l = new Label
        {
            Text = text, Font = f ?? Theme.Body, ForeColor = c ?? Theme.Text, BackColor = Color.Transparent,
            Margin = new Padding(4, 4, 4, 4),
        };
        if (width > 0) { l.AutoSize = false; l.Width = width; l.Height = TextRenderer.MeasureText(text, l.Font, new Size(width, 0), TextFormatFlags.WordBreak).Height + 4; }
        else l.AutoSize = true;
        return l;
    }

    public static Button Btn(string text, Action onClick, bool primary = false)
    {
        var b = new Button
        {
            Text = text, FlatStyle = FlatStyle.Flat, AutoSize = true, Padding = new Padding(10, 3, 10, 3),
            BackColor = primary ? Color.White : Theme.Card, ForeColor = primary ? Color.Black : Theme.Text,
            Font = Theme.Body, Cursor = Cursors.Hand, Margin = new Padding(4),
        };
        b.FlatAppearance.BorderColor = primary ? Color.White : Theme.Border;
        b.Click += (_, _) => onClick();
        return b;
    }

    public static CheckBox Toggle(string text, bool value, Action<bool> onChange)
    {
        var c = new CheckBox
        {
            Text = text, Checked = value, ForeColor = Theme.Text, Font = Theme.Body, AutoSize = true,
            Margin = new Padding(6, 6, 16, 6), Cursor = Cursors.Hand,
        };
        c.CheckedChanged += (_, _) => onChange(c.Checked);
        return c;
    }

    public static FlowLayoutPanel Page()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(18, 14, 10, 14),
        };
    }

    public static FlowLayoutPanel Card(string? title = null, string? sub = null, int width = PageW)
    {
        var p = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0),
            BackColor = Theme.Card, Padding = new Padding(14, 12, 14, 12), Margin = new Padding(0, 0, 0, 12),
        };
        p.Resize += (_, _) => { using var path = Gfx.Round(new Rectangle(0, 0, p.Width, p.Height), 12); p.Region = new Region(path); };
        p.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.Border);
            using var path = Gfx.Round(new Rectangle(0, 0, p.Width - 1, p.Height - 1), 12);
            e.Graphics.DrawPath(pen, path);
        };
        if (title != null) p.Controls.Add(Lbl(title, Theme.H2, width: width - 40));
        if (sub != null) p.Controls.Add(Lbl(sub, Theme.Body, Theme.Muted, width - 40));
        return p;
    }

    public static Control Header(string title, string sub, params Control[] right)
    {
        var card = Card(null, null);
        var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = Theme.Card, WrapContents = false };
        left.Controls.Add(Lbl(title, Theme.H1));
        left.Controls.Add(Lbl(sub, Theme.Body, Theme.Muted));
        card.Controls.Add(left);
        if (right.Length > 0)
        {
            var r = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = new Padding(20, 18, 0, 0), WrapContents = false };
            r.Controls.AddRange(right);
            card.Controls.Add(r);
        }
        return card;
    }

    public static DarkDropdown Combo(IEnumerable<string> items, int width = 260)
    {
        var d = new DarkDropdown { Width = width };
        d.Items.AddRange(items);
        return d;
    }

    public static void DarkTitle(Form f)
    {
        f.HandleCreated += (_, _) =>
        {
            try
            {
                int on = 1, black = 0x000000, white = 0xFFFFFF;
                Gfx.DwmSetWindowAttribute(f.Handle, 20, ref on, 4);    // dark mode title bar
                Gfx.DwmSetWindowAttribute(f.Handle, 35, ref black, 4); // caption colour (Windows 11)
                Gfx.DwmSetWindowAttribute(f.Handle, 34, ref black, 4); // border colour
                Gfx.DwmSetWindowAttribute(f.Handle, 36, ref white, 4); // caption text
            }
            catch { }
        };
    }
}

// A click-through, always-on-top crosshair window. It is a separate window drawn over your screen;
// it does not hook, inject into or read the game.
class CrosshairOverlay : Form
{
    readonly CrosshairSettings s;
    readonly System.Windows.Forms.Timer keepOnTop = new() { Interval = 2000 };
    const int Box = 160;

    public CrosshairOverlay(CrosshairSettings settings)
    {
        s = settings;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(255, 1, 254);
        TransparencyKey = BackColor;
        Size = new Size(Box, Box);
        DoubleBuffered = true;
        keepOnTop.Tick += (_, _) => { Recenter(); Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); };
        keepOnTop.Start();
        Recenter();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000; // LAYERED | TRANSPARENT (click-through) | TOOLWINDOW | NOACTIVATE
            return cp;
        }
    }

    public void Recenter()
    {
        var b = Screen.PrimaryScreen!.Bounds;
        var p = new Point(b.Left + b.Width / 2 - Box / 2, b.Top + b.Height / 2 - Box / 2);
        if (Location != p) Location = p;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None; // no fringes against the transparency key
        int c = Box / 2, t = Math.Max(1, s.Thickness), len = s.Size, gap = s.Gap;
        var col = Color.FromArgb(s.Argb);
        var rects = new List<Rectangle>();
        int half = t / 2;
        switch (s.Style)
        {
            case "Dot":
                rects.Add(new Rectangle(c - t - 1, c - t - 1, t * 2 + 2, t * 2 + 2));
                break;
            case "Circle":
                using (var op = new Pen(Color.Black, t + 2)) if (s.Outline) g.DrawEllipse(op, c - len, c - len, len * 2, len * 2);
                using (var p = new Pen(col, t)) g.DrawEllipse(p, c - len, c - len, len * 2, len * 2);
                break;
            case "T":
                rects.Add(new Rectangle(c - half, c + gap, t, len));
                rects.Add(new Rectangle(c - gap - len, c - half, len, t));
                rects.Add(new Rectangle(c + gap, c - half, len, t));
                break;
            default:
                rects.Add(new Rectangle(c - half, c - gap - len, t, len));
                rects.Add(new Rectangle(c - half, c + gap, t, len));
                rects.Add(new Rectangle(c - gap - len, c - half, len, t));
                rects.Add(new Rectangle(c + gap, c - half, len, t));
                break;
        }
        if (s.CenterDot && s.Style != "Dot") rects.Add(new Rectangle(c - half, c - half, t, t));
        if (s.Outline)
        {
            using var ob = new SolidBrush(Color.Black);
            foreach (var r in rects) g.FillRectangle(ob, Rectangle.Inflate(r, 1, 1));
        }
        using var br = new SolidBrush(col);
        foreach (var r in rects) g.FillRectangle(br, r);
    }

    protected override void Dispose(bool disposing) { if (disposing) keepOnTop.Dispose(); base.Dispose(disposing); }
}

static class Gfx
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static System.Drawing.Drawing2D.GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// Clean dark dropdown that replaces the old Windows combo box.
class DarkDropdown : Control
{
    public readonly List<string> Items = new();
    int index = -1;
    bool hover;
    public string Placeholder = "Choose…";
    public event Action? SelectedChanged;

    public DarkDropdown()
    {
        Size = new Size(260, 38);
        Margin = new Padding(4, 6, 8, 6);
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Cursor = Cursors.Hand;
        Font = Theme.Body;
    }

    public int SelectedIndex
    {
        get => index;
        set { index = value; Invalidate(); }
    }

    public string? SelectedItem
    {
        get => index >= 0 && index < Items.Count ? Items[index] : null;
        set { index = value == null ? -1 : Items.IndexOf(value); Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 9);
        using var fill = new SolidBrush(hover ? Theme.Hover : Color.FromArgb(8, 8, 9));
        using var pen = new Pen(hover ? Color.FromArgb(70, 70, 74) : Theme.Border);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
        var text = SelectedItem ?? Placeholder;
        TextRenderer.DrawText(g, text, Font, new Rectangle(12, 0, Width - 40, Height),
            SelectedItem == null ? Theme.Muted : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var cp = new Pen(Theme.Muted, 1.6f);
        float cx = Width - 20, cy = Height / 2f;
        g.DrawLines(cp, new[] { new PointF(cx - 5, cy - 2.5f), new PointF(cx, cy + 2.5f), new PointF(cx + 5, cy - 2.5f) });
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Items.Count == 0) return;
        var pop = new DropPopup(this);
        var pt = PointToScreen(new Point(0, Height + 4));
        pop.Location = pt;
        pop.Show(FindForm());
    }

    internal void Pick(int i) { index = i; Invalidate(); SelectedChanged?.Invoke(); }

    sealed class DropPopup : Form
    {
        readonly DarkDropdown owner;
        const int RowH = 34, MaxRows = 9;
        int hoverRow = -1, scroll;

        public DropPopup(DarkDropdown o)
        {
            owner = o;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(12, 12, 13);
            Opacity = 0.96;
            DoubleBuffered = true;
            Font = o.Font;
            int rows = Math.Min(MaxRows, o.Items.Count);
            Size = new Size(o.Width, rows * RowH + 12);
            using var path = Gfx.Round(new Rectangle(0, 0, Width, Height), 10);
            Region = new Region(path);
            if (o.index > MaxRows - 1) scroll = Math.Min(o.index - MaxRows / 2, o.Items.Count - MaxRows);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } // drop shadow
        }

        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }

        int RowAt(int y) { int r = (y - 6) / RowH; return r >= 0 && r < Math.Min(MaxRows, owner.Items.Count) ? r + scroll : -1; }

        protected override void OnMouseMove(MouseEventArgs e) { var r = RowAt(e.Y); if (r != hoverRow) { hoverRow = r; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverRow = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { var r = RowAt(e.Y); if (r >= 0) { owner.Pick(r); Close(); } base.OnMouseClick(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int max = Math.Max(0, owner.Items.Count - MaxRows);
            scroll = Math.Clamp(scroll - Math.Sign(e.Delta), 0, max);
            hoverRow = RowAt(e.Y); Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Border))
            using (var path = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10))
                g.DrawPath(pen, path);
            int rows = Math.Min(MaxRows, owner.Items.Count);
            for (int i = 0; i < rows; i++)
            {
                int idx = i + scroll;
                var rect = new Rectangle(6, 6 + i * RowH, Width - 12, RowH);
                bool sel = idx == owner.index, hov = idx == hoverRow;
                if (sel || hov)
                {
                    using var b = new SolidBrush(sel ? Color.FromArgb(34, 34, 37) : Theme.Hover);
                    using var rp = Gfx.Round(new RectangleF(rect.X, rect.Y + 1, rect.Width, rect.Height - 2), 7);
                    g.FillPath(b, rp);
                }
                TextRenderer.DrawText(g, owner.Items[idx], Font, new Rectangle(rect.X + 10, rect.Y, rect.Width - 30, rect.Height),
                    sel ? Color.White : Color.FromArgb(205, 205, 210), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (sel)
                {
                    using var cp = new Pen(Color.White, 1.6f);
                    float cx = rect.Right - 16, cy = rect.Y + rect.Height / 2f;
                    g.DrawLines(cp, new[] { new PointF(cx - 5, cy), new PointF(cx - 1.5f, cy + 3.5f), new PointF(cx + 5, cy - 4) });
                }
            }
            if (owner.Items.Count > MaxRows)
            {
                float track = Height - 16, thumb = track * MaxRows / owner.Items.Count;
                float y = 8 + (track - thumb) * scroll / (owner.Items.Count - MaxRows);
                using var sb = new SolidBrush(Color.FromArgb(60, 60, 64));
                using var sp = Gfx.Round(new RectangleF(Width - 6, y, 3, thumb), 1.5f);
                g.FillPath(sb, sp);
            }
        }
    }
}

public enum IconKind { Display, Resolution, Monitor, Crosshair, Profiles, Settings, Exit }

// Icon-only sidebar button. The selected one is a white rounded tile with a black icon.
class NavIcon : Control
{
    public readonly IconKind Kind;
    bool selected, hover;
    public bool Selected { get => selected; set { selected = value; Invalidate(); } }

    public NavIcon(IconKind kind)
    {
        Kind = kind;
        Size = new Size(48, 48);
        Margin = new Padding(8, 5, 8, 5);
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Theme.Side;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (selected || hover)
        {
            using var b = new SolidBrush(selected ? Color.White : Theme.Hover);
            using var path = Gfx.Round(new RectangleF(0, 0, Width - 1, Height - 1), 12);
            g.FillPath(b, path);
        }
        var col = selected ? Color.Black : hover ? Color.White : Color.FromArgb(190, 190, 195);
        Draw(g, Kind, col, Width / 2f, Height / 2f);
    }

    public static void Draw(Graphics g, IconKind k, Color col, float cx, float cy)
    {
        using var p = new Pen(col, 1.8f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        using var b = new SolidBrush(col);
        switch (k)
        {
            case IconKind.Display: // half-filled circle
                g.DrawEllipse(p, cx - 8, cy - 8, 16, 16);
                g.FillPie(b, cx - 8, cy - 8, 16, 16, 90, 180);
                break;
            case IconKind.Resolution: // screen with expand corners
            {
                using var r = Gfx.Round(new RectangleF(cx - 11, cy - 8, 22, 16), 3);
                g.DrawPath(p, r);
                g.DrawLines(p, new[] { new PointF(cx - 6, cy - 1), new PointF(cx - 6, cy - 4), new PointF(cx - 3, cy - 4) });
                g.DrawLines(p, new[] { new PointF(cx + 6, cy + 1), new PointF(cx + 6, cy + 4), new PointF(cx + 3, cy + 4) });
                break;
            }
            case IconKind.Monitor: // monitor on a stand
            {
                using var r = Gfx.Round(new RectangleF(cx - 11, cy - 9, 22, 15), 3);
                g.DrawPath(p, r);
                g.DrawLine(p, cx, cy + 6, cx, cy + 9);
                g.DrawLine(p, cx - 5, cy + 9.5f, cx + 5, cy + 9.5f);
                break;
            }
            case IconKind.Crosshair:
                g.DrawEllipse(p, cx - 7.5f, cy - 7.5f, 15, 15);
                g.DrawLine(p, cx, cy - 11, cx, cy - 4);
                g.DrawLine(p, cx, cy + 4, cx, cy + 11);
                g.DrawLine(p, cx - 11, cy, cx - 4, cy);
                g.DrawLine(p, cx + 4, cy, cx + 11, cy);
                g.FillEllipse(b, cx - 1.3f, cy - 1.3f, 2.6f, 2.6f);
                break;
            case IconKind.Profiles: // game controller
            {
                using var r = Gfx.Round(new RectangleF(cx - 11, cy - 6, 22, 12), 6);
                g.DrawPath(p, r);
                g.DrawLine(p, cx - 7, cy, cx - 3, cy);
                g.DrawLine(p, cx - 5, cy - 2, cx - 5, cy + 2);
                g.FillEllipse(b, cx + 3, cy - 2.2f, 2.6f, 2.6f);
                g.FillEllipse(b, cx + 6, cy - 0.4f, 2.6f, 2.6f);
                break;
            }
            case IconKind.Settings: // sun / gear
                g.DrawEllipse(p, cx - 4, cy - 4, 8, 8);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(p, cx + (float)Math.Cos(a) * 7, cy + (float)Math.Sin(a) * 7, cx + (float)Math.Cos(a) * 10, cy + (float)Math.Sin(a) * 10);
                }
                break;
            case IconKind.Exit: // leave
                g.DrawLines(p, new[] { new PointF(cx - 2, cy - 9), new PointF(cx - 9, cy - 9), new PointF(cx - 9, cy + 9), new PointF(cx - 2, cy + 9) });
                g.DrawLine(p, cx - 3, cy, cx + 10, cy);
                g.DrawLines(p, new[] { new PointF(cx + 6, cy - 4), new PointF(cx + 10, cy), new PointF(cx + 6, cy + 4) });
                break;
        }
    }
}

// The logo tile at the top of the sidebar.
class LogoTile : Control
{
    public LogoTile()
    {
        Size = new Size(40, 40);
        Margin = new Padding(12, 14, 12, 18);
        DoubleBuffered = true;
        BackColor = Theme.Side;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var b = new SolidBrush(Color.White);
        using var path = Gfx.Round(new RectangleF(0, 0, Width - 1, Height - 1), 10);
        g.FillPath(b, path);
        using var f = new Font("Segoe UI Black", 17f, FontStyle.Bold);
        using var tb = new SolidBrush(Color.Black);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("S", f, tb, new RectangleF(0, 1, Width, Height), sf);
    }
}
