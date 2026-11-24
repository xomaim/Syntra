using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Syntra;

// Account stored on this PC. The password is never saved — only a salted PBKDF2 hash of it.
public class Account
{
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
    public bool LoggedIn { get; set; }
    public DateTime Since { get; set; }

    public bool Exists => Username.Length > 0 && Hash.Length > 0;
    static string FilePath => Path.Combine(AppSettings.Dir, "account.json");

    public static Account Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Account>(File.ReadAllText(FilePath)) ?? new(); } catch { }
        return new();
    }

    public void Save()
    {
        try { Directory.CreateDirectory(AppSettings.Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); } catch { }
    }

    static string HashOf(string password, byte[] salt) =>
        Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 200_000, HashAlgorithmName.SHA256, 32));

    public void SetPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        Salt = Convert.ToBase64String(salt);
        Hash = HashOf(password, salt);
    }

    public bool Check(string password)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(HashOf(password, Convert.FromBase64String(Salt))), Convert.FromBase64String(Hash)); }
        catch { return false; }
    }
}

// Full Terms of Service window. You must read to the end and accept to use Syntra.
sealed class TermsForm : Form
{
    static readonly (string Head, string Body)[] Sections =
    {
        ("1. What Syntra is",
         "Syntra is a Windows app that changes what your monitor shows: colour, saturation, vibrance, gamma, hue, resolution and refresh rate, your monitor's own brightness and contrast, and an on-screen crosshair. By installing, opening or using Syntra you agree to these Terms. If you don't agree, click Decline and don't use it."),
        ("2. What Syntra never does",
         "Syntra never reads a game's memory, never changes a game's files, never puts code into a game or into any other program, and installs no driver. It runs as your own Windows user and needs no admin rights. The optional repair button can restart it as admin, and Windows always asks you first."),
        ("3. Fair play and bans",
         "Every game sets its own rules. Some games don't allow overlays, crosshairs or tools that change how the screen looks. It is your job to check and follow the rules of every game you play. Syntra can't promise that any game allows it, and any warning, suspension or ban is your responsibility."),
        ("4. Your account",
         "Your account (name, username and password) is stored only on this PC. Your password is never saved — only a salted, scrambled check of it. Keep your password to yourself. If you forget it, you can reset Syntra on this PC by deleting its settings folder."),
        ("5. Your data and privacy",
         "Your settings, looks, profiles, themes and background picture stay on your PC in %AppData%\\Syntra. Syntra doesn't sell, share or upload them. It only connects to the internet to check GitHub for updates and download them, and when you choose to check your copy on GitHub or VirusTotal."),
        ("6. Updates",
         "Syntra checks for a newer version about once a minute and installs it automatically, then restarts. You can turn this off in Settings. Updates may add, change or remove features. When an update changes these Terms, you may be asked to accept them again."),
        ("7. Using Syntra properly",
         "Don't sell, rent or redistribute Syntra as your own, don't pass off a modified copy as the official one, and don't use Syntra to break the law or anyone else's rules. Official copies come only from the Syntra GitHub releases page — Settings shows whether your copy is the official one."),
        ("8. Your screen and health",
         "Strong colour, brightness and contrast changes can be uncomfortable. Take breaks. If you are sensitive to flashing or colour changes, use Syntra carefully. Exiting Syntra from the tray, or pressing Ctrl + Alt + V, puts your screen back to normal."),
        ("9. No warranty",
         "Syntra is provided \"as is\" and \"as available\", without any warranty of any kind. It may have bugs, may not work on every PC, monitor or graphics card, and may stop working after Windows or driver updates."),
        ("10. Limit of liability",
         "As far as the law allows, the makers of Syntra are not responsible for any loss or damage from using it or not being able to use it — including bans, lost progress, lost data, or problems with your PC or monitor. Nothing in these Terms removes rights you have by law that can't be removed."),
        ("11. Ending",
         "You can stop using Syntra at any time: exit it from the tray and delete Syntra.exe and the %AppData%\\Syntra folder. If you break these Terms, you are no longer allowed to use Syntra."),
        ("12. Changes to these Terms",
         "These Terms may be updated. The newest version is always shown in the app. Continuing to use Syntra after a change means you accept the new Terms."),
    };

    readonly FlowLayoutPanel flow;
    readonly ToggleSwitch agree;
    readonly RoundButton accept;
    readonly Label hint;
    bool readAll;

    public TermsForm()
    {
        Text = "Syntra — Terms of Service";
        ClientSize = new Size(640, 720);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body;
        Ui.DarkTitle(this);

        // Header
        var header = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Theme.Bg };
        var logo = new LogoTile { Location = new Point(32, 28) };
        var title = new Label { Text = "Terms of Service", Font = Theme.H1, ForeColor = Theme.Text, AutoSize = true, Location = new Point(84, 22), BackColor = Color.Transparent };
        var sub = new Label { Text = "Please read these before using Syntra.   Last updated 24 November 2026", Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(87, 62), BackColor = Color.Transparent };
        header.Controls.AddRange(new Control[] { logo, title, sub });
        header.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 32, header.Height - 1, header.Width - 32, header.Height - 1); };

        // Footer
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 92, BackColor = Theme.Bg };
        footer.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 32, 0, footer.Width - 32, 0); };
        hint = new Label { Text = "Scroll to the end to continue  ↓", Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(32, 14), BackColor = Color.Transparent };
        agree = new ToggleSwitch("I have read and accept the Terms of Service", false) { Location = new Point(28, 42), BackColor = Theme.Bg, Enabled = false };
        accept = new RoundButton { Text = "Accept", Primary = true, Size = new Size(110, 40), Location = new Point(ClientSize.Width - 32 - 110, 38), Enabled = false };
        var decline = new RoundButton { Text = "Decline", Size = new Size(100, 40), Location = new Point(ClientSize.Width - 32 - 110 - 8 - 100, 38) };
        agree.Toggled += on => { if (!readAll) { agree.Checked = false; return; } accept.Enabled = on; accept.Invalidate(); };
        accept.Click += (_, _) => { if (agree.Checked) { DialogResult = DialogResult.OK; Close(); } };
        decline.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        footer.Controls.AddRange(new Control[] { hint, agree, decline, accept });

        // Body: scrolls with the wheel; its scrollbar is pushed out of sight.
        var viewport = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg,
            Padding = new Padding(32, 18, 32, 24), Location = Point.Empty,
        };
        void Fit() => flow.Size = new Size(viewport.ClientSize.Width + SystemInformation.VerticalScrollBarWidth, viewport.ClientSize.Height);
        viewport.Resize += (_, _) => Fit();
        viewport.Controls.Add(flow);

        const int W = 640 - 64;
        flow.Controls.Add(Para("Welcome to Syntra. These Terms are a simple agreement between you and the makers of Syntra about how you can use the app. They're written in plain English — please read them all.", Theme.Text, W, 10.5f, 18));
        foreach (var (head, body) in Sections)
        {
            var h = Ui.Lbl(head, new Font("Segoe UI Semibold", 12.5f), Theme.Text, W);
            h.Margin = new Padding(0, 10, 0, 4);
            flow.Controls.Add(h);
            flow.Controls.Add(Para(body, Theme.Mix(Theme.Text, Theme.Muted, 0.35), W, 10.5f, 12));
        }
        flow.Controls.Add(Para("Thanks for using Syntra.", Theme.Muted, W, 10.5f, 4));

        flow.Scroll += (_, _) => CheckRead();
        flow.MouseWheel += (_, _) => CheckRead();
        foreach (Control c in flow.Controls) c.MouseWheel += (_, _) => BeginInvoke(new Action(CheckRead));

        Controls.Add(viewport);
        Controls.Add(footer);
        Controls.Add(header);
        Shown += (_, _) => { Fit(); flow.Focus(); CheckRead(); };
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            int step = e.KeyCode switch { Keys.Down => 40, Keys.Up => -40, Keys.PageDown => 400, Keys.PageUp => -400, Keys.End => 99999, Keys.Home => -99999, _ => 0 };
            if (step == 0) return;
            flow.AutoScrollPosition = new Point(0, Math.Max(0, -flow.AutoScrollPosition.Y + step));
            CheckRead();
        };
    }

    static Label Para(string text, Color c, int w, float size, int bottom)
    {
        var l = Ui.Lbl(text, new Font("Segoe UI", size), c, w);
        l.Margin = new Padding(0, 0, 0, bottom);
        return l;
    }

    void CheckRead()
    {
        if (readAll) return;
        int bottom = -flow.AutoScrollPosition.Y + flow.ClientSize.Height;
        if (bottom >= flow.DisplayRectangle.Height - 30)
        {
            readAll = true;
            agree.Enabled = true; agree.Invalidate();
            hint.Text = "✓  You've read everything";
            hint.ForeColor = Theme.Good;
        }
    }
}

// Sign up (name, username, password) or log in.
sealed class AccountForm : Form
{
    readonly FlowLayoutPanel body = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        Padding = new Padding(36, 26, 36, 20),
    };
    readonly Label error = Ui.Lbl("", Theme.Body, Theme.Warn, 340);

    public AccountForm()
    {
        Text = "Syntra";
        ClientSize = new Size(424, 520);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg; body.BackColor = Theme.Bg;
        Font = Theme.Body;
        Ui.DarkTitle(this);
        Controls.Add(body);
        if (Account.Load().Exists) LogIn(); else SignUp();
    }

    void Top(string title, string sub, bool signUp)
    {
        body.Controls.Clear();
        error.Text = "";
        body.Controls.Add(new LogoTile { Margin = new Padding(2, 0, 0, 16) });
        // Sign up | Log in switch
        var tabs = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, Margin = new Padding(0, 0, 0, 12), WrapContents = false };
        var a = new RoundButton { Text = "Sign up", Primary = signUp, Size = new Size(170, 36), Margin = new Padding(0, 0, 8, 0) };
        var b = new RoundButton { Text = "Log in", Primary = !signUp, Size = new Size(170, 36), Margin = new Padding(0) };
        a.Click += (_, _) => SignUp();
        b.Click += (_, _) => LogIn();
        tabs.Controls.Add(a); tabs.Controls.Add(b);
        body.Controls.Add(tabs);
        body.Controls.Add(Ui.Lbl(title, Theme.H1, width: 350));
        body.Controls.Add(Ui.Lbl(sub, Theme.Body, Theme.Muted, 350));
        body.Controls.Add(new Label { Height = 6, Width = 10 });
    }

    static RoundButton Wide(string text)
    {
        return new RoundButton { Text = text, Primary = true, Size = new Size(350, 42), Margin = new Padding(4, 8, 4, 4) };
    }

    void SignUp()
    {
        Top("Create your account", "Just a name, a username and a password.", true);
        var name = new DarkInput("Name", 350);
        var user = new DarkInput("Username", 350);
        var pass = new DarkInput("Password", 350);
        pass.Box.UseSystemPasswordChar = true;
        body.Controls.AddRange(new Control[] { name, user, pass });
        var go = Wide("Create account");
        go.Click += (_, _) =>
        {
            var n = name.Box.Text.Trim(); var u = user.Box.Text.Trim(); var p = pass.Box.Text;
            if (n.Length < 1) { error.Text = "Enter your name."; return; }
            if (u.Length < 3 || u.Any(char.IsWhiteSpace)) { error.Text = "Username needs 3+ characters and no spaces."; return; }
            if (p.Length < 6) { error.Text = "Password needs at least 6 characters."; return; }
            var acc = new Account { Name = n, Username = u, LoggedIn = true, Since = DateTime.UtcNow };
            acc.SetPassword(p);
            acc.Save();
            DialogResult = DialogResult.OK; Close();
        };
        body.Controls.Add(go);
        body.Controls.Add(error);
        AcceptButton = go;
        ActiveControl = name.Box;
    }

    void LogIn()
    {
        var existing = Account.Load();
        Top("Welcome back", existing.Exists ? $"Log in as {existing.Name}." : "No account on this PC yet — sign up first.", false);
        var user = new DarkInput("Username", 350);
        var pass = new DarkInput("Password", 350);
        pass.Box.UseSystemPasswordChar = true;
        if (existing.Exists) user.Box.Text = existing.Username;
        body.Controls.AddRange(new Control[] { user, pass });
        var go = Wide("Log in");
        go.Click += (_, _) =>
        {
            var acc = Account.Load();
            if (!acc.Exists) { error.Text = "No account on this PC yet. Sign up first."; return; }
            if (!string.Equals(acc.Username, user.Box.Text.Trim(), StringComparison.OrdinalIgnoreCase) || !acc.Check(pass.Box.Text))
            { error.Text = "Wrong username or password."; pass.Box.Clear(); return; }
            acc.LoggedIn = true; acc.Save();
            DialogResult = DialogResult.OK; Close();
        };
        body.Controls.Add(go);
        body.Controls.Add(error);
        AcceptButton = go;
        ActiveControl = existing.Exists ? pass.Box : user.Box;
    }
}
