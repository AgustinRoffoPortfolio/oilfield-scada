namespace WebApp;

/// Un intervalo del historial: promedio, minimo y maximo de ese tramo.
/// Se devuelven los tres porque promediar solo esconderia los picos.
public sealed record HistoryPoint
{
    public DateTime Ts { get; init; }
    public double Avg { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
}