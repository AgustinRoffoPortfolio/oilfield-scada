// state.js — clasifica una lectura contra sus umbrales de catalogo.
// Una sola definicion de "esto esta mal", compartida por mimico y faceplates.

export const OK = 0;
export const WARN = 1;
export const ALARM = 2;
export const BAD = 3;      // dato invalido o comunicacion caida

// Prioridad: lo peor gana. Un equipo se pinta por su variable mas comprometida.
export const worst = (a, b) => (a > b ? a : b);

export const CLASS = { [OK]: "", [WARN]: "warn", [ALARM]: "alarm", [BAD]: "bad" };

/**
 * Estado de una lectura. Un umbral en null significa "sin limite de ese lado":
 * la vibracion no tiene minimo, cuanto menos mejor.
 */
export function readingState(r) {
  if (r.value == null || r.quality !== 0) return BAD;

  const { value, warnLow, warnHigh, alarmLow, alarmHigh } = r;

  if ((alarmLow != null && value < alarmLow) ||
      (alarmHigh != null && value > alarmHigh)) return ALARM;

  if ((warnLow != null && value < warnLow) ||
      (warnHigh != null && value > warnHigh)) return WARN;

  return OK;
}

/** Estado de un equipo: el peor de sus variables. Status en FAULT manda a ALARM. */
export function equipmentState(readings, stale) {
  if (stale) return BAD;

  let s = OK;
  for (const r of readings) {
    if (r.variable === "Status") {
      if (r.value === 2) s = worst(s, ALARM);   // 2 = FAULT
      continue;
    }
    s = worst(s, readingState(r));
  }
  return s;
}