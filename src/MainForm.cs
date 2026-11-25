using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Syntra;

class MainForm : Form
{
    readonly AppSettings S = Program.Settings;
    readonly BgPanel content = new() { Dock = DockStyle.Fill };
    readonly BgPanel sideHost = new() { Dock = DockStyle.Left, Width = 64, Tint = Bg.SideTint };
    readonly BgFlow side = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Tint = Bg.SideTint };
    readonly BgFlow sideBottom = new()
    {
        Tint = Bg.SideTint,
        Dock = DockStyle.Bottom, Height = 124, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        Padding = new Padding(0, 0, 0, 10),
    };
    readonly LogoTile logo = new();
    readonly ToolTip tips = new() { InitialDelay = 250 };
    readonly Dictionary<string, Func<Control>> pages = new();
    readonly Dictionary<string, NavIcon> navButtons = new();
    readonly NotifyIcon tray = new();
    readonly ContextMenuStrip menu = new();
    readonly System.Windows.Forms.Timer watch = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer applyTimer = new() { Interval = 15 };
    readonly System.Windows.Forms.Timer saveDebounce = new() { Interval = 600 };
    readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 60_000 };
    bool dirty, remoteDirty, cloudChanged = true, cloudBusy, cloudApplying;
    long cloudRev;
    DateTime cloudBeat;
    readonly System.Windows.Forms.Timer cloudTimer = new() { Interval = 1000 };
    LiveLink? live;
    string liveDevice = "";
    DateTime lastRemoteRefresh;
    RemoteServer? remote;
    string remoteError = "";
    MonitorDdc? ddc;
    CrosshairOverlay? overlay;
    HueOverlay? hueOverlay;
    string? activeGame;
    string currentPage = "Display";
    const int HotkeyId = 0x5A11;
    static readonly int QuitMsg = RegisterQuit("Syntra.Quit");
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "RegisterWindowMessage", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int RegisterQuit(string name);

    public MainForm()
    {
        Text = "Syntra";
        Size = new Size(1080, 720);
        MinimumSize = new Size(1000, 560);
        Font = Theme.Body;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = MakeIcon();
        Ui.DarkTitle(this);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Bg.Root = this;
        Opacity = Math.Clamp(S.WindowOpacity, 60, 100) / 100.0;

        Controls.Add(content);
        content.Resize += (_, _) => FitPage();
        sideHost.Controls.Add(side);
        sideHost.Controls.Add(sideBottom);
        sideHost.Paint += (_, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, sideHost.Width - 1, 0, sideHost.Width - 1, sideHost.Height); };
        Controls.Add(sideHost);

        side.Controls.Add(logo);
        AddPage("Display", IconKind.Display, BuildDisplay);
        AddPage("Resolution", IconKind.Resolution, BuildResolution);
        AddPage("Monitor", IconKind.Monitor, BuildMonitor);
        AddPage("Hue changer", IconKind.Hue, BuildHue);
        AddPage("Crosshair", IconKind.Crosshair, BuildCrosshair);
        AddPage("Profiles", IconKind.Profiles, BuildProfiles);
        AddPage("Customize", IconKind.Customize, BuildCustomize);
        AddPage("Settings", IconKind.Settings, BuildSettings, bottom: true);
        var exit = new NavIcon(IconKind.Exit);
        tips.SetToolTip(exit, "Exit (puts your screen back)");
        exit.Click += (_, _) => Program.ExitApp();
        sideBottom.Controls.Add(exit);

        LoadBackground();
        ApplyThemeColours();

        saveDebounce.Tick += (_, _) => { saveDebounce.Stop(); S.Save(); };
        applyTimer.Tick += (_, _) =>
        {
            if (dirty) { dirty = false; ApplyNow(); }
            if (remoteDirty && (DateTime.UtcNow - lastRemoteRefresh).TotalMilliseconds > 400)
            {
                remoteDirty = false; lastRemoteRefresh = DateTime.UtcNow;
                if (currentPage is "Display" or "Hue changer" && Visible && Control.MouseButtons == MouseButtons.None) Show(currentPage);
            }
        };
        applyTimer.Start();

        tray.Icon = Icon;
        tray.Text = "Syntra";
        tray.Visible = true;
        menu.Renderer = new DarkMenuRenderer();
        menu.ShowImageMargin = false;
        menu.Font = new Font("Segoe UI", 10f);
        menu.Padding = new Padding(4, 6, 4, 6);
        menu.HandleCreated += (_, _) => Gfx.RoundCorners(menu.Handle);
        void Item(string text, Action a, string keys = "")
        {
            var it = new ToolStripMenuItem(text)
            {
                AutoSize = false, Size = new Size(300, 36), TextAlign = ContentAlignment.MiddleLeft,
                ShowShortcutKeys = false, Tag = keys,
            };
            it.Click += (_, _) => a();
            menu.Items.Add(it);
        }
        Item("Open Syntra", ShowFromTray);
        Item("Colours on / off", ToggleEnabled, "Ctrl+Alt+V");
        Item("Put my screen back to normal", () => { S.Enabled = false; ApplyNow(); S.Save(); });
        menu.Items.Add(new ToolStripSeparator { AutoSize = false, Size = new Size(300, 9) });
        Item("Exit", Program.ExitApp);
        menu.AutoSize = false;
        menu.Size = new Size(308, menu.Padding.Vertical + 4 * 36 + 9 + 4);
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowFromTray();

        watch.Tick += (_, _) => WatchForeground();
        watch.Start();

        updateTimer.Tick += async (_, _) => await CheckUpdate();
        updateTimer.Start();
        Load += async (_, _) => { await Task.Delay(3000); await CheckUpdate(); };

        Show("Display");
        ApplyNow();
        UpdateHueOverlay();
        if (S.Crosshair.On) ShowCrosshair(true);
        UpdateRemote();
        cloudTimer.Tick += async (_, _) => await CloudTick();
        cloudTimer.Start();
    }

    // ---------- Phone control ----------
    void UpdateRemote()
    {
        if (S.RemoteOn && remote == null)
        {
            remote = new RemoteServer(
                () => (string)Invoke(new Func<string>(RemoteState)),
                body => (string)Invoke(new Func<string>(() => RemoteSet(body))));
            try { remote.Start(); remoteError = ""; }
            catch (Exception ex) { remote = null; remoteError = ex.Message; }
        }
        else if (!S.RemoteOn && remote != null) { remote.Stop(); remote = null; }
    }

    string RemoteState()
    {
        var L = ActiveLook;
        var o = new System.Text.Json.Nodes.JsonObject
        {
            ["enabled"] = S.Enabled, ["saturation"] = L.Saturation, ["vibrance"] = L.Vibrance, ["brightness"] = L.Brightness,
            ["contrast"] = L.Contrast, ["gamma"] = L.Gamma, ["temperature"] = L.Temperature,
            ["hueOn"] = S.HueOn, ["hueDegrees"] = S.HueDegrees, ["game"] = activeGame,
            ["user"] = Account.Load().Name, ["version"] = Updater.Current.ToString(),
            ["presets"] = new System.Text.Json.Nodes.JsonArray(Look.BuiltIn().Concat(S.Saved).Select(l => (System.Text.Json.Nodes.JsonNode?)l.Name).ToArray()),
        };
        return o.ToJsonString();
    }

    string RemoteSet(string body)
    {
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(body) is System.Text.Json.Nodes.JsonObject j)
            {
                var L = ActiveLook;
                // Moving a colour slider from the phone means "show it": switch colours on unless the phone also said otherwise.
                if (!j.ContainsKey("enabled") && j.Any(kv => kv.Key is "saturation" or "vibrance" or "brightness" or "contrast" or "gamma" or "temperature" or "preset" or "hueDegrees" or "hueOn"))
                    S.Enabled = true;
                foreach (var (k, v) in j)
                {
                    if (v == null) continue;
                    double D(double min, double max) => Math.Clamp(v.GetValue<double>(), min, max);
                    switch (k)
                    {
                        case "saturation": L.Saturation = D(0, 300); break;
                        case "vibrance": L.Vibrance = D(0, 100); break;
                        case "brightness": L.Brightness = D(50, 150); break;
                        case "contrast": L.Contrast = D(50, 150); break;
                        case "gamma": L.Gamma = D(0.5, 2); break;
                        case "temperature": L.Temperature = D(-100, 100); break;
                        case "hueDegrees": S.HueDegrees = D(-180, 180); break;
                        case "enabled": S.Enabled = v.GetValue<bool>(); break;
                        case "hueOn": S.HueOn = v.GetValue<bool>(); break;
                        case "preset":
                            var name = v.GetValue<string>();
                            var p = Look.BuiltIn().Concat(S.Saved).FirstOrDefault(x => x.Name == name);
                            if (p != null) CopyInto(p, L);
                            break;
                    }
                }
                Changed();
                UpdateHueOverlay();
                hueOverlay?.SetEffect(S.HueDegrees, S.HueSat / 100.0);
                remoteDirty = true;
            }
        }
        catch { }
        return RemoteState();
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

        // The page is made a little wider/taller than the window so its scrollbar sits out of sight.
        Control page;
        try { page = pages[name](); }
        catch (Exception ex)
        {
            // Never leave a blank screen: show what went wrong and log it.
            try { File.AppendAllText(Path.Combine(AppSettings.Dir, "crash.log"), $"[{DateTime.Now}] v{Updater.Current} page {name}\n{ex}\n\n"); } catch { }
            var err = Ui.Page();
            err.Controls.Add(Ui.Header(name, "This page hit a problem. Click the tab again — and send crash.log from %AppData%\\Syntra if it keeps happening."));
            err.Controls.Add(Ui.Lbl(ex.Message, Theme.Body, Theme.Warn, Ui.PageW));
            err.Controls.Add(Ui.Btn("Try again", () => Show(name), true));
            page = err;
        }
        page.Dock = DockStyle.None;
        page.Location = Point.Empty;
        currentControl = page;
        FitPage();
        content.Controls.Add(page);
        content.ResumeLayout();
    }

    Control? currentControl;

    void FitPage()
    {
        if (currentControl == null || currentControl.IsDisposed) return;
        currentControl.Size = new Size(content.ClientSize.Width + SystemInformation.VerticalScrollBarWidth,
                                       content.ClientSize.Height + SystemInformation.HorizontalScrollBarHeight);
    }

    void Refresh(string page) { if (currentPage == page) Show(page); }

    void ApplyThemeColours()
    {
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        content.BackColor = Theme.Bg;
        sideHost.BackColor = side.BackColor = sideBottom.BackColor = Theme.Side;
        foreach (var b in navButtons.Values) b.Invalidate();
        logo.Invalidate();
        sideHost.Invalidate();
        if (IsHandleCreated) Ui.RetitleColour(this);
        Bg.ClearCache();
        Invalidate(true);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { if (!Bg.Paint(e.Graphics, this)) base.OnPaintBackground(e); }
    protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); if (Bg.On) Invalidate(true); }

    Look ActiveLook => activeGame != null && S.GameProfiles.TryGetValue(activeGame, out var l) ? l : S.Current;

    void ApplyNow(bool forceRamp = false)
    {
        if (!S.Enabled) { ColorEngine.Reset(); return; }
        bool wholeHue = S.HueOn && S.HueTarget == "";
        ColorEngine.Apply(ActiveLook, S.LowBlue, wholeHue ? S.HueDegrees : 0, wholeHue ? S.HueSat / 100.0 : 1, forceRamp);
    }

    // Slider moves only mark "dirty"; a 15 ms timer applies the newest value. Fast, never queues up.
    void Changed() { dirty = true; if (!cloudApplying) cloudChanged = true; saveDebounce.Stop(); saveDebounce.Start(); }

    // Syntra Cloud: the website writes settings with by="web"; this PC applies them and reports its own changes back.
    async Task CloudTick()
    {
        if (!CloudConfig.On || cloudBusy) return;
        cloudBusy = true;
        try
        {
            var acc = Account.Load();
            if (acc.CloudRefresh.Length == 0) return;
            JsonObject? mine = null;
            if (cloudChanged || (DateTime.UtcNow - cloudBeat).TotalSeconds > 25)
            {
                mine = JsonNode.Parse(RemoteState()) as JsonObject ?? new JsonObject();
                mine["rev"] = cloudRev + 1; mine["by"] = "pc"; mine["seen"] = cloudRev; // newest phone change this PC has applied
            }
            var st = await Cloud.Sync(acc, cloudChanged ? mine : null);
            if (st != null && (string?)st["by"] == "pc") { try { cloudRev = Math.Max(cloudRev, st["rev"]?.GetValue<long>() ?? 0); } catch { } }
            cloudBeat = DateTime.UtcNow;
            // open the instant link once we know this PC's id
            if (acc.DeviceId.Length > 0 && acc.DeviceId != liveDevice)
            {
                live?.Stop();
                liveDevice = acc.DeviceId;
                live = new LiveLink(liveDevice, changes => BeginInvoke(() =>
                {
                    cloudApplying = true;
                    try { RemoteSet(changes.ToJsonString()); } finally { cloudApplying = false; }
                    dirty = false; ApplyNow();          // apply right now, don't wait for the next timer tick
                    cloudChanged = true;
                }));
            }
            if (st == null) return;
            long rev = 0;
            try { rev = st["rev"]?.GetValue<long>() ?? 0; } catch { }
            string by = (string?)st["by"] ?? "";
            if (cloudChanged) { cloudChanged = false; cloudRev = Math.Max(cloudRev + 1, rev); }
            if (by == "web" && rev > cloudRev)
            {
                cloudRev = rev;
                var changes = st["changes"] as JsonObject ?? st;
                cloudApplying = true;
                try { RemoteSet(changes.ToJsonString()); } finally { cloudApplying = false; }
                cloudChanged = true; // report the applied state back
            }
        }
        catch { }
        finally { cloudBusy = false; }
    }

    void ToggleEnabled() { S.Enabled = !S.Enabled; ApplyNow(); UpdateHueOverlay(); S.Save(); Refresh("Display"); }

    // Uses only the foreground window's process *name*. Never opens the game's memory.
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
                if (S.GameProfiles.ContainsKey(p.ProcessName)) game = p.ProcessName;
            }
        }
        catch { }
        if (game != activeGame)
        {
            activeGame = game;
            ApplyNow();
            if (currentPage is "Display" or "Profiles") Show(currentPage);
        }
        else if (S.Enabled) ApplyNow(forceRamp: true); // some games reset gamma on launch
    }

    async Task CheckUpdate()
    {
        if (!S.AutoUpdate) return;
        var tag = await Updater.CheckAsync();
        if (currentPage == "Settings" && Visible) Show("Settings");
        if (tag == null) return;
        tray.ShowBalloonTip(3000, "Syntra updated", $"Restarting into {tag}…", ToolTipIcon.Info);
        await Task.Delay(1500);
        Updater.Restart(tray: !Visible);
    }

    void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RegisterHotKey(Handle, HotkeyId, 0x1 | 0x2 | 0x4000, (uint)Keys.V);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId) ToggleEnabled();
        if (m.Msg == Program.ShowMsg && Program.ShowMsg != 0) ShowFromTray();
        if (m.Msg == QuitMsg && QuitMsg != 0) Program.ExitApp();
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
        watch.Stop(); updateTimer.Stop(); applyTimer.Stop();
        remote?.Stop();
        cloudTimer.Stop();
        live?.Stop();
        try { Native.UnregisterHotKey(Handle, HotkeyId); } catch { }
        overlay?.Close();
        hueOverlay?.Close();
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
            using var b = new SolidBrush(Color.White);
            using var path = Gfx.Round(new RectangleF(1, 1, 30, 30), 8);
            g.FillPath(b, path);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("S", new Font("Segoe UI Black", 18f, FontStyle.Bold, GraphicsUnit.Pixel), Brushes.Black, new RectangleF(0, 1, 32, 32), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ---------- Display ----------
    Control BuildDisplay()
    {
        var page = Ui.Page();
        var L = ActiveLook;
        string status = !S.Enabled ? "● Off" : activeGame != null ? $"● Live · {activeGame}" : "● Live";
        var live = Ui.Lbl(status, Theme.H2, S.Enabled ? Theme.Good : Theme.Muted);
        live.Margin = new Padding(4, 10, 12, 4);
        page.Controls.Add(Ui.Header("Display", "Pick a preset, then fine-tune it for any game.",
            live,
            Ui.Btn(S.Enabled ? "Turn off" : "Turn on", ToggleEnabled),
            Ui.Btn("Reset", () => { CopyInto(new Look(), L); Changed(); Show("Display"); })));

        var top = Ui.Card();
        var sat = new DarkSlider("Saturation   (100 = untouched)", 0, 300, L.Saturation, 100, 400) { Format = v => $"{v:0}%", Mark = 100 };
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
        presets.Controls.Add(Ui.Btn("Delete", () =>
        {
            if (combo.SelectedIndex < 0) return;
            var n = all[combo.SelectedIndex].Name;
            if (S.Saved.RemoveAll(x => x.Name == n) > 0) { S.Save(); Show("Display"); }
        }));
        if (!ColorEngine.HasMatrix)
            presets.Controls.Add(Ui.Lbl("Windows colour effects are unavailable, so saturation can't be changed on this PC. Gamma and fine tune still work.", Theme.Body, Theme.Warn, 290));

        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
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
            b.AutoSize = false; b.Size = new Size(158, 40); b.Margin = new Padding(5);
            res.Controls.Add(b);
        }
        page.Controls.Add(res);

        var hzCard = Ui.Card("Refresh rate", "Highest first.");
        foreach (var hz in modes.Where(m => m.W == cur.W && m.H == cur.H).Select(m => m.Hz).Distinct().OrderByDescending(x => x))
        {
            var h = hz;
            var b = Ui.Btn($"{hz} Hz", () => TryMode(cur with { Hz = h }), hz == cur.Hz);
            b.AutoSize = false; b.Size = new Size(110, 40); b.Margin = new Padding(5);
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
        var tw = new DarkInput("Width", 110); tw.Box.Text = cur.W.ToString();
        var th = new DarkInput("Height", 110); th.Box.Text = cur.H.ToString();
        var thz = new DarkInput("Hz", 90); thz.Box.Text = cur.Hz.ToString();
        custom.Controls.AddRange(new Control[] { tw, th, thz,
            Ui.Btn("Apply", () =>
            {
                if (int.TryParse(tw.Box.Text, out var w) && int.TryParse(th.Box.Text, out var h) && int.TryParse(thz.Box.Text, out var z))
                    TryMode(new DisplayModes.Mode(w, h, z));
            }, true) });
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
            Text = "Keep this display mode?", Size = new Size(400, 190), StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, TopMost = true,
            BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.Body,
        };
        Ui.DarkTitle(f);
        var lbl = new Label { Text = $"Going back in {left} s.", Location = new Point(22, 22), AutoSize = true, Font = Theme.H2 };
        var keep = new RoundButton { Text = "Keep", Primary = true, DialogResult = DialogResult.OK, Location = new Point(166, 90), Size = new Size(96, 40) };
        var back = new RoundButton { Text = "Go back", DialogResult = DialogResult.Cancel, Location = new Point(270, 90), Size = new Size(96, 40) };
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
        page.Controls.Add(Ui.Header("Monitor", "Change your display's hardware brightness and contrast from here.",
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

    // ---------- Hue changer ----------
    void UpdateHueOverlay()
    {
        bool perApp = S.Enabled && S.HueOn && S.HueTarget != "";
        if (!perApp) { hueOverlay?.Close(); hueOverlay = null; return; }
        hueOverlay ??= new HueOverlay();
        hueOverlay.Target = S.HueTarget;
        hueOverlay.SetEffect(S.HueDegrees, S.HueSat / 100.0);
    }

    // Names of open apps with a window. Any app that closes while we look is simply skipped.
    static List<string> WindowApps()
    {
        var names = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id != Environment.ProcessId && p.MainWindowHandle != IntPtr.Zero)
                {
                    var n = p.ProcessName;
                    if (!Hidden.Contains(n)) names.Add(n);
                }
            }
            catch { }
            finally { p.Dispose(); }
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    Control BuildHue()
    {
        var page = Ui.Page();
        page.Controls.Add(Ui.Header("Hue changer", "Shift colours on the whole screen, or on just one app."));

        var where = Ui.Card("Where", "Pick “Whole screen”, or an app that's open — like Discord — to change only that app.");
        var apps = WindowApps();
        if (S.HueTarget != "" && !apps.Contains(S.HueTarget, StringComparer.OrdinalIgnoreCase)) apps.Insert(0, S.HueTarget);
        var items = new List<string> { "Whole screen" };
        items.AddRange(apps);
        var target = Ui.Combo(items, 320);
        target.SelectedIndex = S.HueTarget == "" ? 0 : items.FindIndex(x => string.Equals(x, S.HueTarget, StringComparison.OrdinalIgnoreCase));
        target.SelectedChanged += () =>
        {
            S.HueTarget = target.SelectedIndex <= 0 ? "" : target.SelectedItem!;
            S.Save(); ApplyNow(); UpdateHueOverlay();
        };
        where.Controls.Add(target);
        where.Controls.Add(Ui.Btn("Refresh apps", () => Show("Hue changer")));
        where.Controls.Add(Ui.Toggle("Hue changer on", S.HueOn, v => { S.HueOn = v; S.Save(); ApplyNow(); UpdateHueOverlay(); }));
        page.Controls.Add(where);

        var card = Ui.Card("Colour");
        var preview = new HuePreview(Ui.PageW - 40) { Degrees = S.HueDegrees, Sat = S.HueSat };
        var hue = new DarkSlider("Hue shift", -180, 180, S.HueDegrees, 0, 410) { Format = v => $"{v:0}°", Mark = 0 };
        var hs = new DarkSlider("Colour strength", 0, 200, S.HueSat, 100, 410) { Format = v => $"{v:0}%", Mark = 100 };
        void Push()
        {
            preview.Degrees = S.HueDegrees; preview.Sat = S.HueSat; preview.Invalidate();
            if (S.HueTarget == "") Changed(); else hueOverlay?.SetEffect(S.HueDegrees, S.HueSat / 100.0);
            saveDebounce.Stop(); saveDebounce.Start();
        }
        hue.Changed += v => { S.HueDegrees = v; Push(); };
        hs.Changed += v => { S.HueSat = v; Push(); };
        card.Controls.Add(hue); card.Controls.Add(hs); card.Controls.Add(preview);
        var quick = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0) };
        foreach (var (n, d) in new[] { ("Reset", 0), ("Warm", -25), ("Cool", 30), ("Purple", 90), ("Swap", 180) })
        {
            var deg = d;
            quick.Controls.Add(Ui.Btn(n, () => { S.HueDegrees = deg; hue.SetValue(deg); if (deg == 0) { S.HueSat = 100; hs.SetValue(100); } Push(); }));
        }
        card.Controls.Add(quick);
        page.Controls.Add(card);

        string note = S.HueTarget == ""
            ? "Whole screen: changes everything you see, games included."
            : $"Only {S.HueTarget}: a see-through layer follows that window on any monitor, even when you are clicked into something else. It never touches the app itself. Works best with the app in a normal (not exclusive full-screen) window.";
        page.Controls.Add(Ui.Lbl(note, Theme.Body, Theme.Muted, Ui.PageW));
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
        page.Controls.Add(Ui.Header("Crosshair", "A see-through crosshair in the middle of your screen. Works in borderless/windowed games."));

        var card = Ui.Card();
        var preview = new CrosshairPreview(c, 420, 250);
        void Redraw() { preview.Invalidate(); if (overlay != null) { overlay.Recenter(); overlay.Invalidate(); } }
        var controls = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = Theme.Card, WrapContents = false, Margin = new Padding(0) };
        controls.Controls.Add(Ui.Toggle("Show crosshair", c.On, v => { c.On = v; ShowCrosshair(v); S.Save(); }));
        var style = Ui.Combo(new[] { "Cross", "T", "Dot", "Circle" }, 380);
        style.SelectedItem = c.Style;
        style.SelectedChanged += () => { c.Style = style.SelectedItem!; Redraw(); S.Save(); };
        controls.Controls.Add(style);
        void Sl(string t, int min, int max, int val, int def, Action<int> set)
        {
            var s = new DarkSlider(t, min, max, val, def, 380);
            s.Changed += v => { set((int)v); Redraw(); };
            s.Released += () => S.Save();
            controls.Controls.Add(s);
        }
        Sl("Size", 2, 60, c.Size, 10, v => c.Size = v);
        Sl("Thickness", 1, 8, c.Thickness, 2, v => c.Thickness = v);
        Sl("Gap", 0, 20, c.Gap, 3, v => c.Gap = v);

        // Size scale for every crosshair: slider is logarithmic so 0.01× and 50× are both easy to reach.
        static string Fmt(double v) => v < 1 ? $"{v:0.00}×" : v < 10 ? $"{v:0.0#}×" : $"{v:0.#}×";
        var scaleRow = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, WrapContents = false, Margin = new Padding(0) };
        var scale = new DarkSlider("Size scale (all crosshairs)", Math.Log10(CrosshairSettings.MinScale), Math.Log10(CrosshairSettings.MaxScale),
            Math.Log10(Math.Clamp(c.Scale, CrosshairSettings.MinScale, CrosshairSettings.MaxScale)), 0, 270)
        { Step = 0.001, Format = v => Fmt(Math.Pow(10, v)), Mark = 0 };
        var exact = new DarkInput("1.00", 100);
        exact.Box.Text = c.Scale.ToString("0.##");
        void SetScale(double v, bool fromBox)
        {
            v = Math.Round(Math.Clamp(v, CrosshairSettings.MinScale, CrosshairSettings.MaxScale), 2);
            c.Scale = v;
            if (fromBox) scale.SetValue(Math.Log10(v)); else exact.Box.Text = v.ToString("0.##");
            Redraw();
        }
        scale.Changed += v => SetScale(Math.Pow(10, v), false);
        scale.Released += () => S.Save();
        exact.Box.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (double.TryParse(exact.Box.Text.Replace("×", "").Replace("x", "").Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v)) { SetScale(v, true); S.Save(); }
        };
        exact.Box.LostFocus += (_, _) =>
        {
            if (double.TryParse(exact.Box.Text.Replace("×", "").Replace("x", "").Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v)) { SetScale(v, true); S.Save(); }
        };
        exact.Margin = new Padding(4, 12, 4, 4);
        scaleRow.Controls.Add(scale);
        scaleRow.Controls.Add(exact);
        controls.Controls.Add(scaleRow);
        var quick = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, WrapContents = false, Margin = new Padding(0) };
        foreach (var q in new[] { 0.5, 1, 2, 5, 10 })
        {
            var qq = q;
            quick.Controls.Add(Ui.Btn(Fmt(q), () => { SetScale(qq, true); S.Save(); }));
        }
        controls.Controls.Add(quick);
        card.Controls.Add(preview);
        card.Controls.Add(controls);
        page.Controls.Add(card);

        var presets = Ui.Card("Presets", "Tap one to use it. You can still change it with the sliders.");
        foreach (var (pname, pset) in CrosshairSettings.Presets())
        {
            var ps = pset;
            var tile = new CrosshairTile(pname, ps);
            tile.Click += (_, _) => { c.CopyFrom(ps); S.Save(); Show("Crosshair"); };
            presets.Controls.Add(tile);
        }
        page.Controls.Add(presets);

        var extra = Ui.Card("Colour");
        foreach (var col in new[] { Color.FromArgb(0, 255, 102), Color.Red, Color.Cyan, Color.Yellow, Color.Magenta, Color.White, Color.Black })
        {
            var cc = col;
            var b = new Swatch(cc, c.Argb == cc.ToArgb());
            b.Click += (_, _) => { c.Argb = cc.ToArgb(); S.Save(); Show("Crosshair"); };
            extra.Controls.Add(b);
        }
        extra.Controls.Add(Ui.Btn("Pick…", () =>
        {
            using var d = new ColorDialog { Color = Color.FromArgb(c.Argb), FullOpen = true };
            if (d.ShowDialog(this) == DialogResult.OK) { c.Argb = Color.FromArgb(255, d.Color).ToArgb(); S.Save(); Show("Crosshair"); }
        }));
        extra.Controls.Add(Ui.Toggle("Outline", c.Outline, v => { c.Outline = v; Redraw(); S.Save(); }));
        bool whiteEdge = Color.FromArgb(c.OutlineArgb).GetBrightness() > 0.5f;
        extra.Controls.Add(Ui.Toggle("White outline", whiteEdge, v => { c.OutlineArgb = (v ? Color.White : Color.Black).ToArgb(); Redraw(); S.Save(); }));
        var ow = new DarkSlider("Outline thickness", 1, 4, c.OutlineWidth, 1, 260);
        ow.Changed += v => { c.OutlineWidth = (int)v; Redraw(); };
        ow.Released += () => S.Save();
        extra.Controls.Add(ow);
        extra.Controls.Add(Ui.Toggle("Centre dot", c.CenterDot, v => { c.CenterDot = v; Redraw(); S.Save(); }));
        page.Controls.Add(extra);
        overlay?.Invalidate();
        return page;
    }

    // Preset tile: the crosshair on a see-through checkerboard, with its name.
    sealed class CrosshairTile : Control
    {
        readonly string name; readonly CrosshairSettings s; bool hover;
        public CrosshairTile(string n, CrosshairSettings settings)
        {
            name = n; s = settings;
            Size = new Size(132, 112); Margin = new Padding(4); Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Theme.Card);
            var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, 80);
            using (var clip = Gfx.Round(box, 10))
            {
                g.SetClip(clip);
                const int sq = 10;
                using var a = new SolidBrush(Color.FromArgb(58, 60, 66));
                using var b2 = new SolidBrush(Color.FromArgb(44, 46, 51));
                for (int y = 0; y < 82; y += sq)
                    for (int x = 0; x < Width; x += sq)
                        g.FillRectangle(((x / sq + y / sq) & 1) == 0 ? a : b2, x, y, sq, sq);
                g.ResetClip();
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var p = new Pen(hover ? Theme.Accent : Theme.Border, hover ? 2 : 1);
                g.DrawPath(p, clip);
            }
            CrosshairArt.Draw(g, s, Width / 2, 41);
            TextRenderer.DrawText(g, name, Theme.Body, new Rectangle(0, 84, Width, 26), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // Round colour dot button.
    sealed class Swatch : Control
    {
        readonly Color col; readonly bool sel; bool hover;
        public Swatch(Color c, bool selected)
        {
            col = c; sel = selected;
            Size = new Size(36, 36); Margin = new Padding(4); Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Theme.Card);
            if (sel || hover) using (var p = new Pen(sel ? Theme.Accent : Theme.Muted, 2)) g.DrawEllipse(p, 1, 1, Width - 3, Height - 3);
            using var b = new SolidBrush(col);
            g.FillEllipse(b, 6, 6, Width - 13, Height - 13);
        }
    }

    // ---------- Profiles ----------
    static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost", "TextInputHost", "explorer", "SystemSettings", "ShellExperienceHost", "StartMenuExperienceHost",
        "SearchHost", "LockApp", "XboxPcApp", "GameBar", "Widgets", "dwm", "taskmgr", "Syntra",
    };

    Control BuildProfiles()
    {
        var page = Ui.Page();
        page.Controls.Add(Ui.Header("Profiles", "A look for each game — applied when it's in front, put back when you leave it."));

        var pick = Ui.Card("Pick a game", "Open programs. Syntra only looks at the program's name.");
        var combo = Ui.Combo(WindowApps(), 300);
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

    // ---------- Customize ----------
    void LoadBackground()
    {
        Bg.Source?.Dispose();
        Bg.Source = null;
        Bg.Dim = S.BackgroundDim;
        Bg.ClearCache();
        if (S.BackgroundPath == "" || !File.Exists(S.BackgroundPath)) return;
        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(S.BackgroundPath));
            using var img = Image.FromStream(ms);
            Bg.Source = new Bitmap(img);
        }
        catch { }
    }

    void UseTheme(ThemePreset t)
    {
        Theme.Use(t);
        S.AppTheme = Theme.Current.Clone();
        S.Save();
        ApplyThemeColours();
        Show(currentPage);
    }

    Control BuildCustomize()
    {
        var page = Ui.Page();
        page.Controls.Add(Ui.Header("Customize", "Themes and backgrounds for Syntra. Game colour lives on Display."));

        var themes = Ui.Card("Theme", $"Now: {Theme.Current.Name}");
        foreach (var t in Theme.Presets)
        {
            var tt = t;
            var tile = new ThemeTile(t, t.Name == Theme.Current.Name);
            tile.Click += (_, _) => UseTheme(tt);
            themes.Controls.Add(tile);
        }
        page.Controls.Add(themes);

        var own = Ui.Card("Make it yours", "Pick your own accent colour, or share themes as small .json files.");
        own.Controls.Add(Ui.Btn("Accent colour…", () =>
        {
            using var d = new ColorDialog { Color = Theme.Accent, FullOpen = true };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var t = Theme.Current.Clone();
            t.Name = "Custom";
            t.Accent = Theme.Hex(d.Color);
            t.AccentText = d.Color.GetBrightness() > 0.55f ? "#000000" : "#FFFFFF";
            UseTheme(t);
        }));
        own.Controls.Add(Ui.Btn("Panel colour…", () =>
        {
            using var d = new ColorDialog { Color = Theme.Card, FullOpen = true };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var t = Theme.Current.Clone();
            t.Name = "Custom";
            t.Card = Theme.Hex(d.Color);
            t.Hover = Theme.Hex(Theme.Mix(d.Color, Color.White, 0.08));
            t.Border = Theme.Hex(Theme.Mix(d.Color, Color.White, 0.14));
            t.Side = Theme.Hex(Theme.Mix(d.Color, Color.Black, 0.5));
            t.Bg = Theme.Hex(Theme.Mix(d.Color, Color.Black, 0.65));
            UseTheme(t);
        }));
        own.Controls.Add(Ui.Btn("Import theme…", () =>
        {
            using var d = new OpenFileDialog { Filter = "Syntra theme (*.json)|*.json" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try { UseTheme(AppSettings.FromJson<ThemePreset>(File.ReadAllText(d.FileName)) ?? throw new Exception()); }
            catch { MessageBox.Show(this, "That file isn't a Syntra theme.", "Syntra"); }
        }));
        own.Controls.Add(Ui.Btn("Export theme…", () =>
        {
            using var d = new SaveFileDialog { Filter = "Syntra theme (*.json)|*.json", FileName = $"{Theme.Current.Name}.json" };
            if (d.ShowDialog(this) == DialogResult.OK) File.WriteAllText(d.FileName, AppSettings.ToJson(Theme.Current));
        }));
        page.Controls.Add(own);

        var bg = Ui.Card("Background picture", S.BackgroundPath == "" ? "No picture. The window is plain." : "Your picture shows around the panels. Only you see it; nothing is uploaded.");
        bg.Controls.Add(Ui.Btn("Choose PNG / JPG…", () =>
        {
            using var d = new OpenFileDialog { Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                Directory.CreateDirectory(AppSettings.Dir);
                var dest = Path.Combine(AppSettings.Dir, "background" + Path.GetExtension(d.FileName).ToLowerInvariant());
                Bg.Source?.Dispose(); Bg.Source = null;
                File.Copy(d.FileName, dest, true);
                S.BackgroundPath = dest; S.Save();
                LoadBackground(); Invalidate(true); Show("Customize");
            }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't use that picture: " + ex.Message, "Syntra"); }
        }, true));
        if (S.BackgroundPath != "")
            bg.Controls.Add(Ui.Btn("Remove", () => { S.BackgroundPath = ""; S.Save(); LoadBackground(); Invalidate(true); Show("Customize"); }));
        var dim = new DarkSlider("Dim the picture", 0, 90, S.BackgroundDim, 45, 400) { Format = v => $"{v:0}%" };
        dim.Changed += v => { S.BackgroundDim = (int)v; Bg.Dim = (int)v; };
        dim.Released += () => { S.Save(); Invalidate(true); };
        bg.Controls.Add(dim);
        page.Controls.Add(bg);

        var win = Ui.Card("Window");
        var op = new DarkSlider("See-through window", 60, 100, S.WindowOpacity, 100, 400) { Format = v => $"{v:0}%" };
        op.Changed += v => { S.WindowOpacity = (int)v; Opacity = v / 100.0; };
        op.Released += () => S.Save();
        win.Controls.Add(op);
        page.Controls.Add(win);
        return page;
    }

    sealed class ThemeTile : Control
    {
        readonly ThemePreset t; readonly bool sel; bool hover;
        public ThemeTile(ThemePreset preset, bool selected)
        {
            t = preset; sel = selected;
            Size = new Size(200, 92); Margin = new Padding(5); Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        static Color C(string h) { try { return ColorTranslator.FromHtml(h); } catch { return Color.Black; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Theme.Card);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Gfx.Round(r, 12))
            {
                using (var b = new SolidBrush(C(t.Bg))) g.FillPath(b, path);
                using var p = new Pen(sel ? Theme.Accent : hover ? Theme.Muted : Theme.Border, sel ? 2 : 1);
                g.DrawPath(p, path);
            }
            using (var side = new SolidBrush(C(t.Side))) using (var sp = Gfx.Round(new RectangleF(8, 8, 22, Height - 18), 6)) g.FillPath(side, sp);
            using (var ab = new SolidBrush(C(t.Accent))) using (var ap = Gfx.Round(new RectangleF(11, 12, 16, 16), 4)) g.FillPath(ab, ap);
            using (var cb = new SolidBrush(C(t.Card))) using (var cp = Gfx.Round(new RectangleF(36, 8, Width - 46, 38), 6)) g.FillPath(cb, cp);
            using (var ab = new SolidBrush(C(t.Accent))) using (var lp = Gfx.Round(new RectangleF(44, 30, 60, 5), 2.5f)) g.FillPath(ab, lp);
            TextRenderer.DrawText(g, t.Name, Theme.H2, new Point(38, Height - 36), Color.White);
        }
    }

    sealed class PromiseTile : Control
    {
        readonly string title, desc;
        public PromiseTile(string t, string d)
        {
            title = t; desc = d;
            Size = new Size(272, 88); Margin = new Padding(0, 6, 12, 6);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Theme.Card);
            using (var path = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12))
            {
                using var b = new SolidBrush(Theme.Mix(Theme.Card, Theme.Hover, 0.45));
                using var p = new Pen(Theme.Border);
                g.FillPath(b, path); g.DrawPath(p, path);
            }
            // shield badge with a tick
            using (var ab = new SolidBrush(Theme.Accent)) g.FillEllipse(ab, 14, 16, 30, 30);
            using (var tp = new Pen(Theme.AccentText, 2.4f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                g.DrawLines(tp, new[] { new PointF(22, 31), new PointF(27, 36), new PointF(36, 26) });
            TextRenderer.DrawText(g, title, Theme.H2, new Point(56, 16), Theme.Text);
            TextRenderer.DrawText(g, desc, Theme.Body, new Rectangle(56, 40, Width - 66, Height - 44), Theme.Muted, TextFormatFlags.WordBreak);
        }
    }

    // ---------- Settings ----------
    Control BuildSettings()
    {
        var page = Ui.Page();
        page.Controls.Add(Ui.Header("Settings", "Startup, updates, account and safety.  ·  Syntra by Pixy Studios"));

        var gen = Ui.Card();
        gen.Controls.Add(Ui.Toggle("Start Syntra when my PC starts", Startup.IsOn(), v => Startup.Set(v)));
        gen.Controls.Add(Ui.Toggle("Update by itself", S.AutoUpdate, v => { S.AutoUpdate = v; S.Save(); }));
        gen.Controls.Add(Ui.Lbl($"Version {Updater.Current} · {Updater.LastStatus}", Theme.Body, Theme.Muted, Ui.PageW - 40));
        gen.Controls.Add(Ui.Btn("Check for updates now", async () =>
        {
            var tag = await Updater.CheckAsync();
            if (tag != null) Updater.Restart(tray: false); else Show("Settings");
        }));
        gen.Controls.Add(Ui.Lbl("Quick on/off shortcut:  Ctrl + Alt + V", Theme.Body, Theme.Text, Ui.PageW - 40));
        gen.Controls.Add(Ui.Lbl($"NVIDIA driver vibrance: {(ColorEngine.HasDriverVibrance ? "found" : "not found — software saturation is used")}   ·   Running as admin: {(Program.IsAdmin ? "yes" : "no (normal)")}", Theme.Body, Theme.Muted, Ui.PageW - 40));
        gen.Controls.Add(Ui.Btn("Put my screen back to normal", () => { S.Enabled = false; ApplyNow(); UpdateHueOverlay(); S.Save(); }));
        gen.Controls.Add(Ui.Btn("Repair: restart as admin", () => Startup.RestartAsAdmin()));
        gen.Controls.Add(Ui.Btn("Uninstall Syntra", () =>
        {
            if (Installer.Uninstall()) Program.ExitApp();
        }));
        gen.Controls.Add(Ui.Lbl($"Installed at {Installer.Dir}", Theme.Body, Theme.Muted, Ui.PageW - 40));
        page.Controls.Add(gen);

        var web = Ui.Card("Control from anywhere", "Open this on your phone — any Wi-Fi or mobile data — and sign in with your Syntra account.");
        const string ControlUrl = "https://tinyurl.com/2betund3";
        web.Controls.Add(Ui.Lbl("tinyurl.com/2betund3", new Font("Segoe UI Semibold", 18f), Theme.Text));
        web.Controls.Add(Ui.Btn("Open", () => { try { Process.Start(new ProcessStartInfo(ControlUrl) { UseShellExecute = true }); } catch { } }, true));
        web.Controls.Add(Ui.Btn("Copy link", () => Clipboard.SetText(ControlUrl)));
        // QR code: point your phone camera at it
        var qr = new PictureBox { Size = new Size(180, 180), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, Margin = new Padding(4, 8, 16, 4) };
        web.Controls.Add(qr);
        web.Controls.Add(Ui.Lbl("Scan with your phone camera to open it.", Theme.Body, Theme.Muted));
        _ = Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var bytes = await http.GetByteArrayAsync("https://api.qrserver.com/v1/create-qr-code/?size=360x360&margin=12&data=" + Uri.EscapeDataString(ControlUrl));
                var img = Image.FromStream(new MemoryStream(bytes));
                if (!qr.IsDisposed) qr.BeginInvoke(() => { if (!qr.IsDisposed) qr.Image = img; });
            }
            catch { }
        });
        var accNow = Account.Load();
        web.Controls.Add(Ui.Lbl(accNow.CloudUserId.Length > 0 ? "✓  This PC is linked to your account." : "Log out and back in once to link this PC.", Theme.Body, accNow.CloudUserId.Length > 0 ? Theme.Good : Theme.Muted, Ui.PageW - 40));
        page.Controls.Add(web);

        var phone = Ui.Card("Phone control on home Wi-Fi", "Works without the internet. Your phone must be on the same Wi-Fi as this PC.");
        phone.Controls.Add(Ui.Toggle("Phone control on", S.RemoteOn, v => { S.RemoteOn = v; S.Save(); UpdateRemote(); Show("Settings"); }));
        if (S.RemoteOn)
        {
            if (remote == null)
                phone.Controls.Add(Ui.Lbl("Couldn't start: " + remoteError, Theme.Body, Theme.Warn, Ui.PageW - 40));
            else
            {
                phone.Controls.Add(Ui.Lbl("On your phone, open:", Theme.Body, Theme.Muted, Ui.PageW - 40));
                foreach (var url in RemoteServer.Addresses())
                {
                    var u = url;
                    phone.Controls.Add(Ui.Lbl(u, new Font("Segoe UI Semibold", 16f), Theme.Text));
                    phone.Controls.Add(Ui.Btn("Copy", () => Clipboard.SetText(u)));
                }
                phone.Controls.Add(Ui.Lbl("Sign in with your Syntra username and password. If Windows asks about the firewall, allow Private networks.", Theme.Body, Theme.Muted, Ui.PageW - 40));
            }
        }
        page.Controls.Add(phone);

        var acc = Account.Load();
        var a = Ui.Card("Account", acc.Exists ? $"{acc.Name} · @{acc.Username}" : "No account.");
        a.Controls.Add(Ui.Btn("Log out", () =>
        {
            acc.LoggedIn = false; acc.Save();
            Hide();
            using var f = new AccountForm();
            if (f.ShowDialog() != DialogResult.OK) { Program.ExitApp(); return; }
            Show(); Show("Settings");
        }));
        page.Controls.Add(a);

        var promise = Ui.Card("Built to stay clean", "Syntra only adjusts the picture Windows sends to your display. Your games stay untouched.");
        foreach (var (t, d) in new[]
        {
            ("Hands off memory", "Syntra doesn't look inside any running game."),
            ("Game files untouched", "Your game installs stay exactly as they were."),
            ("Nothing hooked", "No code is added to games or to other apps."),
            ("Zero drivers", "Nothing is installed at system level."),
            ("Your own account", "No admin rights. Repair asks Windows before it elevates."),
            ("Official builds", "Every version comes from Pixy Studios. Check yours below."),
        })
            promise.Controls.Add(new PromiseTile(t, d));
        page.Controls.Add(promise);

        var verify = Ui.Card("Is this copy genuine?", "The fingerprint (SHA-256) of the Syntra you are running, checked against the official Pixy Studios release.");
        string hash;
        try { using var fs = File.OpenRead(Environment.ProcessPath!); hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant(); }
        catch { hash = "unavailable"; }
        var hb = new DarkInput("", 600);
        hb.Box.Text = hash; hb.Box.ReadOnly = true; hb.Box.Font = new Font("Consolas", 9.5f);
        verify.Controls.Add(hb);
        verify.Controls.Add(Ui.Btn("Copy", () => Clipboard.SetText(hash)));
        verify.Controls.Add(Ui.Btn("Check on VirusTotal ↗", () =>
        {
            try { Process.Start(new ProcessStartInfo($"https://www.virustotal.com/gui/file/{hash}") { UseShellExecute = true }); } catch { }
        }, true));
        var official = Ui.Lbl("Checking this copy against the official Pixy Studios release…", Theme.Body, Theme.Muted, Ui.PageW - 40);
        verify.Controls.Add(official);
        _ = CheckOfficial(hash, official);
        page.Controls.Add(verify);
        return page;
    }

    // Compares the running file's SHA-256 with the one GitHub Actions published for this version.
    static async Task CheckOfficial(string hash, Label result)
    {
        string text; Color col;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Syntra");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Updater.Repo}/releases/tags/v{Updater.Current}");
            var body = System.Text.Json.Nodes.JsonNode.Parse(json)?["body"]?.ToString() ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(body, "[0-9a-fA-F]{64}");
            if (!m.Success) { text = "No official fingerprint found for this version."; col = Theme.Muted; }
            else if (string.Equals(m.Value, hash, StringComparison.OrdinalIgnoreCase))
            { text = $"✓  Legit — this is the official Syntra v{Updater.Current} from Pixy Studios."; col = Theme.Good; }
            else { text = "✗  This file does NOT match the official Pixy Studios release. Download Syntra again from the official link."; col = Theme.Warn; }
        }
        catch { text = "Couldn't reach the Pixy Studios release server right now."; col = Theme.Muted; }
        if (result.IsDisposed) return;
        result.Text = text; result.ForeColor = col;
    }

    static string? Prompt(string title, string text, string def)
    {
        using var f = new Form
        {
            Text = title, Size = new Size(380, 190), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.Body,
        };
        Ui.DarkTitle(f);
        var l = new Label { Text = text, Location = new Point(16, 14), AutoSize = true };
        var tb = new DarkInput("", 330) { Location = new Point(14, 40) };
        tb.Box.Text = def;
        var ok = new RoundButton { Text = "Save", Primary = true, DialogResult = DialogResult.OK, Location = new Point(250, 92), Size = new Size(94, 38) };
        f.Controls.AddRange(new Control[] { l, tb, ok });
        f.AcceptButton = ok;
        return f.ShowDialog() == DialogResult.OK ? tb.Box.Text : null;
    }
}
