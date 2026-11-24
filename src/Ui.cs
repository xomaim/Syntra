using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Syntra;

// ---------------- Theme ----------------
public class ThemePreset
{
    public string Name { get; set; } = "Custom";
    public string Bg { get; set; } = "#000000";
    public string Side { get; set; } = "#060607";
    public string Card { get; set; } = "#0D0D0E";
    public string Hover { get; set; } = "#1A1A1C";
    public string Border { get; set; } = "#242426";
    public string Accent { get; set; } = "#FFFFFF";
    public string AccentText { get; set; } = "#000000";
    public ThemePreset Clone() => (ThemePreset)MemberwiseClone();
}

static class Theme
{
    public static Color Bg, Side, Card, Hover, Border, Accent, AccentText;
    public static Color Text = Color.FromArgb(240, 240, 242);
    public static Color Muted = Color.FromArgb(145, 145, 150);
    public static readonly Color Good = Color.FromArgb(90, 210, 130);
    public static readonly Color Warn = Color.FromArgb(245, 120, 105);
    public static readonly Font H1 = new("Segoe UI Semibold", 20f);
    public static readonly Font H2 = new("Segoe UI Semibold", 11f);
    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Big = new("Segoe UI Semibold", 26f);
    public static ThemePreset Current = new();

    public static readonly List<ThemePreset> Presets = new()
    {
        new() { Name = "Midnight" },
        new() { Name = "Graphite", Bg = "#0E0E10", Side = "#0A0A0C", Card = "#17171A", Hover = "#232327", Border = "#2E2E33", Accent = "#E9E9EE", AccentText = "#000000" },
        new() { Name = "Ocean", Bg = "#02060C", Side = "#040912", Card = "#09121F", Hover = "#122039", Border = "#1B2B45", Accent = "#4EA8FF", AccentText = "#00101F" },
        new() { Name = "Violet", Bg = "#06030C", Side = "#08050F", Card = "#110B1D", Hover = "#1E1533", Border = "#2A1E45", Accent = "#B388FF", AccentText = "#12002E" },
        new() { Name = "Crimson", Bg = "#090303", Side = "#0C0505", Card = "#160A0B", Hover = "#2A1214", Border = "#3A1A1D", Accent = "#FF4D5E", AccentText = "#1F0004" },
        new() { Name = "Forest", Bg = "#020704", Side = "#040A06", Card = "#09150D", Hover = "#12261A", Border = "#1A3524", Accent = "#3DDC84", AccentText = "#00170A" },
        new() { Name = "Sakura", Bg = "#0A0508", Side = "#0D060A", Card = "#180B12", Hover = "#2A1420", Border = "#3B1D2D", Accent = "#FF8FC7", AccentText = "#2A0016" },
        new() { Name = "Gold", Bg = "#080603", Side = "#0B0804", Card = "#16110A", Hover = "#271E10", Border = "#3A2C16", Accent = "#FFC94D", AccentText = "#241700" },
    };

    static Theme() => Use(Presets[0]);

    static Color C(string hex, Color fallback)
    {
        try { return ColorTranslator.FromHtml(hex); } catch { return fallback; }
    }

    public static void Use(ThemePreset p)
    {
        Current = p.Clone();
        Bg = C(p.Bg, Color.Black); Side = C(p.Side, Color.Black); Card = C(p.Card, Color.FromArgb(13, 13, 14));
        Hover = C(p.Hover, Color.FromArgb(26, 26, 28)); Border = C(p.Border, Color.FromArgb(36, 36, 38));
        Accent = C(p.Accent, Color.White); AccentText = C(p.AccentText, Color.Black);
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    public static Color Mix(Color a, Color b, double t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}

static class Gfx
{
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        rad = Math.Max(0.5f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2f));
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void RoundCorners(IntPtr hwnd)
    {
        try { int round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, 4); } catch { }
    }
}

// ---------------- Controls ----------------
class DarkSlider : Control
{
    public double Min, Max, Value, Default, Step = 1;
    public double? Mark;
    public string Title;
    public Func<double, string> Format = v => v.ToString("0");
    public event Action<double>? Changed;
    public event Action? Released;
    bool drag, hover;

    public DarkSlider(string title, double min, double max, double value, double def, int width = 400)
    {
        Title = title; Min = min; Max = max; Value = value; Default = def;
        Size = new Size(width, 54);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Card; ForeColor = Theme.Text;
        Margin = new Padding(4, 2, 24, 2);
        Cursor = Cursors.Hand;
    }

    Rectangle Track => new(10, 37, Width - 20, 4);

    public void SetValue(double v) { Value = Math.Clamp(v, Min, Max); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        TextRenderer.DrawText(g, Title, Theme.Body, new Point(6, 6), Theme.Text);
        var vs = Format(Value);
        var sz = TextRenderer.MeasureText(vs, Theme.H2);
        TextRenderer.DrawText(g, vs, Theme.H2, new Point(Width - sz.Width - 6, 4), Theme.Text);
        var t = Track;
        using (var bb = new SolidBrush(Theme.Border)) using (var tp = Gfx.Round(t, 2)) g.FillPath(bb, tp);
        float x = (float)(t.Left + (Value - Min) / (Max - Min) * t.Width);
        using var ab = new SolidBrush(Theme.Accent);
        if (x - t.Left > 1) using (var fp = Gfx.Round(new RectangleF(t.Left, t.Top, x - t.Left, t.Height), 2)) g.FillPath(ab, fp);
        if (Mark is double m)
        {
            float mx = (float)(t.Left + (m - Min) / (Max - Min) * t.Width);
            using var p = new Pen(Theme.Muted);
            g.DrawLine(p, mx, t.Top - 6, mx, t.Bottom + 6);
        }
        float r = drag || hover ? 8 : 7;
        g.FillEllipse(ab, x - r, t.Top + 2 - r, r * 2, r * 2);
    }

    void FromX(int px)
    {
        var t = Track;
        double v = Min + (double)(px - t.Left) / t.Width * (Max - Min);
        v = Math.Clamp(Math.Round(Math.Clamp(v, Min, Max) / Step) * Step, Min, Max);
        if (Math.Abs(v - Value) > 1e-9) { Value = v; Invalidate(); Changed?.Invoke(v); }
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { drag = true; Capture = true; FromX(e.X); } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (drag) FromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (drag) { drag = false; Capture = false; Invalidate(); Released?.Invoke(); } base.OnMouseUp(e); }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { Value = Default; Invalidate(); Changed?.Invoke(Value); Released?.Invoke(); base.OnMouseDoubleClick(e); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var v = Math.Clamp(Value + Math.Sign(e.Delta) * Step, Min, Max);
        if (v != Value) { Value = v; Invalidate(); Changed?.Invoke(v); Released?.Invoke(); }
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }
}

// Pill-shaped button used everywhere.
class RoundButton : Button
{
    public bool Primary;
    bool hover, down;

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = Theme.Body;
        Padding = new Padding(12, 4, 12, 4);
        MinimumSize = new Size(0, 34);
        Margin = new Padding(4);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor is { A: 255 } pc ? pc : Theme.Card);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fill, border, text;
        if (Primary)
        {
            fill = down ? Theme.Mix(Theme.Accent, Color.Black, 0.2) : hover ? Theme.Mix(Theme.Accent, Color.Black, 0.1) : Theme.Accent;
            border = fill; text = Theme.AccentText;
        }
        else
        {
            fill = down ? Theme.Mix(Theme.Hover, Color.White, 0.08) : hover ? Theme.Hover : Theme.Mix(Theme.Card, Theme.Hover, 0.35);
            border = hover ? Theme.Mix(Theme.Border, Color.White, 0.15) : Theme.Border;
            text = Enabled ? Theme.Text : Theme.Muted;
        }
        using var path = Gfx.Round(r, 11);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var p = new Pen(border)) g.DrawPath(p, path);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

// Switch with label.
class ToggleSwitch : Control
{
    bool on;
    public event Action<bool>? Toggled;
    public bool Checked { get => on; set { on = value; Invalidate(); } }

    public ToggleSwitch(string text, bool value)
    {
        Text = text; on = value;
        Font = Theme.Body;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(54 + TextRenderer.MeasureText(text, Theme.Body).Width + 8, 32);
        Margin = new Padding(4, 4, 18, 4);
        Cursor = Cursors.Hand;
        BackColor = Theme.Card;
    }

    protected override void OnClick(EventArgs e) { if (!Enabled) return; on = !on; Invalidate(); Toggled?.Invoke(on); base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor is { A: 255 } pc ? pc : Theme.Card);
        var track = new RectangleF(2, 7, 40, 22);
        using (var path = Gfx.Round(track, 11))
        using (var b = new SolidBrush(on ? Theme.Accent : Theme.Border))
            g.FillPath(b, path);
        float kx = on ? track.Right - 19 : track.Left + 3;
        using (var kb = new SolidBrush(on ? Theme.AccentText : Color.FromArgb(200, 200, 205)))
            g.FillEllipse(kb, kx, track.Top + 3, 16, 16);
        TextRenderer.DrawText(g, Text, Font, new Point(52, 9), Enabled ? Theme.Text : Theme.Muted);
    }
}

// Text box in a rounded dark frame.
class DarkInput : Panel
{
    public readonly TextBox Box = new() { BorderStyle = BorderStyle.None };
    bool focused;

    public DarkInput(string placeholder = "", int width = 300)
    {
        Size = new Size(width, 42);
        Margin = new Padding(4, 4, 4, 8);
        Padding = new Padding(14, 12, 14, 8);
        DoubleBuffered = true;
        BackColor = Theme.Card;
        Box.BackColor = Theme.Mix(Theme.Bg, Theme.Card, 0.5);
        Box.ForeColor = Theme.Text;
        Box.Font = new Font("Segoe UI", 10.5f);
        Box.Dock = DockStyle.Fill;
        Box.PlaceholderText = placeholder;
        Box.GotFocus += (_, _) => { focused = true; Invalidate(); };
        Box.LostFocus += (_, _) => { focused = false; Invalidate(); };
        Controls.Add(Box);
        Click += (_, _) => Box.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor is { A: 255 } pc ? pc : Theme.Card);
        using var path = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 11);
        using (var b = new SolidBrush(Box.BackColor)) g.FillPath(b, path);
        using (var p = new Pen(focused ? Theme.Accent : Theme.Border, focused ? 1.4f : 1f)) g.DrawPath(p, path);
    }
}

// The background picture is drawn once at window size; every "back" surface (window, sidebar,
// page, sidebar icons) draws its own slice of it, so the picture runs behind everything except the panels.
static class Bg
{
    public static Image? Source;
    public static int Dim = 40;
    public static Form? Root;
    public const int SideTint = 70;   // sidebar is a touch darker so icons stay readable
    static Bitmap? cache;
    static Size cacheSize;
    static Image? cacheSrc;
    static int cacheDim = -1;
    static Color cacheBg;

    public static bool On => Source != null && Root != null;

    public static void ClearCache() { cache?.Dispose(); cache = null; }

    public static bool Paint(Graphics g, Control c, int tint = 0)
    {
        if (Source == null || Root == null || Root.IsDisposed) return false;
        var sz = Root.ClientSize;
        if (sz.Width < 1 || sz.Height < 1) return false;
        if (cache == null || cacheSize != sz || cacheSrc != Source || cacheDim != Dim || cacheBg != Theme.Bg)
        {
            cache?.Dispose();
            cache = new Bitmap(sz.Width, sz.Height);
            using var cg = Graphics.FromImage(cache);
            cg.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float scale = Math.Max((float)sz.Width / Source.Width, (float)sz.Height / Source.Height);
            float w = Source.Width * scale, h = Source.Height * scale;
            cg.DrawImage(Source, (sz.Width - w) / 2, (sz.Height - h) / 2, w, h);
            using var dim = new SolidBrush(Color.FromArgb((int)(Dim / 100.0 * 255), Theme.Bg));
            cg.FillRectangle(dim, 0, 0, sz.Width, sz.Height);
            cacheSize = sz; cacheSrc = Source; cacheDim = Dim; cacheBg = Theme.Bg;
        }
        Point off;
        try { off = Root.PointToClient(c.PointToScreen(Point.Empty)); } catch { return false; }
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;
        g.DrawImage(cache, new Rectangle(-off.X, -off.Y, cache.Width, cache.Height), 0, 0, cache.Width, cache.Height, GraphicsUnit.Pixel);
        g.Restore(state);
        if (tint > 0) using (var t = new SolidBrush(Color.FromArgb(tint, Theme.Side))) g.FillRectangle(t, 0, 0, c.Width, c.Height);
        return true;
    }
}

class BgPanel : Panel
{
    public int Tint;
    public BgPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaintBackground(PaintEventArgs e) { if (!Bg.Paint(e.Graphics, this, Tint)) base.OnPaintBackground(e); }
}

class BgFlow : FlowLayoutPanel
{
    public int Tint;
    public BgFlow() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaintBackground(PaintEventArgs e) { if (!Bg.Paint(e.Graphics, this, Tint)) base.OnPaintBackground(e); }
}

class PageFlow : BgFlow
{
    protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); if (Bg.On) Invalidate(true); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); if (Bg.On) Invalidate(true); }
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

    public static RoundButton Btn(string text, Action onClick, bool primary = false)
    {
        var b = new RoundButton { Text = text, Primary = primary, AutoSize = true };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static ToggleSwitch Toggle(string text, bool value, Action<bool> onChange)
    {
        var t = new ToggleSwitch(text, value);
        t.Toggled += onChange;
        return t;
    }

    public static FlowLayoutPanel Page()
    {
        return new PageFlow
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
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
        p.Resize += (_, _) => { using var path = Gfx.Round(new Rectangle(0, 0, p.Width, p.Height), 14); p.Region = new Region(path); };
        p.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.Border);
            using var path = Gfx.Round(new Rectangle(0, 0, p.Width - 1, p.Height - 1), 14);
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
                int on = 1, white = 0xFFFFFF;
                Gfx.DwmSetWindowAttribute(f.Handle, 20, ref on, 4);
                Gfx.DwmSetWindowAttribute(f.Handle, 36, ref white, 4);
                RetitleColour(f);
            }
            catch { }
        };
    }

    public static void RetitleColour(Form f)
    {
        try
        {
            int cap = ColorTranslator.ToWin32(Theme.Side);
            Gfx.DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);
            Gfx.DwmSetWindowAttribute(f.Handle, 34, ref cap, 4);
        }
        catch { }
    }
}

// ---------------- Tray menu look ----------------
class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Theme.Card);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var p = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(Theme.Hover);
        using var path = Gfx.Round(new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), 8);
        g.FillPath(b, path);
    }

    // Every item's text starts at the same left edge; shortcuts line up on the right.
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var item = e.Item;
        var font = e.TextFont ?? item.Font;
        var box = new Rectangle(16, 0, item.Width - 32, item.Height);
        TextRenderer.DrawText(e.Graphics, item.Text, font, box, item.Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.Left);
        if (item.Tag is string keys && keys.Length > 0)
            TextRenderer.DrawText(e.Graphics, keys, font, box, Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.Right);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var p = new Pen(Theme.Border);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(p, 10, y, e.Item.Width - 10, y);
    }

    sealed class DarkColors : ProfessionalColorTable
    {
        public override Color MenuBorder => Theme.Border;
        public override Color ToolStripDropDownBackground => Theme.Card;
        public override Color ImageMarginGradientBegin => Theme.Card;
        public override Color ImageMarginGradientMiddle => Theme.Card;
        public override Color ImageMarginGradientEnd => Theme.Card;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }
}

// ---------------- Dropdown ----------------
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
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Cursor = Cursors.Hand;
        Font = Theme.Body;
    }

    public int SelectedIndex { get => index; set { index = value; Invalidate(); } }

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
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor is { A: 255 } pc ? pc : Theme.Card);
        using var path = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 11);
        using var fill = new SolidBrush(hover ? Theme.Hover : Theme.Mix(Theme.Bg, Theme.Card, 0.5));
        using var pen = new Pen(hover ? Theme.Mix(Theme.Border, Color.White, 0.2) : Theme.Border);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
        var text = SelectedItem ?? Placeholder;
        TextRenderer.DrawText(g, text, Font, new Rectangle(14, 0, Width - 44, Height),
            SelectedItem == null ? Theme.Muted : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var cp = new Pen(Theme.Muted, 1.6f);
        float cx = Width - 20, cy = Height / 2f;
        g.DrawLines(cp, new[] { new PointF(cx - 5, cy - 2.5f), new PointF(cx, cy + 2.5f), new PointF(cx + 5, cy - 2.5f) });
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Items.Count == 0) return;
        var pop = new DropPopup(this) { Location = PointToScreen(new Point(0, Height + 4)) };
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
            BackColor = Theme.Card;
            Opacity = 0.97;
            DoubleBuffered = true;
            Font = o.Font;
            Size = new Size(o.Width, Math.Min(MaxRows, o.Items.Count) * RowH + 12);
            using var path = Gfx.Round(new Rectangle(0, 0, Width, Height), 12);
            Region = new Region(path);
            if (o.index > MaxRows - 1) scroll = Math.Min(o.index - MaxRows / 2, o.Items.Count - MaxRows);
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }

        int RowAt(int y) { int r = (y - 6) / RowH; return y >= 6 && r < Math.Min(MaxRows, owner.Items.Count) ? r + scroll : -1; }

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
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Border))
            using (var path = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12))
                g.DrawPath(pen, path);
            int rows = Math.Min(MaxRows, owner.Items.Count);
            for (int i = 0; i < rows; i++)
            {
                int idx = i + scroll;
                var rect = new Rectangle(6, 6 + i * RowH, Width - 12, RowH);
                bool sel = idx == owner.index, hov = idx == hoverRow;
                if (sel || hov)
                {
                    using var b = new SolidBrush(sel ? Theme.Mix(Theme.Hover, Color.White, 0.05) : Theme.Hover);
                    using var rp = Gfx.Round(new RectangleF(rect.X, rect.Y + 1, rect.Width, rect.Height - 2), 9);
                    g.FillPath(b, rp);
                }
                TextRenderer.DrawText(g, owner.Items[idx], Font, new Rectangle(rect.X + 10, rect.Y, rect.Width - 30, rect.Height),
                    sel ? Color.White : Theme.Mix(Theme.Text, Theme.Muted, 0.3), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (sel)
                {
                    using var cp = new Pen(Theme.Accent, 1.6f);
                    float cx = rect.Right - 16, cy = rect.Y + rect.Height / 2f;
                    g.DrawLines(cp, new[] { new PointF(cx - 5, cy), new PointF(cx - 1.5f, cy + 3.5f), new PointF(cx + 5, cy - 4) });
                }
            }
            if (owner.Items.Count > MaxRows)
            {
                float track = Height - 16, thumb = track * MaxRows / owner.Items.Count;
                float y = 8 + (track - thumb) * scroll / (owner.Items.Count - MaxRows);
                using var sb = new SolidBrush(Theme.Border);
                using var sp = Gfx.Round(new RectangleF(Width - 6, y, 3, thumb), 1.5f);
                g.FillPath(sb, sp);
            }
        }
    }
}

// ---------------- Sidebar ----------------
public enum IconKind { Display, Resolution, Monitor, Hue, Crosshair, Profiles, Customize, Settings, Exit }

class NavIcon : Control
{
    public readonly IconKind Kind;
    bool selected, hover;
    public bool Selected { get => selected; set { selected = value; Invalidate(); } }

    public NavIcon(IconKind kind)
    {
        Kind = kind;
        Size = new Size(48, 48);
        Margin = new Padding(8, 4, 8, 4);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var tile = new RectangleF(4, 4, Width - 8, Height - 8);
        if (selected)
        {
            using var b = new SolidBrush(Theme.Accent);
            using var path = Gfx.Round(tile, 11);
            g.FillPath(b, path);
        }
        else if (hover)
        {
            using var b = new SolidBrush(Color.FromArgb(150, Theme.Hover));
            using var p = new Pen(Color.FromArgb(120, Theme.Mix(Theme.Border, Color.White, 0.2)));
            using var path = Gfx.Round(tile, 11);
            g.FillPath(b, path);
            g.DrawPath(p, path);
        }
        var col = selected ? Theme.AccentText : hover ? Color.White : Color.FromArgb(185, 185, 190);
        Draw(g, Kind, col, Width / 2f, Height / 2f);
    }

    public static void Draw(Graphics g, IconKind k, Color col, float cx, float cy)
    {
        using var p = new Pen(col, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var b = new SolidBrush(col);
        switch (k)
        {
            case IconKind.Display:
                g.DrawEllipse(p, cx - 8, cy - 8, 16, 16);
                g.FillPie(b, cx - 8, cy - 8, 16, 16, 90, 180);
                break;
            case IconKind.Resolution:
            {
                using var r = Gfx.Round(new RectangleF(cx - 11, cy - 8, 22, 16), 3);
                g.DrawPath(p, r);
                g.DrawLines(p, new[] { new PointF(cx - 6, cy - 1), new PointF(cx - 6, cy - 4), new PointF(cx - 3, cy - 4) });
                g.DrawLines(p, new[] { new PointF(cx + 6, cy + 1), new PointF(cx + 6, cy + 4), new PointF(cx + 3, cy + 4) });
                break;
            }
            case IconKind.Monitor:
            {
                using var r = Gfx.Round(new RectangleF(cx - 11, cy - 9, 22, 15), 3);
                g.DrawPath(p, r);
                g.DrawLine(p, cx, cy + 6, cx, cy + 9);
                g.DrawLine(p, cx - 5, cy + 9.5f, cx + 5, cy + 9.5f);
                break;
            }
            case IconKind.Hue: // droplet
            {
                g.DrawArc(p, cx - 6.5f, cy - 3, 13, 13, -30, 240);
                float tx = 6.5f * (float)Math.Cos(Math.PI / 6), ty = cy + 3.5f - 6.5f * 0.5f;
                g.DrawLine(p, cx + tx, ty, cx, cy - 10);
                g.DrawLine(p, cx - tx, ty, cx, cy - 10);
                g.FillPie(b, cx - 6.5f, cy - 3, 13, 13, 90, 90);
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
            case IconKind.Profiles:
            {
                using var r = Gfx.Round(new RectangleF(cx - 11, cy - 6, 22, 12), 6);
                g.DrawPath(p, r);
                g.DrawLine(p, cx - 7, cy, cx - 3, cy);
                g.DrawLine(p, cx - 5, cy - 2, cx - 5, cy + 2);
                g.FillEllipse(b, cx + 3, cy - 2.2f, 2.6f, 2.6f);
                g.FillEllipse(b, cx + 6, cy - 0.4f, 2.6f, 2.6f);
                break;
            }
            case IconKind.Customize:
            {
                using var r = Gfx.Round(new RectangleF(cx - 10, cy - 9, 20, 18), 3);
                g.DrawPath(p, r);
                g.DrawLine(p, cx - 4, cy - 9, cx - 4, cy + 9);
                g.DrawLine(p, cx - 4, cy - 1, cx + 10, cy - 1);
                break;
            }
            case IconKind.Settings:
                g.DrawEllipse(p, cx - 4, cy - 4, 8, 8);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(p, cx + (float)Math.Cos(a) * 7, cy + (float)Math.Sin(a) * 7, cx + (float)Math.Cos(a) * 10, cy + (float)Math.Sin(a) * 10);
                }
                break;
            case IconKind.Exit:
                g.DrawLines(p, new[] { new PointF(cx - 2, cy - 9), new PointF(cx - 9, cy - 9), new PointF(cx - 9, cy + 9), new PointF(cx - 2, cy + 9) });
                g.DrawLine(p, cx - 3, cy, cx + 10, cy);
                g.DrawLines(p, new[] { new PointF(cx + 6, cy - 4), new PointF(cx + 10, cy), new PointF(cx + 6, cy + 4) });
                break;
        }
    }
}

class LogoTile : Control
{
    public LogoTile()
    {
        Size = new Size(40, 40);
        Margin = new Padding(12, 14, 12, 18);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var b = new SolidBrush(Theme.Accent);
        using var path = Gfx.Round(new RectangleF(0, 0, Width - 1, Height - 1), 11);
        g.FillPath(b, path);
        using var f = new Font("Segoe UI Black", 17f, FontStyle.Bold);
        using var tb = new SolidBrush(Theme.AccentText);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("S", f, tb, new RectangleF(0, 1, Width, Height), sf);
    }
}

// ---------------- Crosshair ----------------
static class CrosshairArt
{
    public static void Draw(Graphics g, CrosshairSettings s, int c, int cy)
    {
        g.SmoothingMode = SmoothingMode.None;
        int t = Math.Max(1, s.Thickness), len = s.Size, gap = s.Gap, half = t / 2;
        var col = Color.FromArgb(s.Argb);
        var rects = new List<Rectangle>();
        switch (s.Style)
        {
            case "Dot":
                rects.Add(new Rectangle(c - t - 1, cy - t - 1, t * 2 + 2, t * 2 + 2));
                break;
            case "Circle":
                if (s.Outline) using (var op = new Pen(Color.Black, t + 2)) g.DrawEllipse(op, c - len, cy - len, len * 2, len * 2);
                using (var p = new Pen(col, t)) g.DrawEllipse(p, c - len, cy - len, len * 2, len * 2);
                break;
            case "T":
                rects.Add(new Rectangle(c - half, cy + gap, t, len));
                rects.Add(new Rectangle(c - gap - len, cy - half, len, t));
                rects.Add(new Rectangle(c + gap, cy - half, len, t));
                break;
            default:
                rects.Add(new Rectangle(c - half, cy - gap - len, t, len));
                rects.Add(new Rectangle(c - half, cy + gap, t, len));
                rects.Add(new Rectangle(c - gap - len, cy - half, len, t));
                rects.Add(new Rectangle(c + gap, cy - half, len, t));
                break;
        }
        if (s.CenterDot && s.Style != "Dot") rects.Add(new Rectangle(c - half, cy - half, t, t));
        if (s.Outline)
        {
            using var ob = new SolidBrush(Color.Black);
            foreach (var r in rects) g.FillRectangle(ob, Rectangle.Inflate(r, 1, 1));
        }
        using var br = new SolidBrush(col);
        foreach (var r in rects) g.FillRectangle(br, r);
    }
}

// Live preview of the crosshair on a little game-like scene.
class CrosshairPreview : Control
{
    readonly CrosshairSettings s;
    public CrosshairPreview(CrosshairSettings settings, int w = 420, int h = 230)
    {
        s = settings;
        Size = new Size(w, h);
        Margin = new Padding(4, 4, 16, 4);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Card);
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using var clip = Gfx.Round(r, 14);
        g.SetClip(clip);
        const int sq = 14;
        using (var a = new SolidBrush(Color.FromArgb(58, 60, 66)))
        using (var b2 = new SolidBrush(Color.FromArgb(44, 46, 51)))
            for (int y = 0; y < Height; y += sq)
                for (int x = 0; x < Width; x += sq)
                    g.FillRectangle(((x / sq + y / sq) & 1) == 0 ? a : b2, x, y, sq, sq);
        g.ResetClip();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, clip);
        CrosshairArt.Draw(g, s, Width / 2, Height / 2);
    }
}

// Click-through crosshair window. A separate window drawn over your screen; never touches the game.
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
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000; return cp; }
    }

    public void Recenter()
    {
        var b = Screen.PrimaryScreen!.Bounds;
        var p = new Point(b.Left + b.Width / 2 - Box / 2, b.Top + b.Height / 2 - Box / 2);
        if (Location != p) Location = p;
    }

    protected override void OnPaint(PaintEventArgs e) => CrosshairArt.Draw(e.Graphics, s, Box / 2, Box / 2);

    protected override void Dispose(bool disposing) { if (disposing) keepOnTop.Dispose(); base.Dispose(disposing); }
}

// A rainbow and the same rainbow after the hue shift.
class HuePreview : Control
{
    public double Degrees, Sat = 100;
    public HuePreview(int w = 420)
    {
        Size = new Size(w, 70);
        Margin = new Padding(4, 8, 4, 8);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Card);
        var m = ColorEngine.Mul(ColorEngine.HueMatrix(Degrees), ColorEngine.SatMatrix(Sat / 100.0));
        int h = 28;
        for (int x = 0; x < Width; x++)
        {
            var c = FromHsv(x * 359.9 / Width);
            double r = c.R / 255.0, gr = c.G / 255.0, b = c.B / 255.0;
            double nr = r * m[0, 0] + gr * m[1, 0] + b * m[2, 0];
            double ng = r * m[0, 1] + gr * m[1, 1] + b * m[2, 1];
            double nb = r * m[0, 2] + gr * m[1, 2] + b * m[2, 2];
            var c2 = Color.FromArgb(Cl(nr), Cl(ng), Cl(nb));
            using var p1 = new Pen(c); g.DrawLine(p1, x, 4, x, 4 + h);
            using var p2 = new Pen(c2); g.DrawLine(p2, x, 10 + h, x, 10 + h * 2);
        }
        TextRenderer.DrawText(g, "before", Theme.Body, new Point(6, 9), Color.Black);
        TextRenderer.DrawText(g, "after", Theme.Body, new Point(6, 15 + h), Color.Black);
    }

    static int Cl(double v) => (int)Math.Clamp(v * 255, 0, 255);

    static Color FromHsv(double h)
    {
        double x = 1 - Math.Abs(h / 60 % 2 - 1);
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (1.0, x, 0.0), 1 => (x, 1.0, 0.0), 2 => (0.0, 1.0, x), 3 => (0.0, x, 1.0), 4 => (x, 0.0, 1.0), _ => (1.0, 0.0, x),
        };
        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }
}
