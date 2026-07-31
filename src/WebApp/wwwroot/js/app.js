import { connect } from "./stream.js";
import { createChart } from "./chart.js";

const STATUS = ["STOPPED", "RUNNING", "FAULT"];
const NORMAL_STATUS = "RUNNING";
const STALE_MS = 10000;   // sin dato nuevo del sensor por mas de esto = dato viejo

// Orden de proceso, no alfabetico: como lo lee un operador.
const WELL_VARS = ["THP", "CHP", "T_head", "Q_oil", "Q_water", "Q_gas",
                   "ESP_freq", "ESP_current", "ESP_vib"];
const PLANT = {
  Separator: ["Sep_P", "Sep_level"],
  Pipeline: ["Pipe_P_in", "Pipe_P_out", "Pipe_Q"],
};

const decimals = (max) => (max >= 1000 ? 0 : max >= 100 ? 1 : 2);
const el = (tag, cls) => Object.assign(document.createElement(tag), { className: cls });

const nodes = new Map();   // nombre de tag -> elementos a actualizar
let built = false;

function buildTag(name, reading) {
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

  nodes.set(name, { wrap, value, fill });
  return wrap;
}

function buildCard(title, readings, vars) {
  const card = el("article", "card");
  const head = el("div", "card-head");
  const t = el("span", "card-title");
  t.textContent = title;
  const status = el("span", "card-status");
  head.append(t, status);

  const body = el("div", "card-body");
  for (const v of vars) {
    const r = readings.find((x) => x.variable === v);
    if (r) body.append(buildTag(r.name, r));
  }

  card.append(head, body);
  nodes.set(`${title}/Status`, { statusEl: status });
  return card;
}

function build(data) {
  const wells = document.getElementById("wells");
  const plant = document.getElementById("plant");

  const equipments = [...new Set(data.map((r) => r.equipment))];
  for (const eq of equipments.filter((e) => e.startsWith("POZO"))) {
    wells.append(buildCard(eq, data.filter((r) => r.equipment === eq), WELL_VARS));
  }
  for (const [eq, vars] of Object.entries(PLANT)) {
    plant.append(buildCard(eq, data.filter((r) => r.equipment === eq), vars));
  }
  built = true;
}

function update(data) {
    const now = Date.now();
  // Con deadband, un tag que no cambia no reporta: su timestamp envejece sin que el
  // dato sea malo. Por eso la vejez se evalua sobre el conjunto, no tag por tag.
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

    // Anormalidad: calidad no-Good, o el sensor dejo de reportar.
    node.wrap.classList.toggle("bad", r.quality !== 0);
    node.wrap.classList.toggle("stale", stale && r.quality === 0);
  }
}

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

// Andamio del paso 5: un solo trend fijo para verificar el motor de graficos.
const trend = createChart(document.getElementById("trend-canvas"));
let trendData = [];

const TREND_RANGE = { min: 0, max: 60 };   // rango de ingenieria de POZO-A/THP

async function loadTrend() {
  const res = await fetch("/api/history?tag=POZO-A/THP&minutes=30");
  trendData = await res.json();
  trend.draw(trendData, TREND_RANGE);
}

loadTrend();
setInterval(loadTrend, 10000);
window.addEventListener("resize", () => trend.draw(trendData, TREND_RANGE));