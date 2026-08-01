using Serilog;
using Shared;

namespace OpcUaServer;

/// Fuente de datos respaldada por un RTU Modbus real, del otro lado de un socket.
/// Lee bloques de registros en su propia tarea y los traduce a nombres de tag
/// segun el mapa del fabricante. El servidor OPC UA no sabe que hay Modbus abajo.
public class ModbusTagSource : ITagValueSource, IDisposable
{
    private readonly ModbusMap _map;
    private readonly ModbusTcpClient _client;
    private readonly int _pollIntervalMs;
    private readonly int _reconnectDelayMs;

    private readonly Dictionary<string, double> _values = new();
    private readonly Lock _gate = new();

    private volatile bool _online;

    public ModbusTagSource(ModbusMap map, string host, int port,
        int pollIntervalMs = 1000, int reconnectDelayMs = 5000)
    {
        _map = map;
        _client = new ModbusTcpClient(host, port, map.UnitId);
        _pollIntervalMs = pollIntervalMs;
        _reconnectDelayMs = reconnectDelayMs;
    }

    /// Si hay comunicacion con el RTU ahora mismo.
    public bool IsOnline => _online;

    /// El ciclo del servidor no hace nada aca: la lectura vive en su propia tarea,
    /// para que un RTU lento no frene la publicacion de los nodos.
    public void Step(double dtSeconds) { }

    public bool TryGetValue(string tagName, out double value)
    {
        if (!_online)
        {
            value = 0;
            return false;
        }

        lock (_gate)
        {
            return _values.TryGetValue(tagName, out value);
        }
    }

    /// Bucle de lectura: conecta, lee bloque por bloque, reconecta si se cae.
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_client.IsConnected)
                {
                    Log.Debug("Intentando conectar al RTU...");
                    await _client.ConnectAsync(ct);
                    _online = true;
                    Log.Information("Driver Modbus conectado al RTU");
                }

                await PollOnceAsync(ct);
                await Task.Delay(_pollIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_online)
                {
                    // Solo se loguea la caida, no cada reintento fallido:
                    // un RTU apagado no tiene que llenar el log de ruido.
                    Log.Warning("Se perdio la comunicacion con el RTU: {Motivo}", ex.Message);
                }

                GoOffline();

                try { await Task.Delay(_reconnectDelayMs, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        GoOffline();
    }

    /// Una pasada completa: un pedido por bloque de equipo.
    private async Task PollOnceAsync(CancellationToken ct)
    {
        foreach (var device in _map.Devices)
        {
            if (device.Registers.Count == 0) continue;

            // Un solo pedido por equipo, cubriendo desde el primer registro
            // hasta el ultimo del bloque.
            var first = device.Registers.Min(r => r.Address);
            var last = device.Registers.Max(r => r.Address + r.Size - 1);
            var count = last - first + 1;

            var registers = await _client.ReadHoldingRegistersAsync(first, count, ct);

            lock (_gate)
            {
                foreach (var entry in device.Registers)
                {
                    var offset = entry.Address - first;
                    var tagName = $"{device.Name}/{entry.Tag}";

                    _values[tagName] = entry.Type == RegisterType.Float32
                        ? ModbusFloat.FromRegisters(registers[offset], registers[offset + 1], _map.WordOrder)
                        : registers[offset];
                }
            }
        }
    }

    /// Marca todo como sin dato. Un valor viejo publicado como bueno es peor
    /// que ningun valor: el operador no tiene forma de saber que esta congelado.
    private void GoOffline()
    {
        _online = false;
        _client.Disconnect();

        lock (_gate)
        {
            _values.Clear();
        }
    }

    public void Dispose() => _client.Dispose();
}