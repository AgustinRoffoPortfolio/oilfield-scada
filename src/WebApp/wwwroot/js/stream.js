// Conexion SSE. EventSource reconecta solo; el watchdog cubre el caso
// en que el servidor queda colgado sin cerrar el socket.
const STALE_MS = 5000;

export function connect({ onData, onStatus }) {
  let lastMessage = 0;

  const source = new EventSource("/api/stream");

  source.onmessage = (event) => {
    lastMessage = Date.now();
    onStatus("online");
    onData(JSON.parse(event.data));
  };

  source.onerror = () => onStatus("lost");

  setInterval(() => {
    if (lastMessage && Date.now() - lastMessage > STALE_MS) onStatus("lost");
  }, 1000);

  return source;
}