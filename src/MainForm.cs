using System.Diagnostics;
using System.Security.Cryptography;

namespace Syntra;

class MainForm : Form
{
    readonly AppSettings S = Program.Settings;
    readonly Panel content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    readonly FlowLayoutPanel side = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        BackColor = Theme.Side, Padding = new Padding(0, 0, 0, 0),
    };
    readonly Panel sideHost = new() { Dock = DockStyle.Left, Width = 64, BackColor = Theme.Side };
    readonly FlowLayoutPanel sideBottom = new()
    {
        Dock = DockStyle.Bottom, Height = 132, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        BackColor = Theme.Side, Padding = new Padding(0, 0, 0, 10),
    };
    readonly ToolTip tips = new() { InitialDelay = 250 };
    readonly Dictionary<string, Func<Control>> pages = new();
    readonly Dictionary<string, NavIcon> navButtons = new();
    readonly NotifyIcon tray = new();
    readonly System.Windows.Forms.Timer watch = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer saveDebounce = new() { Interval = 600 };
    MonitorDdc? ddc;
    CrosshairOverlay? overlay;
    string? activeGame;          // exe name of the game whose profile is in use
    string currentPage = "Display";
    const int HotkeyId = 0x5A11;

    public MainForm()
    {
        Text = "Syntra";
        Size = new Size(1080, 720);
        MinimumSize = new Size(1000, 560);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = MakeIcon();
        Ui.DarkTitle(this);

        Controls.Add(content);
        sideHost.Controls.Add(side);
        sideHost.Controls.Add(sideBottom);
        sideHost.Paint += (_, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, sideHost.Width - 1, 0, sideHost.Width - 1, sideHost.Height); };
        Controls.Add(sideHost);

        side.Controls.Add(new LogoTile());
        AddPage("Display", IconKind.Display, BuildDisplay);
        AddPage("Resolution", IconKind.Resolution, BuildResolution);
        AddPage("Monitor", IconKind.Monitor, BuildMonitor);
        AddPage("Crosshair", IconKind.Crosshair, BuildCrosshair);
        AddPage("Profiles", IconKind.Profiles, BuildProfiles);
        AddPage("Settings", IconKind.Settings, BuildSettings, bottom: true);
        var exit = new NavIcon(IconKind.Exit);
        tips.SetToolTip(exit, "Exit (puts your screen back)");
        exit.Click += (_, _) => Program.ExitApp();
        sideBottom.Controls.Add(exit);

        saveDebounce.Tick += (_, _) => { saveDebounce.Stop(); S.Save(); };

        tray.Icon = Icon;
        tray.Text = "Syntra";
        tray.Visible = true;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Syntra", null, (_, _) => ShowFromTray());
        menu.Items.Add("Colours on / off  (Ctrl+Alt+V)", null, (_, _) => ToggleEnabled());
        menu.Items.Add("Put my screen back to normal", null, (_, _) => { S.Enabled = false; ApplyNow(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit (restores your screen)", null, (_, _) => Program.ExitApp());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowFromTray();

        watch.Tick += (_, _) => WatchForeground();
        watch.Start();

        Show("Display");
        ApplyNow();
        if (S.Crosshair.On) ShowCrosshair(true);
    }

    // ---------- plumbing ----------
    void AddPage(string name, IconKind icon, Func<Control> build, bool bottom = false)
    {
        pages[name] = build;
        var b = new NavIcon(icon);
        tips.SetToolTip(b, name);
        b.Click += (_, _) => Show(name);
        navButtons[name] = b;
        (bottom ? sideBottom : side).Controls.Add(b);
    }

    void Show(string name)
    {
        currentPage = name;
        foreach (var (n, b) in navButtons) b.Selected = n == name;
        content.SuspendLayout();
        foreach (Control c in content.Controls) c.Dispose();
        content.Controls.Clear();
        content.Controls.Add(pages[name]());
        content.ResumeLayout();
    }

    void Refresh(string page) { if (currentPage == page) Show(page); }

    Look ActiveLook => activeGame != null && S.GameProfiles.TryGetValue(activeGame, out var l) ? l : S.Current;

    void ApplyNow()
    {
        if (S.Enabled) ColorEngine.Apply(ActiveLook, S.LowBlue);
        else ColorEngine.Reset();
    }

    void Changed() { ApplyNow(); saveDebounce.Stop(); saveDebounce.Start(); }

    void ToggleEnabled() { S.Enabled = !S.Enabled; ApplyNow(); S.Save(); Refresh("Display"); }

    // Uses only the foreground window's process *name* (like Task Manager shows). Never opens the game's memory.
    void WatchForeground()
    {
        string? game = null;
        try
        {
            var h = Native.GetForegroundWindow();
            if (h != IntPtr.Zero)
            {
                Native.GetWindowThreadProcessId(h, out var pid);
                using var p = Process.GetProcessById((int)pid);
                var n = p.ProcessName;
                if (S.GameProfiles.ContainsKey(n)) game = n;
            }
        }
        catch { }
        if (game != activeGame)
        {
            activeGame = game;
            ApplyNow();
            if (currentPage is "Display" or "Profiles") Show(currentPage);
        }
        if (S.Enabled) ColorEngine.Apply(ActiveLook, S.LowBlue); // some games reset gamma on launch; re-assert gently
    }

    void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RegisterHotKey(Handle, HotkeyId, 0x1 | 0x2 | 0x4000, (uint)Keys.V); // Ctrl+Alt+V, no repeat
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId) ToggleEnabled();
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !Program.Exiting)
        {
            e.Cancel = true;
            Hide();
            tray.ShowBalloonTip(2000, "Syntra is still running", "Your look stays on. Right-click the tray icon to exit.", ToolTipIcon.Info);
            return;
        }
        base.OnFormClosing(e);
    }

    public void Cleanup()
    {
        watch.Stop();
        try { Native.UnregisterHotKey(Handle, HotkeyId); } catch { }
        overlay?.Close();
        S.Save();
        tray.Visible = false;
        tray.Dispose();
        ddc?.Dispose();
    }

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var p = new Pen(Color.White, 3);
            for (int i = 0; i < 4; i++)
            {
                double a = i * Math.PI / 4;
                float dx = (float)(Math.Cos(a) * 13), dy = (float)(Math.Sin(a) * 13);
                g.DrawLine(p, 16 - dx, 16 - dy, 16 + dx, 16 + dy);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ---------- Display ----------
    Control BuildDisplay()
    {
        var page = Ui.Page();
        var L = ActiveLook;
        string status = !S.Enabled ? "● Off" : activeGame != null ? $"● Live · {activeGame} profile" : "● Live";
        var live = Ui.Lbl(status, Theme.H2, S.Enabled ? Theme.Good : Theme.Muted);
        live.Margin = new Padding(4, 8, 12, 4);
        page.Controls.Add(Ui.Header("Display", "Presets are starting points. The sliders work in whatever you play.",
            live,
            Ui.Btn(S.Enabled ? "Turn off" : "Turn on", ToggleEnabled),
            Ui.Btn("Reset", () => { CopyInto(new Look(), L); Changed(); Show("Display"); })));

        var top = Ui.Card();
        var sat = new DarkSlider("Saturation   (100 = the game as it ships)", 0, 300, L.Saturation, 100, 400) { Format = v => $"{v:0}%", Mark = 100 };
        sat.Changed += v => { L.Saturation = v; Changed(); };
        string vibNote = ColorEngine.HasDriverVibrance ? "NVIDIA driver vibrance" : "software";
        var vib = new DarkSlider($"Vibrance   (50 = normal · {vibNote})", 0, 100, L.Vibrance, 50, 400) { Format = v => $"{v:0}%", Mark = 50 };
        vib.Changed += v => { L.Vibrance = v; Changed(); };
        top.Controls.Add(sat); top.Controls.Add(vib);
        page.Controls.Add(top);

        var fine = Ui.Card("Fine tune", "Double-click a slider to reset it. Mouse wheel nudges it.", 540);
        void Add(string t, double min, double max, double val, double def, Action<double> set, Func<double, string>? fmt = null, double step = 1)
        {
            var s = new DarkSlider(t, min, max, val, def, 230) { Step = step, Margin = new Padding(2, 2, 14, 2) };
            if (fmt != null) s.Format = fmt;
            s.Changed += v => { set(v); Changed(); };
            fine.Controls.Add(s);
        }
        Add("Brightness", 50, 150, L.Brightness, 100, v => L.Brightness = v, v => $"{v:0}%");
        Add("Gamma", 0.5, 2.0, L.Gamma, 1.0, v => L.Gamma = v, v => v.ToString("0.00"), 0.01);
        Add("Contrast", 50, 150, L.Contrast, 100, v => L.Contrast = v, v => $"{v:0}%");
        Add("Temperature", -100, 100, L.Temperature, 0, v => L.Temperature = v);
        Add("Highlights", -100, 100, L.Highlights, 0, v => L.Highlights = v);
        Add("Shadows", -100, 100, L.Shadows, 0, v => L.Shadows = v);
        Add("Blacks", -100, 100, L.Blacks, 0, v => L.Blacks = v);
        Add("Whites", -100, 100, L.Whites, 0, v => L.Whites = v);
        if (ColorEngine.LastError != "") fine.Controls.Add(Ui.Lbl(ColorEngine.LastError, Theme.Body, Theme.Warn, 500));

        var presets = Ui.Card("Presets", "Built-in and your saved looks.", 328);
        var all = Look.BuiltIn().Concat(S.Saved).ToList();
        var combo = Ui.Combo(all.Select(l => l.Name), 290);
        presets.Controls.Add(combo);
        presets.Controls.Add(Ui.Btn("Apply", () =>
        {
            if (combo.SelectedIndex < 0) return;
            CopyInto(all[combo.SelectedIndex], L); Changed(); Show("Display");
        }, true));
        presets.Controls.Add(Ui.Btn("Save look", () =>
        {
            var name = Prompt("Save look", "Name for this look:", "My look");
            if (string.IsNullOrWhiteSpace(name)) return;
            var c = L.Clone(); c.Name = name.Trim(); c.Note = "Saved by you.";
            S.Saved.RemoveAll(x => x.Name == c.Name);
            S.Saved.Add(c); S.Save(); Show("Display");
        }));
        presets.Controls.Add(Ui.Btn("Delete saved", () =>
        {
            if (combo.SelectedIndex < 0) return;
            var n = all[combo.SelectedIndex].Name;
            if (S.Saved.RemoveAll(x => x.Name == n) > 0) { S.Save(); Show("Display"); }
        }));
        if (!ColorEngine.HasMatrix)
            presets.Controls.Add(Ui.Lbl("Windows colour effects are unavailable, so saturation can't be changed on this PC. Gamma and fine tune still work.", Theme.Body, Theme.Warn, 290));

        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0) };
        fine.Margin = new Padding(0, 0, 12, 12);
        row.Controls.Add(fine); row.Controls.Add(presets);
        page.Controls.Add(row);
        return page;
    }

    static void CopyInto(Look from, Look to)
    {
        to.Saturation = from.Saturation; to.Vibrance = from.Vibrance; to.Brightness = from.Brightness;
        to.Contrast = from.Contrast; to.Gamma = from.Gamma; to.Temperature = from.Temperature;
        to.Highlights = from.Highlights; to.Shadows = from.Shadows; to.Blacks = from.Blacks; to.Whites = from.Whites;
    }

    // ---------- Resolution ----------
    Control BuildResolution()
    {
        var page = Ui.Page();
        var cur = DisplayModes.Current();
        var modes = DisplayModes.All();
        page.Controls.Add(Ui.Header("Resolution", "Change resolution and refresh rate. Every change asks before it stays."));

        var now = Ui.Card();
        now.Controls.Add(Ui.Lbl($"{cur.W} × {cur.H}", Theme.Big, width: Ui.PageW - 40));
        now.Controls.Add(Ui.Lbl($"{cur.Hz} Hz · {Aspect(cur.W, cur.H)}", Theme.Body, Theme.Muted, Ui.PageW - 40));
        page.Controls.Add(now);

        var res = Ui.Card("Resolution", null);
        foreach (var r in modes.Select(m => (m.W, m.H)).Distinct().OrderByDescending(x => x.W * x.H).Take(24))
        {
            var rr = r;
            var b = Ui.Btn($"{r.W} × {r.H}" + (Aspect(r.W, r.H) == "4:3" ? "  4:3" : ""), () =>
            {
                int hz = modes.Where(m => m.W == rr.W && m.H == rr.H).Select(m => m.Hz).DefaultIfEmpty(60).Max();
                TryMode(new DisplayModes.Mode(rr.W, rr.H, hz));
            }, r.W == cur.W && r.H == cur.H);
            b.AutoSize = false; b.Size = new Size(150, 34);
            res.Controls.Add(b);
        }
        page.Controls.Add(res);

        var hzCard = Ui.Card("Refresh rate", "Highest first.");
        foreach (var hz in modes.Where(m => m.W == cur.W && m.H == cur.H).Select(m => m.Hz).Distinct().OrderByDescending(x => x))
        {
            var h = hz;
            var b = Ui.Btn($"{hz} Hz", () => TryMode(cur with { Hz = h }), hz == cur.Hz);
            b.AutoSize = false; b.Size = new Size(110, 34);
            hzCard.Controls.Add(b);
        }
        page.Controls.Add(hzCard);

        var pvp = Ui.Card("PvP presets", "Stretched modes need GPU scaling set to “Full-screen” in your NVIDIA/AMD panel to fill the screen.");
        DisplayModes.Mode Best(int w, int h) => new(w, h, modes.Where(m => m.W == w && m.H == h).Select(m => m.Hz).DefaultIfEmpty(cur.Hz).Max());
        var native = modes.OrderByDescending(m => m.W * m.H).ThenByDescending(m => m.Hz).FirstOrDefault() ?? cur;
        pvp.Controls.Add(Ui.Btn($"Native {native.W}×{native.H}", () => TryMode(native)));
        if (modes.Any(m => m.W == 1440 && m.H == 1080)) pvp.Controls.Add(Ui.Btn("Stretched 4:3 · 1440×1080", () => TryMode(Best(1440, 1080))));
        if (modes.Any(m => m.W == 1280 && m.H == 960)) pvp.Controls.Add(Ui.Btn("Low 4:3 · 1280×960", () => TryMode(Best(1280, 960))));
        page.Controls.Add(pvp);

        var custom = Ui.Card("Custom", "Only modes your display reports can be used.");
        var tw = new NumericUpDown { Maximum = 7680, Minimum = 640, Value = cur.W, Width = 80, BackColor = Theme.Bg, ForeColor = Theme.Text };
        var th = new NumericUpDown { Maximum = 4320, Minimum = 480, Value = cur.H, Width = 80, BackColor = Theme.Bg, ForeColor = Theme.Text };
        var thz = new NumericUpDown { Maximum = 540, Minimum = 24, Value = cur.Hz, Width = 70, BackColor = Theme.Bg, ForeColor = Theme.Text };
        custom.Controls.AddRange(new Control[] { tw, Ui.Lbl("×"), th, Ui.Lbl("at"), thz, Ui.Lbl("Hz"),
            Ui.Btn("Apply", () => TryMode(new DisplayModes.Mode((int)tw.Value, (int)th.Value, (int)thz.Value)), true) });
        page.Controls.Add(custom);
        return page;
    }

    static string Aspect(int w, int h)
    {
        int a = w, b = h;
        while (b != 0) (a, b) = (b, a % b);
        var r = $"{w / a}:{h / a}";
        return r switch { "8:5" => "16:10", "683:384" => "16:9", "85:48" => "16:9", _ => r };
    }

    void TryMode(DisplayModes.Mode m)
    {
        var before = DisplayModes.Current();
        if (m == before) return;
        if (!DisplayModes.Apply(m, out var err)) { MessageBox.Show(this, err, "Syntra"); return; }
        if (!KeepDialog()) DisplayModes.Apply(before, out _);
        Show("Resolution");
    }

    bool KeepDialog()
    {
        int left = 15;
        using var f = new Form
        {
            Text = "Keep this display mode?", Size = new Size(380, 170), StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, TopMost = true,
            BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.Body,
        };
        Ui.DarkTitle(f);
        var lbl = new Label { Text = $"Going back in {left} s.", Location = new Point(20, 20), AutoSize = true };
        var keep = new Button { Text = "Keep", DialogResult = DialogResult.OK, Location = new Point(170, 80), Size = new Size(85, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.Black };
        var back = new Button { Text = "Go back", DialogResult = DialogResult.Cancel, Location = new Point(265, 80), Size = new Size(85, 32), FlatStyle = FlatStyle.Flat };
        f.Controls.AddRange(new Control[] { lbl, keep, back });
        f.AcceptButton = keep; f.CancelButton = back;
        using var t = new System.Windows.Forms.Timer { Interval = 1000 };
        t.Tick += (_, _) => { left--; lbl.Text = $"Going back in {left} s."; if (left <= 0) { f.DialogResult = DialogResult.Cancel; f.Close(); } };
        t.Start();
        return f.ShowDialog() == DialogResult.OK;
    }

    // ---------- Monitor ----------
    Control BuildMonitor()
    {
        var page = Ui.Page();
        ddc ??= new MonitorDdc();
        page.Controls.Add(Ui.Header("Monitor", "Your monitor's own settings, without reaching round the back.",
            Ui.Btn("Restore my monitor", () =>
            {
                ddc.SetBrightness(ddc.OrigBrightness); ddc.SetContrast(ddc.OrigContrast);
                S.LowBlue = 0; Changed(); Show("Monitor");
            })));

        var hw = Ui.Card(ddc.Name, ddc.Available
            ? "Changed inside the monitor over DDC/CI, like pressing its buttons."
            : "This monitor doesn't answer DDC/CI (common on laptops and some TVs). Turn DDC/CI on in the monitor's own menu.");
        if (ddc.HasBrightness)
        {
            var b = new DarkSlider("Brightness", 0, 100, ddc.OrigBrightness, ddc.OrigBrightness, 420) { Format = v => $"{v:0}%" };
            b.Released += () => ddc.SetBrightness((uint)b.Value);
            hw.Controls.Add(b);
        }
        if (ddc.HasContrast)
        {
            var c = new DarkSlider("Contrast", 0, 100, ddc.OrigContrast, ddc.OrigContrast, 420) { Format = v => $"{v:0}%" };
            c.Released += () => ddc.SetContrast((uint)c.Value);
            hw.Controls.Add(c);
        }
        page.Controls.Add(hw);

        var soft = Ui.Card("Low blue light", "Warmer for late sessions. Works on every monitor.");
        var lb = new DarkSlider("Low blue light", 0, 100, S.LowBlue, 0, 420) { Format = v => $"{v:0}%" };
        lb.Changed += v => { S.LowBlue = v; Changed(); };
        soft.Controls.Add(lb);
        page.Controls.Add(soft);
        return page;
    }

    // ---------- Crosshair ----------
    void ShowCrosshair(bool on)
    {
        if (on)
        {
            overlay ??= new CrosshairOverlay(S.Crosshair);
            overlay.Show();
            overlay.Invalidate();
        }
        else { overlay?.Close(); overlay = null; }
    }

    Control BuildCrosshair()
    {
        var page = Ui.Page();
        var c = S.Crosshair;
        page.Controls.Add(Ui.Header("Crosshair", "A see-through window in the middle of your screen. Works in borderless/windowed games."));
        var card = Ui.Card();
        card.Controls.Add(Ui.Toggle("Show crosshair", c.On, v => { c.On = v; ShowCrosshair(v); S.Save(); }));
        card.Controls.Add(Ui.Toggle("Black outline", c.Outline, v => { c.Outline = v; overlay?.Invalidate(); S.Save(); }));
        card.Controls.Add(Ui.Toggle("Centre dot", c.CenterDot, v => { c.CenterDot = v; overlay?.Invalidate(); S.Save(); }));
        var style = Ui.Combo(new[] { "Cross", "T", "Dot", "Circle" }, 160);
        style.SelectedItem = c.Style;
        style.SelectedChanged += () => { c.Style = (string)style.SelectedItem!; overlay?.Invalidate(); S.Save(); };
        card.Controls.Add(style);
        void Sl(string t, int min, int max, int val, int def, Action<int> set)
        {
            var s = new DarkSlider(t, min, max, val, def, 260);
            s.Changed += v => { set((int)v); overlay?.Invalidate(); };
            s.Released += () => S.Save();
            card.Controls.Add(s);
        }
        Sl("Size", 2, 60, c.Size, 10, v => c.Size = v);
        Sl("Thickness", 1, 8, c.Thickness, 2, v => c.Thickness = v);
        Sl("Gap", 0, 20, c.Gap, 3, v => c.Gap = v);
        page.Controls.Add(card);

        var colours = Ui.Card("Colour");
        foreach (var col in new[] { Color.FromArgb(0, 255, 102), Color.Red, Color.Cyan, Color.Yellow, Color.Magenta, Color.White })
        {
            var cc = col;
            var b = new Button { BackColor = cc, FlatStyle = FlatStyle.Flat, Size = new Size(40, 30), Margin = new Padding(4), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = c.Argb == cc.ToArgb() ? Color.White : Theme.Border;
            b.FlatAppearance.BorderSize = 2;
            b.Click += (_, _) => { c.Argb = cc.ToArgb(); overlay?.Invalidate(); S.Save(); Show("Crosshair"); };
            colours.Controls.Add(b);
        }
        colours.Controls.Add(Ui.Btn("Pick…", () =>
        {
            using var d = new ColorDialog { Color = Color.FromArgb(c.Argb), FullOpen = true };
            if (d.ShowDialog(this) == DialogResult.OK) { c.Argb = Color.FromArgb(255, d.Color).ToArgb(); overlay?.Invalidate(); S.Save(); Show("Crosshair"); }
        }));
        page.Controls.Add(colours);
        page.Controls.Add(Ui.Lbl("Exclusive full-screen games draw over every window, so use borderless/windowed full-screen for the overlay crosshair.", Theme.Body, Theme.Muted, Ui.PageW));
        return page;
    }

    // ---------- Profiles ----------
    Control BuildProfiles()
    {
        var page = Ui.Page();
        page.Controls.Add(Ui.Header("Profiles", "A look for each game — applied when it's in front, put back when you leave it."));

        var pick = Ui.Card("Pick a game", "Running programs with a window. Syntra only looks at the program's name.");
        var procs = Process.GetProcesses()
            .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.Id != Environment.ProcessId; } catch { return false; } })
            .Select(p => p.ProcessName).Where(n => !Hidden.Contains(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var combo = Ui.Combo(procs, 300);
        pick.Controls.Add(combo);
        pick.Controls.Add(Ui.Btn("Save current look for this game", () =>
        {
            if (combo.SelectedItem is not string n) return;
            var l = S.Current.Clone(); l.Name = n;
            S.GameProfiles[n] = l; S.Save(); Show("Profiles");
        }, true));
        pick.Controls.Add(Ui.Btn("Refresh list", () => Show("Profiles")));
        page.Controls.Add(pick);

        var saved = Ui.Card("Saved game profiles",
            S.GameProfiles.Count == 0 ? "No game profiles yet. Open a game, set your colours on Display, then save them here." :
            activeGame != null ? $"Now in use: {activeGame}. Changes on Display go to this game's profile while it's in front." : null);
        foreach (var (name, look) in S.GameProfiles.ToList())
        {
            saved.Controls.Add(Ui.Lbl($"{name}.exe", Theme.H2, width: 260));
            saved.Controls.Add(Ui.Lbl($"Saturation {look.Saturation:0}% · Vibrance {look.Vibrance:0}", Theme.Body, Theme.Muted, 340));
            var n = name;
            saved.Controls.Add(Ui.Btn("Remove", () => { S.GameProfiles.Remove(n); S.Save(); if (activeGame == n) activeGame = null; ApplyNow(); Show("Profiles"); }));
        }
        page.Controls.Add(saved);
        return page;
    }

    static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost", "TextInputHost", "explorer", "SystemSettings", "ShellExperienceHost", "StartMenuExperienceHost",
        "SearchHost", "LockApp", "XboxPcApp", "GameBar", "Widgets", "dwm", "taskmgr", "Syntra",
    };

    // ---------- Settings ----------
    Control BuildSettings()
    {
        var page = Ui.Page();
        page.Controls.Add(Ui.Header("Settings", "How Syntra itself behaves."));

        var gen = Ui.Card();
        gen.Controls.Add(Ui.Toggle("Launch with Windows (starts in the tray and puts your look back)", Startup.IsOn(), v => Startup.Set(v)));
        gen.Controls.Add(Ui.Lbl("Quick on/off shortcut:  Ctrl + Alt + V", Theme.Body, Theme.Text, Ui.PageW - 40));
        gen.Controls.Add(Ui.Lbl($"NVIDIA driver vibrance: {(ColorEngine.HasDriverVibrance ? "found" : "not found — software saturation is used")}", Theme.Body, Theme.Muted, Ui.PageW - 40));
        gen.Controls.Add(Ui.Lbl($"Running as administrator: {(Program.IsAdmin ? "yes" : "no (normal)")}", Theme.Body, Theme.Muted, Ui.PageW - 40));
        gen.Controls.Add(Ui.Btn("Put my screen back to normal", () => { S.Enabled = false; ApplyNow(); S.Save(); }));
        gen.Controls.Add(Ui.Btn("Repair: restart as admin", () => Startup.RestartAsAdmin()));
        gen.Controls.Add(Ui.Lbl("Only use Repair if colours won't apply. Windows asks you first.", Theme.Body, Theme.Muted, Ui.PageW - 40));
        page.Controls.Add(gen);

        var promise = Ui.Card("What Syntra never does", null);
        foreach (var line in new[]
        {
            "✓  It never reads a game's memory.",
            "✓  It never changes a game's files.",
            "✓  It never puts code into a game, or into any other program.",
            "✓  It installs no driver.",
            "✓  It runs as your own Windows user and needs no admin rights. One repair button can restart it as admin — Windows asks you first.",
        })
            promise.Controls.Add(Ui.Lbl(line, Theme.Body, Theme.Text, Ui.PageW - 40));
        page.Controls.Add(promise);

        var verify = Ui.Card("Verify this copy", "SHA-256 of the Syntra.exe now running. Compare it with the one on the GitHub release.");
        string hash;
        try { using var fs = File.OpenRead(Environment.ProcessPath!); hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant(); }
        catch { hash = "unavailable"; }
        var hb = new TextBox { Text = hash, ReadOnly = true, Width = 560, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9.5f) };
        verify.Controls.Add(hb);
        verify.Controls.Add(Ui.Btn("Copy", () => Clipboard.SetText(hash)));
        page.Controls.Add(verify);
        return page;
    }

    static string? Prompt(string title, string text, string def)
    {
        using var f = new Form
        {
            Text = title, Size = new Size(360, 160), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.Body,
        };
        Ui.DarkTitle(f);
        var l = new Label { Text = text, Location = new Point(14, 14), AutoSize = true };
        var tb = new TextBox { Text = def, Location = new Point(14, 40), Width = 315, BackColor = Theme.Bg, ForeColor = Theme.Text };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(244, 76), Size = new Size(85, 30), FlatStyle = FlatStyle.Flat };
        f.Controls.AddRange(new Control[] { l, tb, ok });
        f.AcceptButton = ok;
        return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
    }
}
