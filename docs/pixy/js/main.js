// Pixy Studios — edit the PROJECTS list to add or change cards.
{
const PROJECTS = [
  { name: "Mini Client", tag: "Out now", text: "A cleaner Minecraft experience — a simple, uncluttered launcher for Minecraft Java Edition.", mark: "M", link: "https://miniclient.pages.dev" },
  { name: "Syntra", tag: "Out now", text: "A colour booster for Windows gaming. Push saturation to 300% in any game.", mark: "S",
    link: "https://raw.githack.com/xomaim/Syntra/main/docs/site/index.html", dark: true },
  { soon: true },
  { soon: true },
];

const $ = id => document.getElementById(id);
const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

// "Welcome" letters rise one by one
$("word").innerHTML = [..."Welcome"].map((c, i) => `<span style="animation-delay:${0.35 + i * 0.06}s">${c}</span>`).join("");

// project cards
$("cards").innerHTML = PROJECTS.map(p => {
  if (p.soon) return `<div class="card soon"><span class="badge">Coming soon</span><h3>Coming soon</h3><p>Something new from Pixy Studios.</p></div>`;
  const tagName = p.link ? "a" : "div";
  const href = p.link ? ` href="${esc(p.link)}"` : "";
  return `<${tagName} class="card live${p.dark ? " dark" : ""}"${href}>
    <span class="badge">${esc(p.tag)}</span><span class="mark">${esc(p.mark)}</span>
    <h3>${esc(p.name)}</h3><p>${esc(p.text)}</p>
    ${p.link ? `<span class="go">Open <i>→</i></span>` : ""}
  </${tagName}>`;
}).join("");

// cards slide in as you reach them
const io = new IntersectionObserver(es => es.forEach(e => { if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); } }), { threshold: .15 });
document.querySelectorAll(".card").forEach((c, i) => { c.style.transitionDelay = (i % 2) * 90 + "ms"; io.observe(c); });

// scrolling: logo shrinks and lifts, "Welcome" fades, top bar slides in
const welcome = $("welcome"), root = document.documentElement, top = $("top");
function onScroll() {
  const r = welcome.getBoundingClientRect();
  const k = Math.min(1, Math.max(0, -r.top / (r.height - innerHeight)));
  root.style.setProperty("--ls", 1 - k * .45);
  root.style.setProperty("--ly", -k * 60 + "px");
  root.style.setProperty("--lo", 1 - k * 1.1);
  root.style.setProperty("--wo", 1 - k * 1.6);
  root.style.setProperty("--wy", -k * 40 + "px");
  top.classList.toggle("show", k > .85);
}
addEventListener("scroll", onScroll, { passive: true });
onScroll();
$("year").textContent = new Date().getFullYear();
}
