// format.js — reglas de presentacion compartidas por mimico, tarjetas y faceplates.

const STATUS = ["STOPPED", "RUNNING", "FAULT"];
export const NORMAL_STATUS = "RUNNING";

export const statusText = (value) => STATUS[value] ?? "?";

// Decimales segun el rango de ingenieria: una vibracion de 0 a 10 necesita dos,
// un caudal de gas de 0 a 25000 no necesita ninguno.
export const decimals = (euMax) => {
  const max = euMax ?? 100;
  return max >= 1000 ? 0 : max >= 100 ? 1 : 2;
};

export const formatValue = (value, euMax) =>
  value == null ? "—" : value.toFixed(decimals(euMax));