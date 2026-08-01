// alarms.js — panel de alarmas. Consulta las pendientes y permite reconocerlas.
// El estado lo calcula el backend a partir de las marcas de tiempo; aca solo se pinta.

import { WARN, ALARM, CLASS } from "./state.js";

const rowsEl = document.getElementById("alarm-rows");
const countEl = document.getElementById("alarm-count");

// Severidad del backend a la escala compartida con el mimico: misma nomenclatura,
// mismos colores, una sola definicion de "esto esta mal".
const severityClass = (sev) => CLASS[sev === "alarm" ? ALARM : WARN];

const STATE_LABEL = {
  active: "SIN RECONOCER",
  acked: "RECONOCIDA",
  unacked_cleared: "NORMALIZADA S/REC",
  closed: "CERRADA",
};

const time = (iso) =>
  new Date(iso).toLocaleTimeString("es-AR", { hour12: false });

const num = (v) => (v == null ? "—" : v.toFixed(2));

function render(alarms) {
  rowsEl.replaceChildren();

  const unacked = alarms.filter((a) => a.state === "active").length;
  countEl.textContent = alarms.length === 0
    ? "sin alarmas"
    : `${alarms.length} pendiente${alarms.length > 1 ? "s" : ""}` +
      (unacked ? ` · ${unacked} sin reconocer` : "");
  // Solo se pinta el contador si hay algo sin reconocer.
  countEl.classList.toggle("abnormal", unacked > 0);

  for (const a of alarms) {
    const tr = document.createElement("tr");
    tr.className = severityClass(a.severity);
    // Una alarma ya normalizada se atenua: quedo en pantalla solo para que
    // alguien la vea, no porque el proceso siga mal.
    if (a.clearedAt) tr.classList.add("cleared");

    const dir = a.direction === "high" ? "ALTO" : "BAJO";

    for (const text of [
      time(a.raisedAt), a.equipment, a.variable,
      num(a.clearValue ?? a.raiseValue), `${num(a.limitValue)} ${a.unit ?? ""}`,
      `${a.severity === "alarm" ? "ALARMA" : "AVISO"} ${dir}`,
      STATE_LABEL[a.state] ?? a.state,
    ]) {
      const td = document.createElement("td");
      td.textContent = text;
      tr.append(td);
    }

    const tdBtn = document.createElement("td");
    if (!a.ackedAt) {
      const btn = document.createElement("button");
      btn.className = "ack-btn";
      btn.textContent = "REC";
      btn.title = "Reconocer";
      btn.addEventListener("click", () => acknowledge(a.alarmId, btn));
      tdBtn.append(btn);
    }
    tr.append(tdBtn);

    rowsEl.append(tr);
  }
}

async function acknowledge(id, btn) {
  btn.disabled = true;
  try {
    await fetch(`/api/alarms/${id}/ack`, { method: "POST" });
    await refresh();
  } catch {
    btn.disabled = false;   // fallo la red: que se pueda reintentar
  }
}

export async function refresh() {
  try {
    const res = await fetch("/api/alarms");
    render(await res.json());
  } catch {
    // El panel no se cae porque una consulta fallo: el proximo ciclo reintenta.
  }
}

export function startAlarmPanel(intervalMs = 3000) {
  refresh();
  setInterval(refresh, intervalMs);
}