namespace Syntra;

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(18, 18, 18);
    public static readonly Color Side = Color.FromArgb(14, 14, 14);
    public static readonly Color Card = Color.FromArgb(30, 30, 30);
    public static readonly Color Border = Color.FromArgb(55, 55, 55);
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
        p.Paint += (_, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1); };
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

    public static ComboBox Combo(IEnumerable<string> items, int width = 260)
    {
        var c = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Width = width,
            BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.Body, Margin = new Padding(4, 6, 4, 6),
        };
        c.Items.AddRange(items.Cast<object>().ToArray());
        return c;
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
