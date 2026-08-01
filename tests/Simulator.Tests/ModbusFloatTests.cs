using Shared;

namespace Simulator.Tests;

/// La conversion float <-> registros es la pieza mas facil de romper y la mas
/// dificil de depurar en campo: un orden de palabras equivocado no da error,
/// da un numero absurdo. Por eso se testea de las dos puntas.
public class ModbusFloatTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(-1f)]
    [InlineData(52.01f)]
    [InlineData(0.5f)]
    [InlineData(20000f)]
    [InlineData(-273.15f)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    [InlineData(float.Epsilon)]
    public void RoundTrip_PreservaElValor_EnAmbosOrdenes(float value)
    {
        foreach (var order in new[] { WordOrder.HighFirst, WordOrder.LowFirst })
        {
            var (first, second) = ModbusFloat.ToRegisters(value, order);
            var result = ModbusFloat.FromRegisters(first, second, order);

            Assert.Equal(value, result);
        }
    }

    [Fact]
    public void LosDosOrdenes_IntercambianLasPalabras()
    {
        var high = ModbusFloat.ToRegisters(52.01f, WordOrder.HighFirst);
        var low = ModbusFloat.ToRegisters(52.01f, WordOrder.LowFirst);

        Assert.Equal(high.First, low.Second);
        Assert.Equal(high.Second, low.First);
    }

    [Fact]
    public void LeerConElOrdenEquivocado_DaOtroValor()
    {
        // El error clasico de integracion: el equipo publica en un orden y el
        // driver lee en el otro. No falla, miente. Este test documenta el sintoma.
        var (first, second) = ModbusFloat.ToRegisters(52.01f, WordOrder.HighFirst);
        var misread = ModbusFloat.FromRegisters(first, second, WordOrder.LowFirst);

        Assert.NotEqual(52.01f, misread);
    }

    [Fact]
    public void ValorConocido_SeParteEnLosRegistrosEsperados()
    {
        // 1.0f en IEEE 754 es 0x3F800000. Con la palabra alta primero,
        // los registros tienen que ser 0x3F80 y 0x0000.
        var (first, second) = ModbusFloat.ToRegisters(1.0f, WordOrder.HighFirst);

        Assert.Equal((ushort)0x3F80, first);
        Assert.Equal((ushort)0x0000, second);
    }
}