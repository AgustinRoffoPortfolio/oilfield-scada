// mimic.js — escribe los valores vivos sobre el esquema de proceso.
// No sabe nada de geometría: encuentra cada hueco por data-equipment/data-variable.
// El HTML define DÓNDE va cada cosa; este módulo solo define QUÉ dice.

import { formatValue } from "./format.js";

const mimicRoot = document.getElementById("mimicView");

// Borde interno del recipiente separador, en coordenadas del viewBox del SVG.
const SEP_TOP = 142;
const SEP_BOTTOM = 328;
const SEP_HEIGHT = SEP_BOTTOM - SEP_TOP;

// Cache de nodos: sin esto serían 35 querySelector por segundo, uno por tag.
const slots = new Map();

function slotFor(equipment, variable) {
  const key = `${equipment}/${variable}`;
  if (!slots.has(key)) {
    slots.set(
      key,
      mimicRoot.querySelector(
        `[data-equipment="${equipment}"] [data-variable="${variable}"]`
      )
    );
  }
  return slots.get(key);
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

/**
 * Actualiza el mímico con un snapshot completo de lecturas.
 * Las variables que no tienen lugar en el esquema se ignoran sin ruido.
 */
export function updateMimic(readings) {
  if (!mimicRoot || !Array.isArray(readings)) return;

  for (const r of readings) {
    const slot = slotFor(r.equipment, r.variable);
    if (!slot) continue;

    slot.textContent = formatValue(r.value, r.euMax);

    if (r.variable === "Sep_level" && r.value != null) {
      updateSeparatorLevel(r.value);
    }
  }
}

/** Avisa qué equipo del esquema se clickeó. El handler recibe el nombre. */
export function onEquipmentClick(handler) {
  if (!mimicRoot) return;
  for (const g of mimicRoot.querySelectorAll("[data-equipment]")) {
    g.addEventListener("click", () => handler(g.dataset.equipment));
  }
}