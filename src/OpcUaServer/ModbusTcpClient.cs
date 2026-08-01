using System.Buffers.Binary;
using System.Net.Sockets;

namespace OpcUaServer;

/// Maestro Modbus TCP. Pide registros y nada mas: este driver lee mediciones,
/// no manda ordenes. La contracara exacta del esclavo que corre en el RTU.
public class ModbusTcpClient : IDisposable
{
    private const byte FunctionReadHoldingRegisters = 3;

    private readonly string _host;
    private readonly int _port;
    private readonly byte _unitId;
    private readonly int _timeoutMs;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private ushort _transactionId;

    public ModbusTcpClient(string host, int port, byte unitId, int timeoutMs = 3000)
    {
        _host = host;
        _port = port;
        _unitId = unitId;
        _timeoutMs = timeoutMs;
    }

    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(CancellationToken ct)
    {
        Disconnect();

        var client = new TcpClient
        {
            ReceiveTimeout = _timeoutMs,
            SendTimeout = _timeoutMs,
            // Sin esto, TCP junta paquetes chicos y agrega latencia a cada pedido.
            NoDelay = true
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeoutMs);

            await client.ConnectAsync(_host, _port, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Venció el timeout, no la cancelación del programa. Para el que llama
            // es un fallo de conexión como cualquier otro.
            client.Dispose();
            throw new IOException($"Timeout conectando a {_host}:{_port}");
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _stream = client.GetStream();
    }

    public void Disconnect()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }

    /// Lee un bloque de registros. Devuelve el arreglo o lanza si algo falla:
    /// el que llama decide si eso significa reconectar.
    public async Task<ushort[]> ReadHoldingRegistersAsync(int start, int count, CancellationToken ct)
    {
        if (_stream is null) throw new InvalidOperationException("El driver no esta conectado");

        var transactionId = unchecked(++_transactionId);

        // MBAP (7) + funcion (1) + direccion (2) + cantidad (2).
        var request = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), 6);
        request[6] = _unitId;
        request[7] = FunctionReadHoldingRegisters;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8, 2), (ushort)start);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10, 2), (ushort)count);

        await _stream.WriteAsync(request, ct);

        // Cabecera de la respuesta.
        var header = new byte[8];
        await ReadExactlyAsync(header, ct);

        var responseId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
        if (responseId != transactionId)
            throw new IOException($"Respuesta fuera de orden: esperaba {transactionId}, llego {responseId}");

        var function = header[7];

        if ((function & 0x80) != 0)
        {
            // Respuesta de excepcion: un byte mas con el codigo.
            var code = new byte[1];
            await ReadExactlyAsync(code, ct);
            throw new IOException($"El esclavo rechazo la lectura de {count} registros " +
                                  $"desde {start}: codigo {code[0]}");
        }

        if (function != FunctionReadHoldingRegisters)
            throw new IOException($"Funcion inesperada en la respuesta: {function}");

        // Cantidad de bytes de datos.
        var counter = new byte[1];
        await ReadExactlyAsync(counter, ct);

        var byteCount = counter[0];
        if (byteCount != count * 2)
            throw new IOException($"Esperaba {count * 2} bytes de datos y llegaron {byteCount}");

        var data = new byte[byteCount];
        await ReadExactlyAsync(data, ct);

        var registers = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            registers[i] = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i * 2, 2));
        }

        return registers;
    }

    /// Un socket entrega de a pedazos: pedir N bytes no garantiza que lleguen N.
    /// El timeout es imprescindible: un socket medio abierto no da error nunca,
    /// simplemente no contesta, y el driver se queda esperando para siempre.
    private async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeoutMs);

        var read = 0;
        while (read < buffer.Length)
        {
            int n;
            try
            {
                n = await _stream!.ReadAsync(buffer[read..], timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IOException($"Timeout esperando respuesta del esclavo");
            }

            if (n == 0) throw new IOException("El esclavo cerro la conexion");
            read += n;
        }
    }

    public void Dispose() => Disconnect();
}