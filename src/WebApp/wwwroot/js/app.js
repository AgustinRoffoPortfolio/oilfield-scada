import { connect } from "./stream.js";
import { createChart } from "./chart.js";
import { updateMimic, onEquipmentClick } from "./mimic.js";
import { openFaceplate, updateFaceplates } from "./faceplate.js";

// Orden de proceso para las variables conocidas. Una variable que no este en la
// lista va al final: un equipo nuevo aparece igual, sin tocar este archivo.
const VAR_ORDER = ["THP", "CHP", "T_head", "Q_oil", "Q_water", "Q_gas",
                   "ESP_freq", "ESP_current", "ESP_vib",
                   "Sep_P", "Sep_level", "Pipe_P_in", "Pipe_P_out", "Pipe_Q"];

const WINDOWS = [30, 120, 480, 1440]; // minutos ofrecidos en el selector

const varRank = (v) => {
  const i = VAR_ORDER.indexOf(v);
  return i === -1 ? VAR_ORDER.length : i;
};

const winLabel = (m) => (m < 60 ? `${m}m` : `${m / 60}h`);
const el = (tag, cls) => Object.assign(document.createElement(tag), { className: cls });

let started = false;
let selected = null;       // reading del tag mostrado en el grafico grande
let windowMin = 30;
let latest = [];           // ultimo snapshot, para armar faceplates al vuelo

// --- Grafico de tendencia (nivel 3: investigacion) ---

const trend = createChart(document.getElementById("trend-canvas"));
const trendTitle = document.getElementById("trend-title");
let trendData = [];

function select(reading) {
  selected = reading;
  trendTitle.textContent = reading.unit
    ? `${reading.name} [${reading.unit}]`
    : reading.name;
  trendData = [];
  loadTrend();
}

async function loadTrend() {
  if (!selected) return;
  // encodeURIComponent es obligatorio: el nombre trae "/" (POZO-A/THP).
  const url = `/api/history?tag=${encodeURIComponent(selected.name)}&minutes=${windowMin}`;
  try {
    const res = await fetch(url);
    trendData = await res.json();
    redraw();
  } catch {
    // El dashboard no se cae porque una consulta fallo: el proximo ciclo reintenta.
  }
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

// --- Nivel 2: faceplates ---
// El mimico no se mueve nunca; el detalle se abre encima.

onEquipmentClick((equipment) => {
  const vars = latest
    .filter((r) => r.equipment === equipment)
    .sort((a, b) => varRank(a.variable) - varRank(b.variable));
  if (vars.length) openFaceplate(equipment, vars, select);
});

// --- Conexion ---

const statusEl = document.getElementById("conn-status");

connect({
  onData: (data) => {
    latest = data;
    updateMimic(data);
    updateFaceplates(data);

    if (!started) {
      started = true;
      const first = data.find((r) => r.variable !== "Status");
      if (first) select(first);
    }
  },
  onStatus: (state) => {
    statusEl.textContent = state === "online" ? "en linea" : "sin conexion";
    statusEl.classList.toggle("lost", state !== "online");
  },
});

setInterval(loadTrend, 10000);
window.addEventListener("resize", redraw);