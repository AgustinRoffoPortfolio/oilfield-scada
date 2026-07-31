// faceplate.js — ventanas flotantes de detalle por equipo.
// En un SCADA el mimico nunca se mueve: el operador ubica los equipos por
// reflejo. El detalle se abre encima, no reacomoda la pantalla.

import { formatValue, statusText, NORMAL_STATUS } from "./format.js";
import { createChart } from "./chart.js";

const WIDTH = 494;          // 380 + 30%
const OFFSET = 34;         // corrimiento entre ventanas para que no se tapen
const TREND_MIN = 30;       // ventana del mini-grafico, en minutos
const REFRESH_MS = 10000;

const open = new Map();     // equipment -> estado de la ventana
let zTop = 100;
let opened = 0;

const el = (tag, cls) => Object.assign(document.createElement(tag), { className: cls });

const bringToFront = (win) => { win.style.zIndex = ++zTop; };

function buildRow(reading, onClick) {
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

  wrap.addEventListener("click", () => onClick(reading));
  return { wrap, value, fill };
}

// El arrastre se escucha en window, no en la ventana: si el mouse se mueve mas
// rapido que el repintado, el puntero se sale y el drag se cortaria.
function makeDraggable(win, handle) {
  let dx = 0, dy = 0;
  const onMove = (e) => {
    win.style.left = `${e.clientX - dx}px`;
    win.style.top = `${e.clientY - dy}px`;
  };
  const onUp = () => {
    window.removeEventListener("mousemove", onMove);
    window.removeEventListener("mouseup", onUp);
  };
  handle.addEventListener("mousedown", (e) => {
    if (e.target.closest(".fp-close")) return;
    const r = win.getBoundingClientRect();
    dx = e.clientX - r.left;
    dy = e.clientY - r.top;
    bringToFront(win);
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
    e.preventDefault();
  });
}

// --- Mini-tendencia ---

async function loadTrend(fp) {
  if (!fp.selected) return;
  // encodeURIComponent es obligatorio: el nombre trae "/" (POZO-A/THP).
  const url = `/api/history?tag=${encodeURIComponent(fp.selected.name)}` +
              `&minutes=${TREND_MIN}`;
  try {
    const res = await fetch(url);
    fp.trendData = await res.json();
    drawTrend(fp);
  } catch {
    // Una ventana no se rompe porque una consulta fallo: el proximo ciclo reintenta.
  }
}

function drawTrend(fp) {
  if (!fp.selected) return;
  fp.chart.draw(fp.trendData, {
    min: fp.selected.euMin ?? 0,
    max: fp.selected.euMax ?? 100,
  });
}

function selectVar(fp, reading) {
  if (fp.selected) fp.rows.get(fp.selected.name)?.wrap.classList.remove("selected");
  fp.selected = reading;
  fp.rows.get(reading.name)?.wrap.classList.add("selected");
  fp.trendTitle.textContent = reading.unit
    ? `${reading.variable} [${reading.unit}] · ${TREND_MIN}m`
    : `${reading.variable} · ${TREND_MIN}m`;
  fp.trendData = [];
  loadTrend(fp);
}

// --- API del modulo ---

export function closeFaceplate(equipment) {
  const fp = open.get(equipment);
  if (!fp) return;
  fp.win.remove();
  open.delete(equipment);
}

export function openFaceplate(equipment, readings, onSelect) {
  const existing = open.get(equipment);
  if (existing) { bringToFront(existing.win); return; }

  const win = el("section", "faceplate");
  const slot = opened++ % 6;
  win.style.width = `${WIDTH}px`;
  win.style.left = `${60 + slot * OFFSET}px`;
  win.style.top = `${90 + slot * OFFSET}px`;
  bringToFront(win);

  const head = el("header", "fp-head");
  const title = el("span", "fp-title");
  title.textContent = equipment;
  const status = el("span", "fp-status");
  const close = el("button", "fp-close");
  close.textContent = "\u00d7";
  close.title = "Cerrar (Esc)";
  head.append(title, status, close);

  const body = el("div", "fp-body");
  const trendBox = el("div", "fp-trend");
  const trendTitle = el("div", "fp-trend-title");
  const canvas = document.createElement("canvas");
  trendBox.append(trendTitle, canvas);

  const fp = {
    win, rows: new Map(), statusEl: status, trendTitle,
    selected: null, trendData: [], chart: null,
  };

  const vars = readings.filter((r) => r.variable !== "Status");
  for (const r of vars) {
    const row = buildRow(r, (reading) => {
      selectVar(fp, reading);
      onSelect?.(reading);        // el grafico grande de abajo sigue al faceplate
    });
    fp.rows.set(r.name, row);
    body.append(row.wrap);
  }

  win.append(head, body, trendBox);
  document.body.append(win);      // el canvas debe estar en el DOM antes de medirlo
  makeDraggable(win, head);

  close.addEventListener("click", () => closeFaceplate(equipment));
  win.addEventListener("mousedown", () => bringToFront(win));

  fp.chart = createChart(canvas);
  open.set(equipment, fp);

  updateFaceplates(readings);     // pinta valores sin esperar al proximo snapshot
  if (vars.length) selectVar(fp, vars[0]);
}

export function updateFaceplates(readings) {
  if (open.size === 0) return;

  for (const r of readings) {
    const fp = open.get(r.equipment);
    if (!fp) continue;

    if (r.variable === "Status") {
      const text = statusText(r.value);
      fp.statusEl.textContent = text;
      fp.statusEl.classList.toggle("abnormal", text !== NORMAL_STATUS);
      continue;
    }

    const row = fp.rows.get(r.name);
    if (!row) continue;

    row.value.textContent = formatValue(r.value, r.euMax);
    const min = r.euMin ?? 0, max = r.euMax ?? 100;
    const pct = r.value == null ? 0 : ((r.value - min) / (max - min)) * 100;
    row.fill.style.width = `${Math.min(100, Math.max(0, pct))}%`;
    row.wrap.classList.toggle("bad", r.quality !== 0);
  }
}

// Un solo temporizador para todas las ventanas, no uno por ventana: asi no quedan
// intervalos huerfanos cuando se cierra una.
setInterval(() => { for (const fp of open.values()) loadTrend(fp); }, REFRESH_MS);
window.addEventListener("resize", () => { for (const fp of open.values()) drawTrend(fp); });

// Escape cierra la ventana de mas arriba, no todas: cerrar de a una es lo
// esperable cuando hay varias abiertas para comparar.
window.addEventListener("keydown", (e) => {
  if (e.key !== "Escape" || open.size === 0) return;
  let topKey = null, topZ = -1;
  for (const [eq, fp] of open) {
    const z = Number(fp.win.style.zIndex);
    if (z > topZ) { topZ = z; topKey = eq; }
  }
  closeFaceplate(topKey);
});