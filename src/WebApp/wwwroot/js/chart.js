// Motor de graficos propio. Canvas 2D, sin dependencias.

const GAP_FACTOR = 2.5;   // salto mayor a esto veces el paso tipico = corte de linea
const MARGIN = { top: 8, right: 10, bottom: 20, left: 52 };

function fitToDisplay(canvas, ctx) {
  const dpr = window.devicePixelRatio || 1;
  const { width, height } = canvas.getBoundingClientRect();
  canvas.width = Math.round(width * dpr);
  canvas.height = Math.round(height * dpr);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  return { width, height };
}

function typicalStep(points) {
  if (points.length < 2) return Infinity;
  const deltas = [];
  for (let i = 1; i < points.length; i++) deltas.push(points[i].t - points[i - 1].t);
  deltas.sort((a, b) => a - b);
  return deltas[deltas.length >> 1];
}

// Paso "lindo": redondea al 1, 2 o 5 mas cercano en potencia de diez,
// para que las marcas caigan en 33,7 / 33,8 y no en 33,712 / 33,754.
function niceStep(rough) {
  const mag = 10 ** Math.floor(Math.log10(rough));
  const norm = rough / mag;
  return (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10) * mag;
}

function ticksFor(min, max, target) {
  const step = niceStep((max - min) / target);
  const out = [];
  for (let v = Math.ceil(min / step) * step; v <= max; v += step) out.push(v);
  return { ticks: out, decimals: Math.max(0, -Math.floor(Math.log10(step))) };
}

const hhmm = (ms) =>
  new Date(ms).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false });

export function createChart(canvas) {
  const ctx = canvas.getContext("2d");
  const css = getComputedStyle(canvas);
  const v = (name, fallback) => css.getPropertyValue(name).trim() || fallback;
  const stroke = v("--trend", "#1c1c1c");
  const gridColor = v("--border", "#8a8a8a");
  const labelColor = v("--text-dim", "#565656");

  function draw(raw, range) {
    const { width, height } = fitToDisplay(canvas, ctx);
    ctx.clearRect(0, 0, width, height);
    if (!raw?.length) return;

    const plotW = width - MARGIN.left - MARGIN.right;
    const plotH = height - MARGIN.top - MARGIN.bottom;
    if (plotW <= 0 || plotH <= 0) return;

    const points = raw.map((p) => ({ t: new Date(p.ts).getTime(), v: p.avg }));

    const t0 = points[0].t, t1 = points[points.length - 1].t;
    let vMin = Infinity, vMax = -Infinity;
    for (const p of points) {
      if (p.v < vMin) vMin = p.v;
      if (p.v > vMax) vMax = p.v;
    }
    const pad = (vMax - vMin) * 0.1 || 1;
    vMin -= pad; vMax += pad;

    // Sin un piso, el autoescalado estira el ruido hasta que parece un terremoto.
    // La ventana nunca baja del 10 % del rango de ingenieria del tag.
    const minSpan = range ? (range.max - range.min) * 0.1 : 0;
    if (vMax - vMin < minSpan) {
      const mid = (vMin + vMax) / 2;
      vMin = mid - minSpan / 2;
      vMax = mid + minSpan / 2;
    }

    // El corazon del asunto: dato -> pixel. En canvas la Y crece hacia abajo.
    const x = (t) => MARGIN.left + ((t - t0) / (t1 - t0 || 1)) * plotW;
    const y = (val) => MARGIN.top + plotH - ((val - vMin) / (vMax - vMin)) * plotH;

    ctx.font = "10px Consolas, monospace";
    ctx.lineWidth = 1;

    // Eje Y: grilla horizontal y valores.
    const { ticks, decimals } = ticksFor(vMin, vMax, 4);
    ctx.textAlign = "right";
    ctx.textBaseline = "middle";
    for (const t of ticks) {
      // El 0.5 alinea la linea al centro del pixel: evita que salga de 2 px borrosos.
      const py = Math.round(y(t)) + 0.5;
      ctx.strokeStyle = gridColor;
      ctx.beginPath();
      ctx.moveTo(MARGIN.left, py);
      ctx.lineTo(MARGIN.left + plotW, py);
      ctx.stroke();
      ctx.fillStyle = labelColor;
      ctx.fillText(t.toFixed(decimals), MARGIN.left - 6, py);
    }

    // Eje X: horas, sin grilla vertical para no ensuciar.
    ctx.textAlign = "center";
    ctx.textBaseline = "top";
    ctx.fillStyle = labelColor;
    const xTicks = 5;
    for (let i = 0; i < xTicks; i++) {
      const t = t0 + ((t1 - t0) * i) / (xTicks - 1);
      const px = Math.min(width - 20, Math.max(20, x(t)));
      ctx.fillText(hhmm(t), px, MARGIN.top + plotH + 5);
    }

    // Marco del area de ploteo.
    ctx.strokeStyle = gridColor;
    ctx.strokeRect(MARGIN.left + 0.5, MARGIN.top + 0.5, plotW, plotH);

    // La serie.
    const maxStep = typicalStep(points) * GAP_FACTOR;
    ctx.strokeStyle = stroke;
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    let pen = false;
    for (let i = 0; i < points.length; i++) {
      const p = points[i];
      // Hueco en los datos: se corta la linea en vez de inventar una recta.
      if (pen && p.t - points[i - 1].t > maxStep) pen = false;
      if (pen) ctx.lineTo(x(p.t), y(p.v));
      else ctx.moveTo(x(p.t), y(p.v));
      pen = true;
    }
    ctx.stroke();
  }

  return { draw };
}