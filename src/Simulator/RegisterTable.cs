using Shared;

namespace Simulator;

/// La memoria del RTU: un array plano de registros de 16 bits.
/// No sabe que significa cada direccion, igual que un equipo real.
/// La escribe el modelo fisico y la lee el socket, en hilos distintos.
public class RegisterTable
{
    private readonly ushort[] _registers;
    private readonly WordOrder _wordOrder;
    private readonly Lock _gate = new();

    public RegisterTable(int size, WordOrder wordOrder)
    {
        _registers = new ushort[size];
        _wordOrder = wordOrder;
    }

    public int Size => _registers.Length;

    /// Escribe un valor float en dos registros consecutivos.
    public void WriteFloat(int address, double value)
    {
        var (first, second) = ModbusFloat.ToRegisters((float)value, _wordOrder);

        lock (_gate)
        {
            _registers[address] = first;
            _registers[address + 1] = second;
        }
    }

    /// Escribe un valor entero en un registro.
    public void WriteUInt16(int address, double value)
    {
        var clamped = Math.Clamp(Math.Round(value), 0, ushort.MaxValue);

        lock (_gate)
        {
            _registers[address] = (ushort)clamped;
        }
    }

    /// Copia un rango de registros. Es lo que responde una lectura Modbus.
    /// Devuelve false si el rango se sale de la tabla: el esclavo tiene que
    /// contestar con excepcion, no reventar.
    public bool TryRead(int start, int count, Span<ushort> destination)
    {
        if (start < 0 || count < 1 || start + count > _registers.Length) return false;

        lock (_gate)
        {
            _registers.AsSpan(start, count).CopyTo(destination);
        }

        return true;
    }
}