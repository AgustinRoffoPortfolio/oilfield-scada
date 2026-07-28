namespace Shared;

/// <summary>
/// Tramo de ducto que saca el líquido del separador. La caída de presión entre
/// entrada y salida crece con el cuadrado del caudal: es el rozamiento contra
/// la pared del caño, que aumenta mucho más rápido que la velocidad del fluido.
/// </summary>
public class Pipeline
{
    private const double NominalFlow = 500.0;      // m³/d de diseño
    private const double MaxPressureDrop = 6.0;    // bar de caída a caudal nominal
    private const double InletLoss = 0.5;          // bar que se pierden en la válvula de salida

    public Sensor InletPressure { get; }
    public Sensor OutletPressure { get; }
    public Sensor TotalFlow { get; }

    public Pipeline(int seed)
    {
        InletPressure  = new Sensor("Pipe_P_in",  "bar",  0.08, seed + 1);
        OutletPressure = new Sensor("Pipe_P_out", "bar",  0.08, seed + 2);
        TotalFlow      = new Sensor("Pipe_Q",     "m3/d", 1.00, seed + 3);
    }

    /// <summary>Avanza el ducto con lo que sale del separador.</summary>
    public void Step(double liquidRate, double upstreamPressure, double dt)
    {
        double inlet = Math.Max(0, upstreamPressure - InletLoss);

        // Caída por fricción ∝ caudal².
        double flowFraction = liquidRate / NominalFlow;
        double drop = MaxPressureDrop * flowFraction * flowFraction;

        InletPressure.Update(inlet, dt);
        OutletPressure.Update(Math.Max(0, inlet - drop), dt);
        TotalFlow.Update(liquidRate, dt);
    }
}