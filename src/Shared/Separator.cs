namespace Shared;

/// <summary>
/// Separador de producción: recibe todo lo que producen los pozos juntos y
/// separa gas por arriba y líquido por abajo. La presión la fija el gas que
/// entra; el nivel lo maneja un control que trata de mantenerlo a media altura.
/// </summary>
public class Separator
{
    private double pressure = 10.0; // bar
    private double level = 50.0;    // %

    private const double NominalGasRate = 33000.0;   // Nm³/d de diseño
    private const double NominalLiquidRate = 500.0;  // m³/d de diseño
    private const double MinPressure = 4.0;          // bar sin gas entrando
    private const double PressureGain = 8.0;         // bar que suma el gas a caudal nominal
    private const double PressureTimeConstant = 15.0;// s que tarda en acomodarse
    private const double LevelSetpoint = 50.0;       // % que busca el control
    private const double LevelGain = 30.0;           // % de desvío a caudal doble
    private const double LevelTimeConstant = 25.0;   // s que tarda en acomodarse

    public Sensor Pressure { get; }
    public Sensor Level { get; }

    public Separator(int seed)
    {
        Pressure = new Sensor("Sep_P",     "bar", 0.05, seed + 1);
        Level    = new Sensor("Sep_level", "%",   0.20, seed + 2);
    }

    /// <summary>Avanza el separador con el caudal que le llega de los pozos.</summary>
    public void Step(double liquidRate, double gasRate, double dt)
    {
        // Más gas entrando, más presión en el recipiente.
        double targetPressure = MinPressure + PressureGain * (gasRate / NominalGasRate);

        // Un recipiente tiene volumen: no salta, se acomoda de a poco.
        pressure += (targetPressure - pressure) * dt / PressureTimeConstant;

        // El control de nivel busca el 50%, pero más líquido entrando lo empuja
        // para arriba hasta que la válvula de salida lo compensa.
        double targetLevel = LevelSetpoint + LevelGain * (liquidRate / NominalLiquidRate - 1);
        targetLevel = Math.Clamp(targetLevel, 5.0, 95.0);
        level += (targetLevel - level) * dt / LevelTimeConstant;

        Pressure.Update(pressure, dt);
        Level.Update(level, dt);
    }
}