// Syntra website · Pixy Studios
// All text and lists come from data/site.json; this file only builds and animates the page.
{
const $ = id => document.getElementById(id);
const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;

const ICON = {
  display: '<circle cx="12" cy="12" r="8"/><path d="M12 4a8 8 0 0 0 0 16z" fill="currentColor"/>',
  hue: '<path d="M12 3c3 4 6 7.5 6 11a6 6 0 0 1-12 0c0-3.5 3-7 6-11z"/>',
  cross: '<circle cx="12" cy="12" r="7"/><path d="M12 2v5M12 17v5M2 12h5M17 12h5"/>',
  res: '<rect x="2" y="5" width="20" height="14" rx="2"/><path d="M7 12V9h3M17 12v3h-3"/>',
  mon: '<rect x="2" y="4" width="20" height="13" rx="2"/><path d="M12 17v3M8 21h8"/>',
  pad: '<rect x="2" y="7" width="20" height="10" rx="5"/><path d="M6 12h4M8 10v4"/><circle cx="16" cy="11" r=".6"/><circle cx="18" cy="13" r=".6"/>',
  phone: '<rect x="7" y="2" width="10" height="20" rx="2.5"/><path d="M11 18h2"/>',
  brush: '<rect x="3" y="3" width="18" height="18" rx="3"/><path d="M9 3v18M9 11h12"/>',
  shield: '<path d="M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z"/><path d="M8.5 12l2.5 2.5 4.5-5"/>'
};
const tick = '<svg viewBox="0 0 24 24"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>';
const fill = el => { const p = (el.value - el.min) / (el.max - el.min) * 100; el.style.setProperty("--fill", `linear-gradient(90deg,#fff ${p}%,#2a2a2e ${p}%)`); };

async function load() {
  let d;
  try { d = await (await fetch("data/site.json", { cache: "no-cache" })).json(); }
  catch (e) { console.warn("site.json missing", e); return; }
  build(d);
}

function build(d) {
  // links
  document.querySelectorAll("[data-link]").forEach(a => { const u = d[a.dataset.link]; if (u) a.href = u; });

  // hero text
  $("badge").innerHTML = `<b>${esc(d.badge.tag)}</b>${esc(d.badge.text)}`;
  $("headline").innerHTML = d.hero.lines.map(l => `<span>${esc(l)}</span>`).join("")
    + `<span class="rot">${(d.hero.rotate || []).map((w, i) => `<b class="${i ? "" : "on"}">${esc(w)}</b>`).join("")}</span>`;
  $("note").textContent = d.hero.note || "";
  const rot = [...document.querySelectorAll("#headline .rot b")];
  if (rot.length > 1 && !reduce) {
    let i = 0;
    setInterval(() => {
      const cur = rot[i]; i = (i + 1) % rot.length; const nxt = rot[i];
      cur.classList.remove("on"); cur.classList.add("out");
      nxt.classList.remove("out"); void nxt.offsetWidth; nxt.classList.add("on");
      setTimeout(() => cur.classList.remove("out"), 800);
    }, 2600);
  }
  $("lead").textContent = d.hero.lead;
  $("meta").innerHTML = d.hero.meta.map(esc).join("<i>·</i>");

  // marquee (doubled for a seamless loop)
  const m = d.marquee.map(t => `<span>${esc(t)}</span>`).join("");
  $("marquee").innerHTML = m + m;

  // presets for the before/after
  const P = d.presets;
  $("presets").innerHTML = P.map((p, i) => `<button class="preset${i === 1 ? " on" : ""}" data-i="${i}">
    <img src="img/valley-thumb.jpg" alt="" style="filter:saturate(${p.sat / 100}) hue-rotate(${p.hue}deg)"><b>${esc(p.name)}</b><small>${p.sat}%</small></button>`).join("");
  const cmp = $("cmp");
  function usePreset(i) {
    const p = P[i];
    cmp.style.setProperty("--csat", p.sat / 100);
    cmp.style.setProperty("--chue", p.hue + "deg");
    $("cmptag").textContent = `Syntra · ${p.name} ${p.sat}%`;
    document.querySelectorAll(".preset").forEach((b, j) => b.classList.toggle("on", j === i));
  }
  document.querySelectorAll(".preset").forEach(b => b.onclick = () => usePreset(+b.dataset.i));
  usePreset(1);

  // drag the split
  let dragging = false;
  const setCut = x => { const r = cmp.getBoundingClientRect(); cmp.style.setProperty("--cut", Math.min(98, Math.max(2, (x - r.left) / r.width * 100)) + "%"); };
  cmp.addEventListener("pointerdown", e => { dragging = true; cmp.setPointerCapture(e.pointerId); setCut(e.clientX); });
  cmp.addEventListener("pointermove", e => { if (dragging) setCut(e.clientX); });
  cmp.addEventListener("pointerup", () => dragging = false);
  // a little intro sweep when it first scrolls into view
  if (!reduce) {
    const once = new IntersectionObserver(es => es.forEach(e => {
      if (!e.isIntersecting) return; once.disconnect();
      const t0 = performance.now();
      const step = t => { const k = Math.min(1, (t - t0) / 1600); const v = 50 + Math.sin(k * Math.PI * 2) * 30 * (1 - k); cmp.style.setProperty("--cut", v + "%"); if (k < 1 && !dragging) requestAnimationFrame(step); };
      requestAnimationFrame(step);
    }), { threshold: .5 });
    once.observe(cmp);
  }

  // stats with count-up
  $("stats").innerHTML = d.stats.map(s => `<div class="stat rv"><b data-to="${s.value}" data-s="${esc(s.suffix)}">0${esc(s.suffix)}</b><span>${esc(s.label)}</span></div>`).join("");
  const counter = new IntersectionObserver(es => es.forEach(e => {
    if (!e.isIntersecting) return; counter.unobserve(e.target);
    const b = e.target.querySelector("b"), to = +b.dataset.to, suf = b.dataset.s, t0 = performance.now();
    const step = t => { const k = Math.min(1, (t - t0) / 1300), v = Math.round(to * (1 - Math.pow(1 - k, 3))); b.textContent = v + suf; if (k < 1) requestAnimationFrame(step); };
    reduce ? (b.textContent = to + suf) : requestAnimationFrame(step);
  }), { threshold: .6 });
  document.querySelectorAll(".stat").forEach(s => counter.observe(s));

  // features
  $("words").innerHTML = d.manifesto.split(" ").map(w => `<span>${esc(w)} </span>`).join("");
  $("grid").innerHTML = d.features.map((f, i) => `<div class="f"><div class="ic"><svg viewBox="0 0 24 24">${ICON[f.icon] || ""}</svg></div><h3>${esc(f.title)}</h3><p>${esc(f.text)}</p><div class="num">${String(i + 1).padStart(2, "0")}</div></div>`).join("");
  stories();
  document.querySelectorAll(".f").forEach(card => card.addEventListener("pointermove", e => {
    const r = card.getBoundingClientRect();
    card.style.setProperty("--fx", (e.clientX - r.left) + "px"); card.style.setProperty("--fy", (e.clientY - r.top) + "px");
  }));

  // safe + steps + faq
  $("safelist").innerHTML = d.safe.map(s => `<div class="li"><div class="tick">${tick}</div><div><b>${esc(s.title)}</b><span>${esc(s.text)}</span></div></div>`).join("");
  $("steps").innerHTML = d.steps.map(s => `<li>${esc(s)}</li>`).join("");
  $("faqlist").innerHTML = d.faq.map(f => `<details class="rv"><summary>${esc(f.q)}</summary><p>${esc(f.a)}</p></details>`).join("");

  buildApp(P);
  reveal();
}

// ---------- hero slider: every picture on the page follows it ----------
const hs = $("hsat");
function hero(v) {
  hs.value = v; fill(hs);
  document.documentElement.style.setProperty("--sat", v / 100);
  $("hsatv").textContent = v === 100 ? "" : v + "%";
}
hs.addEventListener("input", () => hero(+hs.value));
hero(100);

// ---------- in-browser app ----------
function buildApp(P) {
  const SIDE = [["display","Display"],["res","Resolution"],["mon","Monitor"],["hue","Hue changer"],["cross","Crosshair"],["pad","Profiles"],["brush","Customize"]];
  $("aside").innerHTML = '<img src="img/logo.png" alt="">' + SIDE.map(([ic, t], i) => `<button title="${t}" aria-label="${t}" class="${i ? "" : "on"}"><svg viewBox="0 0 24 24">${ICON[ic]}</svg></button>`).join("");
  $("aside").querySelectorAll("button").forEach(b => b.onclick = () => $("aside").querySelectorAll("button").forEach(x => x.classList.toggle("on", x === b)));

  const L = { sat: 100, vib: 50, bri: 100, con: 100, temp: 0, hue: 0 };
  const FINE = [["bri","Brightness",50,150,"%"],["con","Contrast",50,150,"%"],["temp","Temperature",-100,100,""],["hue","Hue shift",-180,180,"°"]];
  $("afine").innerHTML = FINE.map(([k, n, min, max]) => `<div><label>${n} <span id="v_${k}"></span></label><input type="range" id="f_${k}" min="${min}" max="${max}" value="${L[k]}" aria-label="${n}"></div>`).join("");
  $("ptiles").innerHTML = P.map((p, i) => `<button class="pt${i ? "" : " on"}" data-i="${i}"><img src="img/valley-thumb.jpg" alt="" style="filter:saturate(${p.sat / 100})"><b>${esc(p.name)}</b><small>${p.sat}%</small></button>`).join("");

  function draw() {
    const s = L.sat / 100 * (L.vib >= 50 ? 1 + (L.vib - 50) / 50 * .6 : L.vib / 50);
    const f = `saturate(${s}) brightness(${L.bri / 100}) contrast(${L.con / 100}) hue-rotate(${L.hue + (L.temp < 0 ? L.temp / 6 : 0)}deg) sepia(${L.temp > 0 ? L.temp / 260 : 0})`;
    $("ashot").style.filter = f;
    $("phshot").style.filter = f;
    $("asatv").textContent = L.sat; $("avibv").textContent = L.vib;
    $("asat").value = L.sat; $("avib").value = L.vib; fill($("asat")); fill($("avib"));
    FINE.forEach(([k, , , , u]) => { const el = $("f_" + k); el.value = L[k]; fill(el); $("v_" + k).textContent = L[k] + u; });
    $("phv").textContent = L.sat + "%"; $("phbar").style.setProperty("--w", (L.sat / 3) + "%");
  }
  const mark = i => document.querySelectorAll(".pt").forEach((p, j) => p.classList.toggle("on", j === i));
  $("asat").oninput = e => { L.sat = +e.target.value; mark(-1); draw(); };
  $("avib").oninput = e => { L.vib = +e.target.value; mark(-1); draw(); };
  FINE.forEach(([k]) => $("f_" + k).oninput = e => { L[k] = +e.target.value; mark(-1); draw(); });
  document.querySelectorAll(".pt").forEach(p => p.onclick = () => { const x = P[+p.dataset.i]; Object.assign(L, { sat: x.sat, hue: x.hue, vib: 50, bri: 100, con: 100, temp: 0 }); mark(+p.dataset.i); draw(); });
  $("areset").onclick = () => { Object.assign(L, { sat: 100, vib: 50, bri: 100, con: 100, temp: 0, hue: 0 }); mark(0); draw(); };
  draw();
}

// ---------- motion: reveal, parallax, tilt, spotlight, hide nav ----------
function reveal() {
  const io = new IntersectionObserver(es => es.forEach(e => { if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); } }), { threshold: .12 });
  document.querySelectorAll(".rv").forEach((el, i) => { el.style.transitionDelay = (i % 3) * 80 + "ms"; io.observe(el); });
}
// progress (0..1) of a tall pinned section
const prog = el => { const r = el.getBoundingClientRect(); return Math.min(1, Math.max(0, -r.top / (r.height - innerHeight))); };
function stories() {
  const st = $("story");
  if (st) {
    const k = prog(st), v = Math.round(k * 300);
    st.style.setProperty("--k", k); st.style.setProperty("--s", v / 100);
    $("storyn").textContent = v + "%";
    $("storyl").textContent = v < 15 ? "Scroll to turn Syntra up" : v < 100 ? "Black & white…" : v < 110 ? "Untouched" : v < 250 ? "Syntra on" : "Full Syntra";
  }
  const rail = $("features"), track = $("grid");
  if (rail && track) {
    const k = prog(rail), max = Math.max(0, track.scrollWidth - innerWidth);
    rail.style.setProperty("--k", k); track.style.setProperty("--tx", (-k * max) + "px");
  }
  const words = document.querySelectorAll("#words span");
  if (words.length) {
    const r = $("manifesto").getBoundingClientRect();
    const k = Math.min(1, Math.max(0, (innerHeight * .85 - r.top) / (r.height + innerHeight * .2)));
    const n = Math.round(k * words.length);
    words.forEach((w, i) => w.classList.toggle("on", i < n));
  }
}
let lastY = 0;
addEventListener("resize", stories);
addEventListener("scroll", () => {
  stories();
  const y = scrollY;
  if (!reduce) {
    document.querySelector(".hero .bg").style.setProperty("--py", y * .35 + "px");
    const city = $("phone"), r = city.getBoundingClientRect();
    city.querySelector(".bg").style.setProperty("--py2", (r.top * -.15) + "px");
    const w = $("appwin"), wr = w.getBoundingClientRect();
    const k = Math.min(1, Math.max(0, 1 - (wr.top - innerHeight * .25) / (innerHeight * .6)));
    w.style.setProperty("--rx", (8 * (1 - k)) + "deg");
  }
  $("nav").classList.toggle("hide", y > 400 && y > lastY);
  lastY = y;
}, { passive: true });
addEventListener("pointermove", e => {
  document.documentElement.style.setProperty("--mx", e.clientX + "px");
  document.documentElement.style.setProperty("--my", e.clientY + "px");
}, { passive: true });

load();
}
