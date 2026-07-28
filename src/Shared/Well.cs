namespace Shared;

/// <summary>
/// Modelo físico de un pozo con bomba electrosumergible (ESP).
/// El pozo calcula los valores físicos reales; los sensores son los que
/// los miden y los reportan, con su ruido y sus fallas propias.
/// </summary>
public class Well
{
    private double elapsedSeconds;
    private double wearRatePerSecond;
    private double frequency = 50.0; // Hz reales del variador

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

    // --- Efectos de la degradación de la bomba a desgaste total ---
    private const double WearVibrationRise = 7.0;  // mm/s que suma la vibración
    private const double WearCurrentRise = 15.0;   // A que suma la corriente
    private const double WearFlowLoss = 0.35;      // fracción de caudal que se pierde

    public string Name { get; }

    // --- Sensores (lo que el SCADA puede leer) ---
    public Sensor EspFrequency { get; }
    public Sensor EspCurrent { get; }
    public Sensor EspVibration { get; }
    public Sensor OilRate { get; }
    public Sensor WaterRate { get; }
    public Sensor GasRate { get; }
    public Sensor WellheadPressure { get; }
    public Sensor CasingPressure { get; }
    public Sensor HeadTemperature { get; }

    /// <summary>Estado operativo. No es una medición analógica, no lleva sensor.</summary>
    public WellStatus Status { get; private set; } = WellStatus.Running;

    /// <summary>Fracción de agua en el líquido producido (0 a 1).</summary>
    public double WaterCut { get; private set; }

    /// <summary>Desgaste acumulado de la bomba, de 0 (sana) a 1 (falla inminente).</summary>
    public double PumpWear { get; private set; }

    /// <summary>Frecuencia que pide el operador; el variador la alcanza con rampa.</summary>
    public double FrequencySetpoint { get; set; } = 52.0;

    public Well(string name, int seed, double initialWaterCut = 0.35)
    {
        Name = name;
        WaterCut = initialWaterCut;

        EspFrequency     = new Sensor("ESP_freq",    "Hz",    0.02, seed + 1);
        EspCurrent       = new Sensor("ESP_current", "A",     0.30, seed + 2);
        EspVibration     = new Sensor("ESP_vib",     "mm/s",  0.05, seed + 3);
        OilRate          = new Sensor("Q_oil",       "m3/d",  0.50, seed + 4);
        WaterRate        = new Sensor("Q_water",     "m3/d",  0.50, seed + 5);
        GasRate          = new Sensor("Q_gas",       "Nm3/d", 50.0, seed + 6);
        WellheadPressure = new Sensor("THP",         "bar",   0.10, seed + 7);
        CasingPressure   = new Sensor("CHP",         "bar",   0.15, seed + 8);
        HeadTemperature  = new Sensor("T_head",      "C",     0.20, seed + 9);
    }

    /// <summary>Avanza la simulación dt segundos.</summary>
    public void Step(double dt)
    {
        elapsedSeconds += dt;

        // El desgaste solo avanza; una bomba no se repara sola.
        PumpWear = Math.Min(1.0, PumpWear + wearRatePerSecond * dt);

        // El variador no salta: se acerca al setpoint de a poco.
        double error = FrequencySetpoint - frequency;
        double maxChange = RampRate * dt;
        frequency += Math.Clamp(error, -maxChange, maxChange);

        double ratio = frequency / NominalFrequency;
        Status = frequency < MinFrequencyToRun ? WellStatus.Stopped : WellStatus.Running;

        // El yacimiento se va inundando: el corte de agua sube despacio y no vuelve.
        WaterCut = Math.Min(0.95, WaterCut + WaterCutRatePerDay * dt / 86400.0);

        // Ley de afinidad, castigada por el desgaste: los impulsores gastados
        // mueven menos líquido a la misma velocidad.
        double liquidRate = MaxLiquidRate * ratio * (1 - WearFlowLoss * PumpWear);
        double flowFraction = liquidRate / MaxLiquidRate;

        double oil = liquidRate * (1 - WaterCut);
        double water = liquidRate * WaterCut;

        // Potencia ∝ velocidad³ y tensión ∝ frecuencia, así que corriente ∝ velocidad².
        // Más rozamiento en los cojinetes, más corriente para el mismo trabajo.
        double current = NominalCurrent * ratio * ratio + WearCurrentRise * PumpWear;

        // La vibración es el síntoma más temprano y más claro del desgaste.
        double vibration = BaseVibration + 1.5 * ratio * ratio + WearVibrationRise * PumpWear;

        // Presión en boca: la estática más la fricción, que crece con el caudal².
        double thp = MinWellheadPressure + PressureGain * flowFraction * flowFraction;

        // Los instrumentos miden la realidad que acabamos de calcular.
        EspFrequency.Update(frequency);
        EspCurrent.Update(current);
        EspVibration.Update(vibration);
        OilRate.Update(oil);
        WaterRate.Update(water);
        GasRate.Update(oil * GasOilRatio);           // el gas sale disuelto en el petróleo
        WellheadPressure.Update(thp);
        CasingPressure.Update(8.0 + 10.0 * ratio);   // gas acumulado en el anular
        HeadTemperature.Update(AmbientTemperature + ReservoirHeat * flowFraction);
    }

    /// <summary>Arranca la degradación gradual de la bomba.</summary>
    /// <param name="minutesToFailure">Minutos hasta el desgaste total.</param>
    public void StartPumpDegradation(double minutesToFailure = 2.0)
    {
        wearRatePerSecond = 1.0 / (minutesToFailure * 60.0);
    }

    /// <summary>Repara la bomba y detiene la degradación.</summary>
    public void RepairPump()
    {
        wearRatePerSecond = 0;
        PumpWear = 0;
    }
}