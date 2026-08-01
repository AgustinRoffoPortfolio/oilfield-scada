namespace Shared;

/// Orden en que se acomodan las dos palabras de 16 bits de un float de 32.
/// Modbus define big-endian dentro de cada registro, pero cual de las dos
/// palabras va primero nunca se estandarizo: cada fabricante hizo lo suyo.
/// Por eso es configuracion y no una constante.
public enum WordOrder
{
    /// Palabra alta primero (big-endian de 32 bits). Lo mas comun.
    HighFirst,

    /// Palabra baja primero. Tipico de equipos con origen en el mundo Modicon.
    LowFirst
}

/// Convierte entre float de 32 bits y el par de registros de 16 bits que
/// viaja por Modbus. Es la misma operacion de los dos lados del cable:
/// el esclavo la usa para escribir y el driver para leer.
public static class ModbusFloat
{
    /// Parte un valor en dos registros.
    public static (ushort First, ushort Second) ToRegisters(float value, WordOrder order)
    {
        // Bits crudos del float, tal como estan en memoria.
        var bits = BitConverter.SingleToUInt32Bits(value);

        var high = (ushort)(bits >> 16);
        var low = (ushort)(bits & 0xFFFF);

        return order == WordOrder.HighFirst ? (high, low) : (low, high);
    }

    /// Rearma el valor a partir de los dos registros.
    public static float FromRegisters(ushort first, ushort second, WordOrder order)
    {
        var (high, low) = order == WordOrder.HighFirst ? (first, second) : (second, first);

        var bits = ((uint)high << 16) | low;
        return BitConverter.UInt32BitsToSingle(bits);
    }
}