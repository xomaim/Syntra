// Syntra web control · Pixy Studios
const $ = id => document.getElementById(id);
const emailOf = u => u.trim().toLowerCase() + "@syntra.user";
const ONLINE_MS = 60_000;
let sb, user, profile, device, devices = [], rev = 0, dragging = 0, pending = {}, timer = null, presetsShown = "";

const FINE = [
  ["vibrance","Vibrance",0,100,1,"%"],["brightness","Brightness",50,150,1,"%"],["contrast","Contrast",50,150,1,"%"],
  ["gamma","Gamma",0.5,2,0.01,""],["temperature","Temperature",-100,100,1,""]
];
const UNITS = { saturation: "%", hueDegrees: "°" };
FINE.forEach(([k, n, min, max, step, u]) => {
  UNITS[k] = u;
  $("fine").insertAdjacentHTML("beforeend",
    `<div class="slider"><div class="row"><span class="name">${n}</span><span class="val" id="v_${k}"></span></div>
     <input type="range" id="${k}" min="${min}" max="${max}" step="${step}"></div>`);
});
const SLIDERS = ["saturation", "hueDegrees", ...FINE.map(f => f[0])];

function esc(s) { return String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])); }
function ago(t) {
  if (!t) return "never";
  const s = (Date.now() - new Date(t)) / 1000;
  if (s < 60) return "just now"; if (s < 3600) return Math.floor(s / 60) + " min ago";
  if (s < 86400) return Math.floor(s / 3600) + " h ago"; return Math.floor(s / 86400) + " d ago";
}
const online = d => d && d.last_seen && Date.now() - new Date(d.last_seen) < ONLINE_MS;

// ---------- sign in ----------
let signUpMode = false;
$("swap").onclick = () => {
  signUpMode = !signUpMode;
  $("name").classList.toggle("hide", !signUpMode);
  $("ltitle").textContent = signUpMode ? "Create your account" : "Sign in to Syntra";
  $("lsub").textContent = signUpMode ? "A name, a username and a password." : "Use the same username and password as the Syntra app.";
  $("lb").textContent = signUpMode ? "Create account" : "Sign in";
  $("swap").textContent = signUpMode ? "Have an account? Sign in" : "New here? Create an account";
  $("p").autocomplete = signUpMode ? "new-password" : "current-password";
  $("le").textContent = "";
};

$("lf").addEventListener("submit", async e => {
  e.preventDefault();
  const u = $("u").value.trim(), p = $("p").value, n = $("name").value.trim();
  $("le").textContent = "";
  if (u.length < 3 || /\s/.test(u)) return $("le").textContent = "Username needs 3+ characters and no spaces.";
  if (p.length < 6) return $("le").textContent = "Password needs at least 6 characters.";
  if (signUpMode && !n) return $("le").textContent = "Enter your name.";
  $("lb").disabled = true; const label = $("lb").textContent; $("lb").textContent = "One moment…";
  const res = signUpMode
    ? await sb.auth.signUp({ email: emailOf(u), password: p, options: { data: { username: u, name: n } } })
    : await sb.auth.signInWithPassword({ email: emailOf(u), password: p });
  $("lb").disabled = false; $("lb").textContent = label;
  if (res.error) {
    $("le").textContent = /invalid login/i.test(res.error.message) ? "Wrong username or password."
      : /already registered/i.test(res.error.message) ? "That username is taken." : res.error.message;
    return;
  }
  if (!res.data.session) { $("le").textContent = "Account made — but email confirmation is on in Supabase. Turn it off (see README)."; return; }
  user = res.data.user;
  intro();
});

// ---------- logo zoom ----------
function intro() {
  $("login").style.display = "none";
  const el = $("intro");
  el.style.display = "flex";
  setTimeout(() => {
    document.body.classList.add("dark");
    document.querySelector("meta[name=theme-color]").content = "#000000";
    $("home").style.display = "block";
    requestAnimationFrame(() => $("home").classList.add("show"));
    el.classList.add("out");
    setTimeout(() => el.style.display = "none", 500);
  }, 1550);
  loadHome();
}

// ---------- home ----------
document.querySelectorAll(".tab").forEach(t => t.onclick = () => {
  document.querySelectorAll(".tab").forEach(x => x.classList.toggle("on", x === t));
  ["control", "pcs", "admin"].forEach(n => $("t_" + n).classList.toggle("hide", n !== t.dataset.tab));
  if (t.dataset.tab === "admin") loadAdmin();
  if (t.dataset.tab === "pcs") renderPcs();
});

async function loadHome() {
  const { data: p } = await sb.from("profiles").select("*").eq("id", user.id).single();
  profile = p || { username: user.user_metadata?.username, name: user.user_metadata?.name };
  $("hi").textContent = "Hi, " + (profile.name || profile.username || "there");
  $("admintab").classList.toggle("hide", !profile.is_admin);
  await refreshDevices();
}

async function refreshDevices() {
  const { data } = await sb.from("devices").select("*").eq("owner", user.id).order("last_seen", { ascending: false });
  devices = data || [];
  if (!device || !devices.find(d => d.id === device.id)) device = devices[0] || null;
  else device = devices.find(d => d.id === device.id);
  $("nopc").classList.toggle("hide", !!device);
  $("controls").classList.toggle("hide", !device);
  if (device) render(device.settings || {});
  const on = online(device);
  $("live").textContent = device ? (on ? "● Live" : "● Offline") : "● No PC";
  $("live").classList.toggle("on", !!on);
  $("sub").textContent = device ? `${device.name || "PC"} · ${on ? "online" : "last seen " + ago(device.last_seen)}` : "No PC linked yet";
  if (!$("t_pcs").classList.contains("hide")) renderPcs();
}

function renderPcs() {
  $("pclist").innerHTML = devices.length ? "" : `<div class="empty"><h3>No PCs yet</h3><p>Sign in to the Syntra app on a PC with this account.</p></div>`;
  devices.forEach(d => {
    const el = document.createElement("div");
    el.className = "item" + (device && d.id === device.id ? " sel" : "");
    el.innerHTML = `<div class="av">🖥</div><div class="t"><b>${esc(d.name || "PC")}</b><small>v${esc(d.version || "?")} · ${online(d) ? "online" : "seen " + ago(d.last_seen)}</small></div><div class="dot ${online(d) ? "on" : ""}"></div>`;
    el.onclick = () => { device = d; rev = 0; refreshDevices(); document.querySelector('[data-tab=control]').click(); };
    $("pclist").appendChild(el);
  });
}

function paint(el) {
  const min = +el.min, max = +el.max, v = +el.value, pct = (v - min) / (max - min) * 100;
  el.style.setProperty("--fill", `linear-gradient(90deg,#fff ${pct}%,#2a2a2e ${pct}%)`);
  $("v_" + el.id).textContent = (el.step === "0.01" ? v.toFixed(2) : Math.round(v)) + (UNITS[el.id] || "");
}

function render(s) {
  if ((s.rev || 0) > rev) rev = s.rev || 0;
  if (!dragging) SLIDERS.forEach(k => { if (s[k] !== undefined) { $(k).value = s[k]; paint($(k)); } });
  $("enabled").classList.toggle("on", s.enabled !== false);
  $("hueOn").classList.toggle("on", !!s.hueOn);
  const names = (s.presets || []).join("|");
  if (names !== presetsShown) {
    presetsShown = names; $("presets").innerHTML = "";
    (s.presets || []).forEach(n => {
      const b = document.createElement("button"); b.className = "chip"; b.textContent = n;
      b.onclick = () => queue("preset", n); $("presets").appendChild(b);
    });
  }
}

function queue(k, v) {
  if (!device) return;
  pending[k] = v;
  if (!timer) timer = setTimeout(send, 120);
}

async function send() {
  timer = null;
  const changes = pending; pending = {};
  rev += 1;
  const settings = { ...(device.settings || {}), ...changes, rev, by: "web", changes };
  device.settings = settings;
  await sb.from("devices").update({ settings, updated_at: new Date().toISOString() }).eq("id", device.id);
}

SLIDERS.forEach(k => {
  const el = $(k);
  el.addEventListener("input", () => { paint(el); queue(k, +el.value); });
  el.addEventListener("pointerdown", () => dragging++);
  el.addEventListener("pointerup", () => setTimeout(() => dragging = Math.max(0, dragging - 1), 600));
});
document.querySelectorAll("[data-sat]").forEach(c => c.onclick = () => { $("saturation").value = c.dataset.sat; paint($("saturation")); queue("saturation", +c.dataset.sat); });
["enabled", "hueOn"].forEach(k => $(k).onclick = () => { const on = !$(k).classList.contains("on"); $(k).classList.toggle("on", on); queue(k, on); });
$("reset").onclick = () => queue("preset", "Balanced");
$("out").onclick = async () => { await sb.auth.signOut(); location.reload(); };

// ---------- admin ----------
let allUsers = [];
async function loadAdmin() {
  const { data, error } = await sb.from("profiles").select("id,username,name,is_admin,created_at,devices(name,version,last_seen)").order("created_at", { ascending: false });
  if (error) { $("userlist").innerHTML = `<div class="empty"><p>${esc(error.message)}</p></div>`; return; }
  allUsers = data || [];
  const pcs = allUsers.flatMap(u => u.devices || []);
  $("s_users").textContent = allUsers.length;
  $("s_pcs").textContent = pcs.length;
  $("s_online").textContent = allUsers.filter(u => (u.devices || []).some(online)).length;
  drawUsers();
}
function drawUsers() {
  const q = $("search").value.trim().toLowerCase();
  $("userlist").innerHTML = "";
  allUsers.filter(u => !q || (u.username + " " + (u.name || "")).toLowerCase().includes(q)).forEach(u => {
    const ds = u.devices || [], on = ds.some(online);
    const last = ds.map(d => d.last_seen).filter(Boolean).sort().pop();
    const ver = ds.map(d => d.version).filter(Boolean).sort().pop();
    const el = document.createElement("div"); el.className = "item";
    el.innerHTML = `<div class="av">${esc((u.name || u.username || "?")[0].toUpperCase())}</div>
      <div class="t"><b>${esc(u.name || "—")} <small>@${esc(u.username)}</small>${u.is_admin ? '<span class="badge">ADMIN</span>' : ""}</b>
      <small>Joined ${new Date(u.created_at).toLocaleDateString()} · ${ds.length} PC${ds.length === 1 ? "" : "s"} · ${ver ? "v" + esc(ver) + " · " : ""}${on ? "online" : "seen " + ago(last)}</small></div>
      <div class="dot ${on ? "on" : ""}"></div>`;
    $("userlist").appendChild(el);
  });
}
$("search").oninput = drawUsers;

// ---------- start ----------
(async () => {
  let cfg;
  try { cfg = await (await fetch("config.json", { cache: "no-store" })).json(); } catch (e) { cfg = {}; }
  if (!cfg.supabaseUrl || cfg.supabaseUrl.startsWith("PASTE")) {
    $("login").style.display = "flex";
    $("le").textContent = "Not set up yet: put your Supabase URL and key in config.json.";
    $("lb").disabled = true; return;
  }
  sb = supabase.createClient(cfg.supabaseUrl, cfg.supabaseAnonKey);
  const { data } = await sb.auth.getSession();
  if (data.session) { user = data.session.user; intro(); }
  else $("login").style.display = "flex";
  setInterval(() => { if (user && !dragging && !timer) refreshDevices(); }, 2000);
  setInterval(() => { if (user && !$("t_admin").classList.contains("hide")) loadAdmin(); }, 15000);
})();
