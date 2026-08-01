namespace WebApp;

/// Una alarma tal como la ve el panel. El estado se calcula en la consulta,
/// a partir de que marcas de tiempo tiene cargadas.
public sealed record AlarmView
{
    public long AlarmId { get; init; }
    public string Name { get; init; } = "";
    public string Equipment { get; init; } = "";
    public string Variable { get; init; } = "";
    public string? Unit { get; init; }
    public string Severity { get; init; } = "";
    public string Direction { get; init; } = "";
    public double LimitValue { get; init; }
    public double RaiseValue { get; init; }
    public DateTime RaisedAt { get; init; }
    public DateTime? AckedAt { get; init; }
    public DateTime? ClearedAt { get; init; }
    public double? ClearValue { get; init; }

    /// active | acked | unacked_cleared | closed
    public string State { get; init; } = "";
}