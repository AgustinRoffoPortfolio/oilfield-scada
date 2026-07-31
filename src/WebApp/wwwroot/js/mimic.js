// mimic.js — escribe los valores vivos sobre el esquema de proceso.
// No sabe nada de geometría: encuentra cada hueco por data-equipment/data-variable.
// El HTML define DÓNDE va cada cosa; este módulo solo define QUÉ dice.

import { formatValue } from "./format.js";
import { readingState, equipmentState, CLASS, OK } from "./state.js";

const mimicRoot = document.getElementById("mimicView");
const STALE_MS = 10000;

// Borde interno del recipiente separador, en coordenadas del viewBox del SVG.
const SEP_TOP = 142;
const SEP_BOTTOM = 328;
const SEP_HEIGHT = SEP_BOTTOM - SEP_TOP;

const slots = new Map(); // cache de nodos: sin esto, 35 querySelector por segundo
const groups = new Map();

function slotFor(equipment, variable) {
  const key = `${equipment}/${variable}`;
  if (!slots.has(key)) {
    slots.set(
      key,
      mimicRoot.querySelector(
        `[data-equipment="${equipment}"] [data-variable="${variable}"]`,
      ),
    );
  }
  return slots.get(key);
}

function groupFor(equipment) {
  if (!groups.has(equipment)) {
    groups.set(
      equipment,
      mimicRoot.querySelector(`[data-equipment="${equipment}"]`),
    );
  }
  return groups.get(equipment);
}

// Reemplaza la clase de estado sin tocar las demas (dimmed, etc).
function setState(node, state) {
  if (!node) return;
  node.classList.remove("warn", "alarm", "bad");
  if (state !== OK) node.classList.add(CLASS[state]);
}

function updateSeparatorLevel(percent) {
  const fill = document.getElementById("sepLevelFill");
  const line = document.getElementById("sepLevelLine");
  if (!fill || !line) return;

  const frac = Math.min(1, Math.max(0, percent / 100));
  const height = SEP_HEIGHT * frac;
  const top = SEP_BOTTOM - height;

  fill.setAttribute("y", top);
  fill.setAttribute("height", height);
  line.setAttribute("y1", top);
  line.setAttribute("y2", top);
}

/** Actualiza valores y estados. Las variables sin lugar en el esquema se ignoran. */
export function updateMimic(readings) {
  if (!mimicRoot || !Array.isArray(readings)) return;

  // Con deadband, un tag que no cambia deja de reportar y su timestamp envejece sin
  // que el dato sea malo. Lo que indica corte de la cadena es que NINGUNO reporte.
  const newest = Math.max(
    ...readings.map((r) => (r.ts ? new Date(r.ts).getTime() : 0)),
  );
  const stale = Date.now() - newest > STALE_MS;

  const byEquipment = new Map();

  for (const r of readings) {
    if (!byEquipment.has(r.equipment)) byEquipment.set(r.equipment, []);
    byEquipment.get(r.equipment).push(r);

    const slot = slotFor(r.equipment, r.variable);
    if (!slot) continue;

    slot.textContent = formatValue(r.value, r.euMax);
    setState(slot, readingState(r));

    if (r.variable === "Sep_level" && r.value != null)
      updateSeparatorLevel(r.value);
  }

  for (const [equipment, rs] of byEquipment) {
    setState(groupFor(equipment), equipmentState(rs, stale));
  }
}

/** Avisa qué equipo del esquema se clickeó. El handler recibe el nombre. */
export function onEquipmentClick(handler) {
  if (!mimicRoot) return;
  for (const g of mimicRoot.querySelectorAll("[data-equipment]")) {
    g.addEventListener("click", () => handler(g.dataset.equipment));
  }
}
