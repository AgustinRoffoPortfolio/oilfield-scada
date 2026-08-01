namespace Alarms;

/// Parametros del motor de alarmas.
public sealed class AlarmOptions
{
    /// Cada cuanto se leen los ultimos valores y se evaluan los umbrales.
    public int PollIntervalMs { get; set; } = 2000;

    /// Margen de retorno, como porcentaje del rango de ingenieria del tag.
    /// Evita que un valor oscilando sobre el limite genere alarmas en rafaga.
    public double HysteresisPercent { get; set; } = 2.0;

    /// Si el ultimo valor de un tag es mas viejo que esto, no se evalua:
    /// no hay dato actual contra el cual comparar.
    public int StaleDataSeconds { get; set; } = 30;
}