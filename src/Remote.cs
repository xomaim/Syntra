using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Syntra;

// Phone control over your home Wi-Fi. Syntra serves a small website from this PC;
// sign in with your Syntra username + password and move the sliders from your phone.
sealed class RemoteServer
{
    public const int Port = 7420;
    readonly Func<string> getState;
    readonly Func<string, string> setState;
    readonly HashSet<string> tokens = new();
    readonly Dictionary<string, (int fails, DateTime until)> lockout = new();
    TcpListener? listener;
    CancellationTokenSource? cts;

    public RemoteServer(Func<string> get, Func<string, string> set) { getState = get; setState = set; }

    public void Start()
    {
        listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();
        cts = new CancellationTokenSource();
        _ = Loop(cts.Token);
    }

    public void Stop()
    {
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        listener = null;
    }

    public static List<string> Addresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var a in ni.GetIPProperties().UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254"))
                        list.Add($"http://{a.Address}:{Port}");
            }
        }
        catch { }
        return list.Distinct().ToList();
    }

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await listener!.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Handle(c);
        }
    }

    static int Find(byte[] b, int len, byte[] pat)
    {
        for (int i = 0; i + pat.Length <= len; i++)
        {
            int j = 0;
            while (j < pat.Length && b[i + j] == pat[j]) j++;
            if (j == pat.Length) return i;
        }
        return -1;
    }

    async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                var s = client.GetStream();
                s.ReadTimeout = 10000;
                var buf = new byte[65536];
                int len = 0, headEnd = -1;
                var crlf2 = Encoding.ASCII.GetBytes("\r\n\r\n");
                using var timeout = new CancellationTokenSource(10000);
                while (headEnd < 0)
                {
                    int n = await s.ReadAsync(buf.AsMemory(len, buf.Length - len), timeout.Token);
                    if (n == 0) return;
                    len += n;
                    headEnd = Find(buf, len, crlf2);
                    if (len >= buf.Length) return;
                }
                var lines = Encoding.ASCII.GetString(buf, 0, headEnd).Split("\r\n");
                var first = lines[0].Split(' ');
                if (first.Length < 2) return;
                string method = first[0], path = first[1].Split('?')[0];
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in lines.Skip(1)) { int i = l.IndexOf(':'); if (i > 0) headers[l[..i].Trim()] = l[(i + 1)..].Trim(); }
                int cl = headers.TryGetValue("Content-Length", out var cls) && int.TryParse(cls, out var v) ? Math.Min(v, 32768) : 0;
                int bodyStart = headEnd + 4;
                while (len - bodyStart < cl && len < buf.Length)
                {
                    int n = await s.ReadAsync(buf.AsMemory(len, buf.Length - len), timeout.Token);
                    if (n == 0) break;
                    len += n;
                }
                var body = Encoding.UTF8.GetString(buf, bodyStart, Math.Max(0, Math.Min(cl, len - bodyStart)));
                var ip = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";

                if (method == "GET" && (path == "/" || path == "/index.html")) { await Send(s, 200, "text/html", RemotePage.Html); return; }
                if (method == "POST" && path == "/api/login") { await Login(s, body, ip); return; }

                bool ok = headers.TryGetValue("Authorization", out var auth) && auth.StartsWith("Bearer ") && tokens.Contains(auth[7..]);
                if (!ok) { await Send(s, 401, "application/json", "{\"error\":\"sign in\"}"); return; }
                if (method == "GET" && path == "/api/state") { await Send(s, 200, "application/json", getState()); return; }
                if (method == "POST" && path == "/api/set") { await Send(s, 200, "application/json", setState(body)); return; }
                await Send(s, 404, "text/plain", "Not found");
            }
            catch { }
        }
    }

    async Task Login(Stream s, string body, string ip)
    {
        lock (lockout)
            if (lockout.TryGetValue(ip, out var lo) && lo.until > DateTime.UtcNow)
            { _ = Send(s, 429, "application/json", "{\"error\":\"Too many tries. Wait a minute.\"}"); return; }
        string user = "", pass = "";
        try { var j = JsonNode.Parse(body); user = (string?)j?["username"] ?? ""; pass = (string?)j?["password"] ?? ""; } catch { }
        var acc = Account.Load();
        bool good = acc.Exists && string.Equals(acc.Username, user.Trim(), StringComparison.OrdinalIgnoreCase) && acc.Check(pass);
        if (!good)
        {
            lock (lockout)
            {
                var cur = lockout.TryGetValue(ip, out var x) ? x : (0, DateTime.MinValue);
                int f = cur.Item1 + 1;
                lockout[ip] = (f >= 5 ? 0 : f, f >= 5 ? DateTime.UtcNow.AddSeconds(60) : DateTime.MinValue);
            }
            await Send(s, 401, "application/json", "{\"error\":\"Wrong username or password.\"}");
            return;
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        lock (tokens) tokens.Add(token);
        var res = new JsonObject { ["token"] = token, ["name"] = acc.Name };
        await Send(s, 200, "application/json", res.ToJsonString());
    }

    static async Task Send(Stream s, int code, string type, string body)
    {
        var b = Encoding.UTF8.GetBytes(body);
        var reason = code switch { 200 => "OK", 401 => "Unauthorized", 404 => "Not Found", 429 => "Too Many Requests", _ => "Error" };
        var h = $"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}; charset=utf-8\r\nContent-Length: {b.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(h));
        await s.WriteAsync(b);
        await s.FlushAsync();
    }
}

static class RemotePage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,viewport-fit=cover">
<meta name="theme-color" content="#ffffff">
<title>Syntra</title>
<style>
*{box-sizing:border-box;margin:0;padding:0;-webkit-tap-highlight-color:transparent}
html,body{font-family:"Segoe UI",system-ui,-apple-system,Roboto,sans-serif}
body{background:#fff;color:#111;min-height:100vh;transition:background .5s,color .5s}
body.dark{background:#000;color:#f0f0f2}
.logo{width:76px;height:76px;border-radius:22px;background:#000;color:#fff;display:flex;align-items:center;justify-content:center;font-weight:900;font-size:42px;letter-spacing:-1px}
/* sign in */
#login{min-height:100vh;display:flex;align-items:center;justify-content:center;padding:24px}
#login .box{width:100%;max-width:360px;text-align:center}
#login .logo{margin:0 auto 22px}
#login h1{font-size:26px;font-weight:700}
#login p{color:#777;margin:6px 0 18px;font-size:15px}
#login input{width:100%;padding:15px 16px;border-radius:14px;border:1px solid #e2e2e2;background:#fafafa;font-size:16px;margin-top:10px;outline:none;transition:border .2s,background .2s}
#login input:focus{border-color:#111;background:#fff}
#login button{width:100%;padding:15px;border-radius:14px;border:0;background:#111;color:#fff;font-size:16px;font-weight:600;margin-top:16px;cursor:pointer}
#login button:active{transform:scale(.99)}
.err{color:#e5484d;min-height:20px;margin-top:12px;font-size:14px}
.foot{color:#aaa;font-size:12px;margin-top:22px}
/* intro */
#intro{position:fixed;inset:0;background:#fff;display:none;align-items:center;justify-content:center;z-index:9;overflow:hidden}
#intro .logo{animation:zoom 1.6s cubic-bezier(.65,0,.25,1) forwards}
@keyframes zoom{0%{transform:scale(.4);opacity:0}30%{transform:scale(1);opacity:1}55%{transform:scale(1)}100%{transform:scale(45)}}
#intro.out{animation:fade .45s forwards}
@keyframes fade{to{opacity:0;visibility:hidden}}
/* home */
#home{display:none;max-width:560px;margin:0 auto;padding:22px 16px 48px;opacity:0;transition:opacity .5s}
#home.show{opacity:1}
.top{display:flex;align-items:center;gap:12px;margin-bottom:18px}
.top .logo{width:42px;height:42px;border-radius:12px;background:#fff;color:#000;font-size:23px}
.top h2{font-size:20px;font-weight:600}
.top small{display:block;color:#8e8e93;font-size:13px;font-weight:400}
.top .sp{flex:1}
.live{font-size:13px;font-weight:600;color:#5ad27f}
.live.off{color:#8e8e93}
.card{background:#0d0d0e;border:1px solid #242426;border-radius:20px;padding:18px 18px 14px;margin-bottom:14px}
.card h3{font-size:15px;font-weight:600;margin-bottom:4px}
.row{display:flex;justify-content:space-between;align-items:baseline}
.name{color:#d0d0d5;font-size:15px}
.val{font-weight:600;font-size:16px}
.hero .val{font-size:48px;font-weight:700;letter-spacing:-1px}
.hero .name{color:#8e8e93}
.slider{margin:4px 0 14px}
input[type=range]{-webkit-appearance:none;appearance:none;width:100%;height:30px;background:transparent;margin-top:6px;touch-action:pan-y}
input[type=range]::-webkit-slider-runnable-track{height:6px;border-radius:3px;background:var(--fill)}
input[type=range]::-moz-range-track{height:6px;border-radius:3px;background:var(--fill)}
input[type=range]::-webkit-slider-thumb{-webkit-appearance:none;width:26px;height:26px;border-radius:50%;background:#fff;margin-top:-10px;box-shadow:0 2px 8px rgba(0,0,0,.5)}
input[type=range]::-moz-range-thumb{width:26px;height:26px;border:0;border-radius:50%;background:#fff}
.chips{display:flex;flex-wrap:wrap;gap:8px;margin-top:8px}
.chip{padding:10px 14px;border-radius:12px;background:#17171a;border:1px solid #2a2a2e;color:#e8e8ec;font-size:14px;cursor:pointer}
.chip:active,.chip.on{background:#fff;color:#000;border-color:#fff}
.switch{display:flex;align-items:center;justify-content:space-between;padding:6px 0}
.tg{width:52px;height:30px;border-radius:15px;background:#2a2a2e;position:relative;cursor:pointer;transition:background .2s;flex:none}
.tg::after{content:"";position:absolute;top:4px;left:4px;width:22px;height:22px;border-radius:50%;background:#ccc;transition:all .2s}
.tg.on{background:#fff}.tg.on::after{left:26px;background:#000}
.btn{width:100%;padding:14px;border-radius:14px;border:1px solid #2a2a2e;background:#17171a;color:#eee;font-size:15px;margin-top:4px;cursor:pointer}
.note{color:#6e6e73;font-size:12px;text-align:center;margin-top:18px}
</style>
</head>
<body>
<section id="login">
  <div class="box">
    <div class="logo">S</div>
    <h1>Sign in to Syntra</h1>
    <p>Use the username and password from the Syntra app on your PC.</p>
    <form id="lf" autocomplete="on">
      <input id="u" placeholder="Username" autocomplete="username" autocapitalize="none" spellcheck="false">
      <input id="p" placeholder="Password" type="password" autocomplete="current-password">
      <button type="submit" id="lb">Sign in</button>
    </form>
    <div class="err" id="le"></div>
    <div class="foot">Pixy Studios</div>
  </div>
</section>

<div id="intro"><div class="logo">S</div></div>

<main id="home">
  <div class="top">
    <div class="logo">S</div>
    <div><h2 id="hi">Syntra</h2><small id="sub">Controlling your PC</small></div>
    <div class="sp"></div>
    <div class="live" id="live">● Live</div>
  </div>

  <div class="card hero">
    <div class="row"><span class="name">Saturation</span><span class="val" id="v_saturation">100%</span></div>
    <input type="range" id="saturation" min="0" max="300" step="1">
    <div class="chips">
      <div class="chip" data-sat="100">100%</div><div class="chip" data-sat="150">150%</div>
      <div class="chip" data-sat="200">200%</div><div class="chip" data-sat="250">250%</div><div class="chip" data-sat="300">300%</div>
    </div>
  </div>

  <div class="card">
    <div class="switch"><h3>Colours on</h3><div class="tg" id="enabled"></div></div>
  </div>

  <div class="card" id="fine"><h3>Fine tune</h3></div>

  <div class="card">
    <div class="switch"><h3>Hue changer</h3><div class="tg" id="hueOn"></div></div>
    <div class="slider"><div class="row"><span class="name">Hue shift</span><span class="val" id="v_hueDegrees">0°</span></div>
    <input type="range" id="hueDegrees" min="-180" max="180" step="1"></div>
  </div>

  <div class="card"><h3>Presets</h3><div class="chips" id="presets"></div></div>

  <button class="btn" id="reset">Reset to normal</button>
  <button class="btn" id="out">Sign out</button>
  <div class="note">Your phone and PC need to be on the same Wi-Fi. · Pixy Studios</div>
</main>

<script>
const $ = id => document.getElementById(id);
let token = ""; try { token = localStorage.getItem("syntra_t") || ""; } catch (e) {}
const FINE = [
  ["vibrance","Vibrance",0,100,1,"%"],["brightness","Brightness",50,150,1,"%"],["contrast","Contrast",50,150,1,"%"],
  ["gamma","Gamma",0.5,2,0.01,""],["temperature","Temperature",-100,100,1,""]
];
const UNITS = {saturation:"%",hueDegrees:"°"};
FINE.forEach(([k,n,min,max,step,u]) => {
  UNITS[k] = u;
  $("fine").insertAdjacentHTML("beforeend",
    `<div class="slider"><div class="row"><span class="name">${n}</span><span class="val" id="v_${k}"></span></div>
     <input type="range" id="${k}" min="${min}" max="${max}" step="${step}"></div>`);
});
const SLIDERS = ["saturation","hueDegrees",...FINE.map(f=>f[0])];

async function api(path, body) {
  const r = await fetch(path, { method: body ? "POST" : "GET",
    headers: { "Content-Type": "application/json", "Authorization": "Bearer " + token },
    body: body ? JSON.stringify(body) : undefined });
  if (r.status === 401 && path !== "/api/login") { signOut(); throw new Error("signed out"); }
  const j = await r.json().catch(() => ({}));
  if (!r.ok) throw new Error(j.error || "Something went wrong");
  return j;
}

function paint(el) {
  const min = +el.min, max = +el.max, v = +el.value, pct = (v - min) / (max - min) * 100;
  el.style.setProperty("--fill", `linear-gradient(90deg,#fff ${pct}%,#2a2a2e ${pct}%)`);
  const u = UNITS[el.id] || "";
  $("v_" + el.id).textContent = (el.step === "0.01" ? v.toFixed(2) : Math.round(v)) + u;
}

let dragging = 0, pending = {}, timer = null;
function queue(k, v) {
  pending[k] = v;
  if (!timer) timer = setTimeout(async () => {
    const body = pending; pending = {}; timer = null;
    try { render(await api("/api/set", body), true); } catch (e) {}
  }, 60);
}

SLIDERS.forEach(k => {
  const el = $(k);
  el.addEventListener("input", () => { paint(el); queue(k, +el.value); });
  el.addEventListener("pointerdown", () => dragging++);
  el.addEventListener("pointerup", () => setTimeout(() => dragging = Math.max(0, dragging - 1), 400));
});
document.querySelectorAll("[data-sat]").forEach(c => c.onclick = () => {
  $("saturation").value = c.dataset.sat; paint($("saturation")); queue("saturation", +c.dataset.sat);
});
["enabled","hueOn"].forEach(k => $(k).onclick = () => {
  const on = !$(k).classList.contains("on"); $(k).classList.toggle("on", on); queue(k, on);
});
$("reset").onclick = () => queue("preset", "Default");
$("out").onclick = () => signOut();

let presetsShown = "";
function render(s, fromSet) {
  if (!s || s.saturation === undefined) return;
  if (!dragging || !fromSet) SLIDERS.forEach(k => { if (s[k] !== undefined && !(dragging && fromSet)) { $(k).value = s[k]; paint($(k)); } });
  $("enabled").classList.toggle("on", !!s.enabled);
  $("hueOn").classList.toggle("on", !!s.hueOn);
  $("live").textContent = s.enabled ? "● Live" : "● Off";
  $("live").classList.toggle("off", !s.enabled);
  $("hi").textContent = s.user ? "Hi, " + s.user : "Syntra";
  $("sub").textContent = s.game ? "Game profile: " + s.game : "Controlling your PC · v" + s.version;
  const names = (s.presets || []).join("|");
  if (names !== presetsShown) {
    presetsShown = names;
    $("presets").innerHTML = "";
    (s.presets || []).forEach(n => {
      const d = document.createElement("div"); d.className = "chip"; d.textContent = n;
      d.onclick = () => queue("preset", n);
      $("presets").appendChild(d);
    });
  }
}

async function poll() {
  if (!token || dragging) return;
  try { render(await api("/api/state")); } catch (e) {}
}

function showHome() {
  $("login").style.display = "none";
  const intro = $("intro");
  intro.style.display = "flex";
  setTimeout(() => {
    document.body.classList.add("dark");
    document.querySelector('meta[name=theme-color]').content = "#000000";
    $("home").style.display = "block";
    requestAnimationFrame(() => $("home").classList.add("show"));
    intro.classList.add("out");
    setTimeout(() => intro.style.display = "none", 500);
  }, 1550);
  poll();
}

function signOut() {
  token = ""; try { localStorage.removeItem("syntra_t"); } catch (e) {}
  location.reload();
}

$("lf").addEventListener("submit", async e => {
  e.preventDefault();
  $("le").textContent = ""; $("lb").textContent = "Signing in…";
  try {
    const j = await api("/api/login", { username: $("u").value, password: $("p").value });
    token = j.token; try { localStorage.setItem("syntra_t", token); } catch (e) {}
    showHome();
  } catch (err) { $("le").textContent = err.message; }
  $("lb").textContent = "Sign in";
});

(async () => {
  if (token) { try { render(await api("/api/state")); showHome(); return; } catch (e) {} }
  $("login").style.display = "flex";
})();
setInterval(poll, 1500);
</script>
</body>
</html>
""";
}
