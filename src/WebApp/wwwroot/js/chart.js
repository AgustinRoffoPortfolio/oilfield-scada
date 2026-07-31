// Motor de graficos propio. Canvas 2D, sin dependencias.
// Esta version dibuja solo la linea; ejes y escalas vienen despues.

const GAP_FACTOR = 2.5;   // salto mayor a esto veces el paso tipico = corte de linea

// Nitidez en HiDPI: el canvas se dibuja a la resolucion real del dispositivo,
// pero seguimos escribiendo coordenadas en pixeles CSS.
function fitToDisplay(canvas, ctx) {
  const dpr = window.devicePixelRatio || 1;
  const { width, height } = canvas.getBoundingClientRect();
  canvas.width = Math.round(width * dpr);
  canvas.height = Math.round(height * dpr);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  return { width, height };
}

// Paso tipico entre puntos, para saber que es un hueco y que es cadencia normal.
function typicalStep(points) {
  if (points.length < 2) return Infinity;
  const deltas = [];
  for (let i = 1; i < points.length; i++) deltas.push(points[i].t - points[i - 1].t);
  deltas.sort((a, b) => a - b);
  return deltas[deltas.length >> 1];
}

export function createChart(canvas) {
  const ctx = canvas.getContext("2d");
  const stroke = getComputedStyle(canvas).getPropertyValue("--trend").trim() || "#1c1c1c";

    function draw(raw, range) {
        const { width, height } = fitToDisplay(canvas, ctx);
    ctx.clearRect(0, 0, width, height);
    if (!raw?.length) return;

    const points = raw.map((p) => ({ t: new Date(p.ts).getTime(), v: p.avg }));

    // Dominio: el tiempo cubierto y el rango de valores presentes, con aire.
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
    const x = (t) => ((t - t0) / (t1 - t0 || 1)) * width;
    const y = (v) => height - ((v - vMin) / (vMax - vMin)) * height;

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