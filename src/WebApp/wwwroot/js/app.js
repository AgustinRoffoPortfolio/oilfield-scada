import { connect } from "./stream.js";
import { createChart } from "./chart.js";

const STATUS = ["STOPPED", "RUNNING", "FAULT"];
const NORMAL_STATUS = "RUNNING";
const STALE_MS = 10000;

// Orden de proceso para las variables conocidas. Una variable que no este en la
// lista va al final: un equipo nuevo aparece igual, sin tocar este archivo.
const VAR_ORDER = ["THP", "CHP", "T_head", "Q_oil", "Q_water", "Q_gas",
                   "ESP_freq", "ESP_current", "ESP_vib",
                   "Sep_P", "Sep_level", "Pipe_P_in", "Pipe_P_out", "Pipe_Q"];

const BIG_CARD_VARS = 6;              // a partir de aca, tarjeta grande
const WINDOWS = [30, 120, 480, 1440]; // minutos ofrecidos en el selector

const varRank = (v) => {
  const i = VAR_ORDER.indexOf(v);
  return i === -1 ? VAR_ORDER.length : i;
};

const winLabel = (m) => (m < 60 ? `${m}m` : `${m / 60}h`);
const decimals = (max) => (max >= 1000 ? 0 : max >= 100 ? 1 : 2);
const el = (tag, cls) => Object.assign(document.createElement(tag), { className: cls });

const nodes = new Map();   // nombre de tag -> elementos a actualizar
let built = false;
let selected = null;       // reading del tag mostrado en el grafico
let windowMin = 30;

function buildTag(reading) {
  const wrap = el("div", "tag");
  const line = el("div", "tag-line");
  const label = el("span", "tag-label");
  label.textContent = reading.variable;
  const value = el("span", "tag-value");
  const unit = el("span", "tag-unit");
  unit.textContent = reading.unit ?? "";
  line.append(label, value, unit);

  const bar = el("div", "tag-bar");
  const fill = el("div", "tag-fill");
  bar.append(fill);
  wrap.append(line, bar);

  wrap.addEventListener("click", () => select(reading));

  nodes.set(reading.name, { wrap, value, fill });
  return wrap;
}

function buildCard(equipment, vars, hasStatus) {
  const card = el("article", "card");
  const head = el("div", "card-head");
  const t = el("span", "card-title");
  t.textContent = equipment;
  const status = el("span", "card-status");
  head.append(t, status);

  const body = el("div", "card-body");
  for (const r of vars) body.append(buildTag(r));

  card.append(head, body);
  if (hasStatus) nodes.set(`${equipment}/Status`, { statusEl: status });
  return card;
}

function build(data) {
  const main = document.getElementById("main-grid");
  const compact = document.getElementById("compact-grid");

  // Los grupos salen del campo equipment, no de una lista escrita a mano.
  for (const eq of [...new Set(data.map((r) => r.equipment))].sort()) {
    const readings = data.filter((r) => r.equipment === eq);
    const vars = readings
      .filter((r) => r.variable !== "Status")
      .sort((a, b) => varRank(a.variable) - varRank(b.variable));
    const hasStatus = readings.some((r) => r.variable === "Status");
    const target = vars.length >= BIG_CARD_VARS ? main : compact;
    target.append(buildCard(eq, vars, hasStatus));
  }

  built = true;
  const first = data.find((r) => r.variable !== "Status");
  if (first) select(first);
}

function update(data) {
  const now = Date.now();
  // Con deadband, un tag que no cambia no reporta: su timestamp envejece sin que el
  // dato sea invalido. Por eso la vejez se evalua sobre el conjunto, no tag por tag.
  const newest = Math.max(...data.map((r) => (r.ts ? new Date(r.ts).getTime() : 0)));
  const stale = now - newest > STALE_MS;

  for (const r of data) {
    if (r.variable === "Status") {
      const node = nodes.get(r.name);
      if (!node?.statusEl) continue;
      const text = STATUS[r.value] ?? "?";
      node.statusEl.textContent = text;
      node.statusEl.classList.toggle("abnormal", text !== NORMAL_STATUS);
      continue;
    }

    const node = nodes.get(r.name);
    if (!node) continue;

    const d = decimals(r.euMax ?? 100);
    node.value.textContent = r.value == null ? "---" : r.value.toFixed(d);

    // Posicion dentro del rango de ingenieria, acotada a 0..100.
    const min = r.euMin ?? 0, max = r.euMax ?? 100;
    const pct = r.value == null ? 0 : ((r.value - min) / (max - min)) * 100;
    node.fill.style.width = `${Math.min(100, Math.max(0, pct))}%`;

    node.wrap.classList.toggle("bad", r.quality !== 0);
    node.wrap.classList.toggle("stale", stale && r.quality === 0);
  }
}

// --- Grafico de tendencia ---

const trend = createChart(document.getElementById("trend-canvas"));
const trendTitle = document.getElementById("trend-title");
let trendData = [];

function select(reading) {
  if (selected) nodes.get(selected.name)?.wrap.classList.remove("selected");
  selected = reading;
  nodes.get(reading.name)?.wrap.classList.add("selected");
  trendTitle.textContent = reading.unit
    ? `${reading.name} [${reading.unit}]`
    : reading.name;
  loadTrend();
}

async function loadTrend() {
  if (!selected) return;
  // encodeURIComponent es obligatorio: el nombre trae "/" (POZO-A/THP).
  const url = `/api/history?tag=${encodeURIComponent(selected.name)}&minutes=${windowMin}`;
  const res = await fetch(url);
  trendData = await res.json();
  redraw();
}

function redraw() {
  if (!selected) return;
  trend.draw(trendData, { min: selected.euMin ?? 0, max: selected.euMax ?? 100 });
}

const winBox = document.getElementById("trend-windows");
for (const m of WINDOWS) {
  const b = el("button", "win-btn");
  b.textContent = winLabel(m);
  b.classList.toggle("active", m === windowMin);
  b.addEventListener("click", () => {
    windowMin = m;
    for (const other of winBox.children) other.classList.remove("active");
    b.classList.add("active");
    loadTrend();
  });
  winBox.append(b);
}

// --- Conexion ---

const statusEl = document.getElementById("conn-status");

connect({
  onData: (data) => {
    if (!built) build(data);
    update(data);
  },
  onStatus: (state) => {
    statusEl.textContent = state === "online" ? "en linea" : "sin conexion";
    statusEl.classList.toggle("lost", state !== "online");
  },
});

setInterval(loadTrend, 10000);
window.addEventListener("resize", redraw);