// mimic.js — escribe los valores vivos sobre el esquema de proceso.
// No sabe nada de geometría: encuentra cada hueco por data-equipment/data-variable.
// El HTML define DÓNDE va cada cosa; este módulo solo define QUÉ dice.

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

// Decimales según el rango de ingeniería: una vibración de 0 a 10 necesita
// dos decimales, un caudal de gas de 0 a 25000 no necesita ninguno.
function formatValue(value, euMax) {
  const max = euMax ?? 100;
  const decimals = max >= 1000 ? 0 : max >= 100 ? 1 : 2;
  return value.toFixed(decimals);
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

    slot.textContent =
      r.value === null || r.value === undefined
        ? "—"
        : formatValue(r.value, r.euMax);

    if (r.variable === "Sep_level" && r.value != null) {
      updateSeparatorLevel(r.value);
    }
  }
}