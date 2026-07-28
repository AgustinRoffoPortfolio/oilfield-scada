using Shared;
using Xunit;

namespace Simulator.Tests;

/// <summary>
/// Tests del modelo físico del pozo. Todos comparan contra TrueValue en vez
/// de Value: nos interesa verificar la física, no el ruido del instrumento.
/// </summary>
public class WellTests
{
    /// <summary>Crea un pozo y lo deja estabilizado en su frecuencia de operación.</summary>
    private static Well CreateSettledWell(double setpoint = 52.0)
    {
        var well = new Well("TEST", seed: 1) { FrequencySetpoint = setpoint };
        for (int i = 0; i < 60; i++) well.Step(1.0);
        return well;
    }

    [Fact]
    public void MayorFrecuencia_ProduceMasCaudalYMasCorriente()
    {
        var slow = CreateSettledWell(40.0);
        var fast = CreateSettledWell(58.0);

        Assert.True(fast.OilRate.TrueValue > slow.OilRate.TrueValue);
        Assert.True(fast.EspCurrent.TrueValue > slow.EspCurrent.TrueValue);
        Assert.True(fast.WellheadPressure.TrueValue > slow.WellheadPressure.TrueValue);
    }

    [Fact]
    public void DegradacionDeBomba_SubeVibracionYCorriente_YBajaCaudal()
    {
        var well = CreateSettledWell();
        double vib0 = well.EspVibration.TrueValue;
        double current0 = well.EspCurrent.TrueValue;
        double oil0 = well.OilRate.TrueValue;

        well.StartPumpDegradation(minutesToFailure: 1.0);
        for (int i = 0; i < 30; i++) well.Step(1.0);

        Assert.True(well.PumpWear > 0);
        Assert.True(well.EspVibration.TrueValue > vib0);
        Assert.True(well.EspCurrent.TrueValue > current0);
        Assert.True(well.OilRate.TrueValue < oil0);
    }

    [Fact]
    public void ObstruccionDeLinea_SubePresionYBajaCaudal()
    {
        var well = CreateSettledWell();
        double thp0 = well.WellheadPressure.TrueValue;
        double oil0 = well.OilRate.TrueValue;

        well.StartLineObstruction(minutesToFull: 1.0);
        for (int i = 0; i < 30; i++) well.Step(1.0);

        Assert.True(well.WellheadPressure.TrueValue > thp0);
        Assert.True(well.OilRate.TrueValue < oil0);
    }

    /// <summary>
    /// El test más valioso del conjunto: las dos fallas bajan el caudal, pero
    /// mueven la presión en direcciones opuestas. Esa diferencia es lo que
    /// permite diagnosticar cuál de las dos está pasando.
    /// </summary>
    [Fact]
    public void BombaGastadaYLineaTapada_TienenFirmasDePresionOpuestas()
    {
        var worn = CreateSettledWell();
        var blocked = CreateSettledWell();
        double thp0 = worn.WellheadPressure.TrueValue;

        worn.StartPumpDegradation(minutesToFailure: 1.0);
        blocked.StartLineObstruction(minutesToFull: 1.0);
        for (int i = 0; i < 30; i++)
        {
            worn.Step(1.0);
            blocked.Step(1.0);
        }

        Assert.True(worn.WellheadPressure.TrueValue < thp0);    // la bomba gastada empuja menos
        Assert.True(blocked.WellheadPressure.TrueValue > thp0); // el tapón acumula presión
    }

    [Fact]
    public void SensorCongelado_RepiteElUltimoValorAunqueLaRealidadCambie()
    {
        var well = CreateSettledWell();
        well.EspVibration.Freeze();
        double frozenReading = well.EspVibration.Value;

        well.StartPumpDegradation(minutesToFailure: 1.0);
        for (int i = 0; i < 30; i++) well.Step(1.0);

        Assert.Equal(frozenReading, well.EspVibration.Value);
        Assert.True(well.EspVibration.TrueValue > frozenReading);
    }

    [Fact]
    public void DerivaDeSensor_SeparaLaLecturaDeLaRealidad()
    {
        var well = CreateSettledWell();
        double truth = well.CasingPressure.TrueValue;

        well.CasingPressure.StartDrift(unitsPerMinute: 6.0);
        for (int i = 0; i < 60; i++) well.Step(1.0);

        // La física no cambió...
        Assert.Equal(truth, well.CasingPressure.TrueValue, 3);
        // ...pero el instrumento miente por unos 6 bar.
        Assert.True(well.CasingPressure.Value - well.CasingPressure.TrueValue > 5.0);
    }
}