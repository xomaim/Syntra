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

// Small terms window (320 × 186). You must accept to use Syntra.
sealed class TermsForm : Form
{
    const string Terms =
        "SYNTRA — TERMS OF USE\r\n\r\n" +
        "1. Syntra changes what your monitor shows: colour, gamma, resolution and an overlay crosshair. It never reads a game's memory, never changes a game's files, never puts code into a game or any other program, and installs no driver.\r\n\r\n" +
        "2. Some games have their own rules about screen tools and overlays. You are responsible for following the rules of the games you play. Any ban risk is yours.\r\n\r\n" +
        "3. Syntra is provided \"as is\", without warranty of any kind. The authors are not liable for any damage, data loss, bans or other problems from using it.\r\n\r\n" +
        "4. Your settings and account stay on this PC. Syntra contacts GitHub only to check for and download updates.\r\n\r\n" +
        "5. Don't resell Syntra or pass off a modified copy as the official one.\r\n\r\n" +
        "6. These terms may change with updates. Using a new version means you accept its terms.";

    public TermsForm()
    {
        Text = "Syntra — Terms";
        ClientSize = new Size(320, 186);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body;
        Ui.DarkTitle(this);

        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = Terms,
            BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 8.5f), Location = new Point(12, 10), Size = new Size(296, 108), TabStop = false,
        };
        var frame = new Panel { Location = new Point(8, 6), Size = new Size(304, 116), BackColor = Theme.Card };
        frame.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawRectangle(p, 0, 0, frame.Width - 1, frame.Height - 1); };
        var tick = new ToggleSwitch("I accept the terms", false) { Location = new Point(6, 130), BackColor = Theme.Bg };
        var accept = new RoundButton { Text = "Accept", Primary = true, Location = new Point(176, 130), Size = new Size(70, 34), Enabled = false };
        var decline = new RoundButton { Text = "Decline", Location = new Point(250, 130), Size = new Size(64, 34) };
        tick.Toggled += on => { accept.Enabled = on; accept.Invalidate(); };
        accept.Click += (_, _) => { if (tick.Checked) { DialogResult = DialogResult.OK; Close(); } };
        decline.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(box);
        Controls.Add(frame);
        Controls.AddRange(new Control[] { tick, accept, decline });
        box.BringToFront();
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
