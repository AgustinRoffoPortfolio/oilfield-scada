using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Serilog;

namespace Simulator;

/// Esclavo Modbus TCP. Escucha, atiende lecturas de registros y nada mas.
/// Implementa la funcion 3 (Read Holding Registers), que es todo lo que este
/// RTU necesita: publica mediciones, no recibe ordenes.
public class ModbusTcpSlave
{
    private const byte FunctionReadHoldingRegisters = 3;
    private const byte ExceptionIllegalFunction = 0x01;
    private const byte ExceptionIllegalAddress = 0x02;

    /// Limite del protocolo: la cantidad de bytes de datos entra en un solo byte.
    private const int MaxRegistersPerRead = 125;

    private readonly RegisterTable _table;
    private readonly byte _unitId;
    private readonly int _port;

    private int _connectedMasters;

    /// Cuantos maestros hay conectados ahora. Lo muestra el cuadro del RTU.
    public int ConnectedMasters => Volatile.Read(ref _connectedMasters);

    public ModbusTcpSlave(RegisterTable table, byte unitId, int port)
    {
        _table = table;
        _unitId = unitId;
        _port = port;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        Log.Information("Esclavo Modbus TCP escuchando en el puerto {Port}, unidad {UnitId}",
            _port, _unitId);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                // Cada cliente en su propia tarea: el maestro puede ser mas de uno.
                _ = HandleClientAsync(client, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Salida normal.
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "desconocido";
        Interlocked.Increment(ref _connectedMasters);

        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var header = new byte[7];
                var body = new byte[256];

                while (!ct.IsCancellationRequested)
                {
                    // 1) Cabecera MBAP: transaccion, protocolo, longitud, unidad.
                    if (!await ReadExactlyAsync(stream, header, ct)) break;

                    var transactionId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
                    var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
                    var unitId = header[6];

                    // length cuenta la unidad, que ya leimos.
                    var remaining = length - 1;
                    if (remaining < 1 || remaining > body.Length) break;

                    if (!await ReadExactlyAsync(stream, body.AsMemory(0, remaining), ct)) break;

                    var response = BuildResponse(transactionId, unitId, body.AsSpan(0, remaining));
                    await stream.WriteAsync(response, ct);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
        {
            // El maestro corto la conexion. Es normal, no es un error.
        }
        finally
        {
            Interlocked.Decrement(ref _connectedMasters);
        }
    }

    /// Arma la respuesta a un pedido ya recibido entero.
    private byte[] BuildResponse(ushort transactionId, byte unitId, ReadOnlySpan<byte> request)
    {
        var function = request[0];

        if (function != FunctionReadHoldingRegisters)
            return BuildException(transactionId, unitId, function, ExceptionIllegalFunction);

        if (request.Length < 5)
            return BuildException(transactionId, unitId, function, ExceptionIllegalAddress);

        var start = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(1, 2));
        var count = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(3, 2));

        if (count < 1 || count > MaxRegistersPerRead)
            return BuildException(transactionId, unitId, function, ExceptionIllegalAddress);

        var registers = new ushort[count];
        if (!_table.TryRead(start, count, registers))
            return BuildException(transactionId, unitId, function, ExceptionIllegalAddress);

        // MBAP (7) + funcion (1) + contador (1) + datos (2 por registro).
        var response = new byte[9 + count * 2];

        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0, 2), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), (ushort)(3 + count * 2));
        response[6] = unitId;
        response[7] = function;
        response[8] = (byte)(count * 2);

        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(9 + i * 2, 2), registers[i]);
        }

        return response;
    }

    /// Respuesta de error: la funcion con el bit alto prendido y un codigo.
    private static byte[] BuildException(ushort transactionId, byte unitId, byte function, byte code)
    {
        var response = new byte[9];

        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0, 2), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), 3);
        response[6] = unitId;
        response[7] = (byte)(function | 0x80);
        response[8] = code;

        return response;
    }

    /// Lee exactamente lo que entra en el buffer. Un socket entrega de a pedazos:
    /// pedir 12 bytes no garantiza que lleguen 12 de una.
    private static async Task<bool> ReadExactlyAsync(NetworkStream stream, Memory<byte> buffer,
        CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer[read..], ct);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}