namespace Shared;

/// <summary>
/// El yacimiento completo. Los pozos producen, el separador recibe la suma de
/// todo lo que producen, y el ducto transporta el líquido que sale del separador.
/// Nada se inventa en cada etapa: lo que entra es lo que salió de la anterior.
/// </summary>
public class Oilfield
{
    public IReadOnlyList<Well> Wells { get; }
    public Separator Separator { get; }
    public Pipeline Pipeline { get; }

    // --- Totales del yacimiento ---
    public double TotalOilRate { get; private set; }    // m³/d
    public double TotalWaterRate { get; private set; }  // m³/d
    public double TotalGasRate { get; private set; }    // Nm³/d
    public double TotalLiquidRate => TotalOilRate + TotalWaterRate;

    public double ElapsedSeconds { get; private set; }

    public Oilfield()
    {
        // Cada pozo con su propia historia: distinto corte de agua y distinta
        // frecuencia de operación, para que no sean tres copias iguales.
        Wells = new List<Well>
        {
            new Well("POZO-A", seed: 42, initialWaterCut: 0.35) { FrequencySetpoint = 52.0 },
            new Well("POZO-B", seed: 77, initialWaterCut: 0.55) { FrequencySetpoint = 48.0 },
            new Well("POZO-C", seed: 13, initialWaterCut: 0.20) { FrequencySetpoint = 55.0 }
        };

        Separator = new Separator(seed: 200);
        Pipeline = new Pipeline(seed: 300);
    }

    /// <summary>Avanza todo el yacimiento dt segundos.</summary>
    public void Step(double dt)
    {
        ElapsedSeconds += dt;

        double oil = 0, water = 0, gas = 0;

        foreach (var well in Wells)
        {
            well.Step(dt);

            // Sumamos el valor FÍSICO, no el que reporta el sensor: al separador
            // le llega el fluido real aunque el caudalímetro esté congelado.
            oil += well.OilRate.TrueValue;
            water += well.WaterRate.TrueValue;
            gas += well.GasRate.TrueValue;
        }

        TotalOilRate = oil;
        TotalWaterRate = water;
        TotalGasRate = gas;

        Separator.Step(TotalLiquidRate, TotalGasRate, dt);
        Pipeline.Step(TotalLiquidRate, Separator.Pressure.TrueValue, dt);
    }

    /// <summary>Busca un pozo por nombre.</summary>
    public Well GetWell(string name) => Wells.First(w => w.Name == name);
}