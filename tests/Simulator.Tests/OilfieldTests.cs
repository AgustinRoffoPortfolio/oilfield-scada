using Shared;
using Xunit;

namespace Simulator.Tests;

/// <summary>Tests del acoplamiento entre pozos, separador y ducto.</summary>
public class OilfieldTests
{
    private static Oilfield CreateSettledField()
    {
        var field = new Oilfield();
        for (int i = 0; i < 90; i++) field.Step(1.0);
        return field;
    }

    [Fact]
    public void TotalesDelYacimiento_SonLaSumaDeLosPozos()
    {
        var field = CreateSettledField();

        Assert.Equal(field.Wells.Sum(w => w.OilRate.TrueValue), field.TotalOilRate, 6);
        Assert.Equal(field.Wells.Sum(w => w.WaterRate.TrueValue), field.TotalWaterRate, 6);
        Assert.Equal(field.Wells.Sum(w => w.GasRate.TrueValue), field.TotalGasRate, 6);
    }

    [Fact]
    public void FallaEnUnPozo_SePropagaHastaElDucto()
    {
        var field = CreateSettledField();
        double flow0 = field.Pipeline.TotalFlow.TrueValue;

        field.GetWell("POZO-B").StartPumpDegradation(minutesToFailure: 1.0);
        for (int i = 0; i < 30; i++) field.Step(1.0);

        // Nadie le avisó al ducto: le llega menos líquido porque hay menos.
        Assert.True(field.Pipeline.TotalFlow.TrueValue < flow0);
    }

    [Fact]
    public void ElDucto_PierdePresionEntreEntradaYSalida()
    {
        var field = CreateSettledField();

        Assert.True(field.Pipeline.OutletPressure.TrueValue < field.Pipeline.InletPressure.TrueValue);
    }
}