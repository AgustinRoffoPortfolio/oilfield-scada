namespace Shared;

/// <summary>
/// Modelo físico de un pozo con bomba electrosumergible (ESP).
/// Todo se deriva de la frecuencia del variador: la bomba levanta un caudal
/// de líquido, ese líquido se reparte entre petróleo y agua según el corte
/// de agua, y el gas sale en proporción al petróleo producido.
/// </summary>
public class Well
{
    private readonly Random random;
    private double elapsedSeconds;

    // --- Constantes de diseño del pozo ---
    private const double NominalFrequency = 60.0;    // Hz
    private const double MaxLiquidRate = 200.0;      // m³/d de líquido a frecuencia nominal
    private const double NominalCurrent = 75.0;      // A a frecuencia nominal
    private const double MinWellheadPressure = 15.0; // bar sin caudal
    private const double PressureGain = 25.0;        // bar de fricción a caudal máximo
    private const double RampRate = 0.5;             // Hz por segundo
    private const double GasOilRatio = 100.0;        // Nm³ de gas por m³ de petróleo
    private const double AmbientTemperature = 25.0;  // °C
    private const double ReservoirHeat = 70.0;       // °C que aporta el fluido a caudal pleno
    private const double BaseVibration = 1.0;        // mm/s con la bomba girando lento
    private const double MinFrequencyToRun = 20.0;   // Hz por debajo de los cuales está parada

    /// <summary>Cuánto sube el corte de agua por día. Acelerado para la demo:
    /// en un pozo real esto pasa a lo largo de meses o años.</summary>
    private const double WaterCutRatePerDay = 0.02;

    public string Name { get; }

    // --- Tags ---
    public double EspFrequency { get; private set; } = 50.0; // Hz
    public double EspCurrent { get; private set; }           // A
    public double EspVibration { get; private set; }         // mm/s
    public double OilRate { get; private set; }              // m³/d
    public double WaterRate { get; private set; }            // m³/d
    public double GasRate { get; private set; }              // Nm³/d
    public double WellheadPressure { get; private set; }     // bar (THP)
    public double CasingPressure { get; private set; }       // bar (CHP)
    public double HeadTemperature { get; private set; }      // °C
    public WellStatus Status { get; private set; } = WellStatus.Running;

    /// <summary>Fracción de agua en el líquido producido (0 a 1).</summary>
    public double WaterCut { get; private set; }

    /// <summary>Frecuencia que pide el operador; el variador la alcanza con rampa.</summary>
    public double FrequencySetpoint { get; set; } = 52.0;

    public Well(string name, int seed, double initialWaterCut = 0.35)
    {
        Name = name;
        random = new Random(seed);
        WaterCut = initialWaterCut;
    }

    /// <summary>Avanza la simulación dt segundos.</summary>
    public void Step(double dt)
    {
        elapsedSeconds += dt;

        // El variador no salta: se acerca al setpoint de a poco.
        double error = FrequencySetpoint - EspFrequency;
        double maxChange = RampRate * dt;
        EspFrequency += Math.Clamp(error, -maxChange, maxChange);

        double ratio = EspFrequency / NominalFrequency;
        Status = EspFrequency < MinFrequencyToRun ? WellStatus.Stopped : WellStatus.Running;

        // El yacimiento se va inundando: el corte de agua sube despacio y no vuelve.
        WaterCut = Math.Min(0.95, WaterCut + WaterCutRatePerDay * dt / 86400.0);

        // Ley de afinidad: el caudal de líquido es proporcional a la velocidad de la bomba.
        double liquidRate = MaxLiquidRate * ratio;
        OilRate = liquidRate * (1 - WaterCut) + Noise(0.5);
        WaterRate = liquidRate * WaterCut + Noise(0.5);

        // El gas viene disuelto en el petróleo: sale en proporción a lo que se produce.
        GasRate = OilRate * GasOilRatio + Noise(50);

        // Potencia ∝ velocidad³ y tensión ∝ frecuencia, así que corriente ∝ velocidad².
        EspCurrent = NominalCurrent * ratio * ratio + Noise(0.3);

        // Vibración: crece con la velocidad. Acá se van a ver las fallas de la bomba.
        EspVibration = BaseVibration + 1.5 * ratio * ratio + Noise(0.05);

        // Presión en boca: la estática más la fricción, que crece con el caudal².
        double flowFraction = liquidRate / MaxLiquidRate;
        WellheadPressure = MinWellheadPressure + PressureGain * flowFraction * flowFraction + Noise(0.1);

        // Presión de casing: el gas acumulado en el anular, más baja y más estable.
        CasingPressure = 8.0 + 10.0 * ratio + Noise(0.15);

        // Temperatura en boca: cuanto más caudal, menos se enfría el fluido al subir.
        HeadTemperature = AmbientTemperature + ReservoirHeat * flowFraction + Noise(0.2);
    }

    /// <summary>Ruido de medición: chico y centrado en cero.</summary>
    private double Noise(double amplitude) => (random.NextDouble() - 0.5) * 2 * amplitude;
}