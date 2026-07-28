namespace Shared;

/// <summary>
/// Modelo físico de un pozo con bomba electrosumergible (ESP).
/// Todo se deriva de la frecuencia del variador: más frecuencia,
/// más caudal, más corriente y más presión en boca.
/// </summary>
public class Well
{
    private readonly Random random;

    // --- Constantes de diseño del pozo ---
    private const double NominalFrequency = 60.0;    // Hz
    private const double MaxOilRate = 120.0;         // m³/d a frecuencia nominal
    private const double NominalCurrent = 75.0;      // A a frecuencia nominal
    private const double MinWellheadPressure = 15.0; // bar sin caudal
    private const double PressureGain = 25.0;        // bar de fricción a caudal máximo
    private const double RampRate = 0.5;             // Hz por segundo

    public string Name { get; }

    // --- Tags ---
    public double EspFrequency { get; private set; } = 50.0; // Hz
    public double OilRate { get; private set; }              // m³/d
    public double EspCurrent { get; private set; }           // A
    public double WellheadPressure { get; private set; }     // bar

    /// <summary>Frecuencia que pide el operador; el variador la alcanza con rampa.</summary>
    public double FrequencySetpoint { get; set; } = 52.0;

    public Well(string name, int seed)
    {
        Name = name;
        random = new Random(seed);
    }

    /// <summary>Avanza la simulación dt segundos.</summary>
    public void Step(double dt)
    {
        // El variador no salta: se acerca al setpoint de a poco.
        double error = FrequencySetpoint - EspFrequency;
        double maxChange = RampRate * dt;
        EspFrequency += Math.Clamp(error, -maxChange, maxChange);

        double ratio = EspFrequency / NominalFrequency;

        // Ley de afinidad: el caudal es proporcional a la velocidad de la bomba.
        OilRate = MaxOilRate * ratio + Noise(0.5);

        // Potencia ∝ velocidad³ y tensión ∝ frecuencia, así que corriente ∝ velocidad².
        EspCurrent = NominalCurrent * ratio * ratio + Noise(0.3);

        // Presión en boca: la estática más la fricción, que crece con el caudal².
        double flowFraction = OilRate / MaxOilRate;
        WellheadPressure = MinWellheadPressure + PressureGain * flowFraction * flowFraction + Noise(0.1);
    }

    /// <summary>Ruido de medición: chico y centrado en cero.</summary>
    private double Noise(double amplitude) => (random.NextDouble() - 0.5) * 2 * amplitude;
}